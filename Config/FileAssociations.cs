using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Config;

/// <summary>
/// 現在の exe を、ユーザーごとのファイル関連付け（既定アプリ）にする。
/// </summary>
internal static class FileAssociations
{
    public const string ProgIdPrefix = "MgaSonicAnvil";

    private const string ClassesRoot = @"Software\Classes";
    private const string FileExtsRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";
    private const string PreviousRoot = @"Software\MGA\MGA Sonic Anvil\FileAssociations";
    private const string CapabilitiesPath = @"Software\MGA\MGA Sonic Anvil\Capabilities";
    private const string RegisteredApplications = @"Software\RegisteredApplications";
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdlist = 0x0000;

    public static IReadOnlyList<string> Extensions => AudioCodec.OpenExtensions;

    public static string NormalizeExtension(string extension)
    {
        var text = (extension ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Extension is empty.", nameof(extension));
        }

        if (text[0] != '.')
        {
            text = "." + text;
        }

        return text.ToLowerInvariant();
    }

    public static string ProgIdFor(string extension) =>
        ProgIdPrefix + NormalizeExtension(extension);

    public static string FormatLabel(string extension)
    {
        var ext = NormalizeExtension(extension);
        var kind = ext switch
        {
            ".mp3" => "MP3",
            ".aif" or ".aiff" => "AIFF",
            _ => "Wave",
        };
        return $"{kind} ({ext})";
    }

    public static string BuildOpenCommand(string exePath) =>
        $"\"{exePath}\" \"%1\"";

    public static bool TryGetCommandExePath(string? command, out string exePath)
    {
        exePath = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            if (end <= 1)
            {
                return false;
            }

            exePath = text[1..end];
        }
        else
        {
            var space = text.IndexOf(' ');
            exePath = space < 0 ? text : text[..space];
        }

        return exePath.Length > 0;
    }

    public static bool CommandTargets(string? command, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !TryGetCommandExePath(command, out var fromCommand))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(fromCommand),
                Path.GetFullPath(exePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(fromCommand, exePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsOurProgId(string? progId, string? extension = null)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return false;
        }

        if (extension is not null)
        {
            return progId.Equals(ProgIdFor(extension), StringComparison.OrdinalIgnoreCase);
        }

        return progId.StartsWith(ProgIdPrefix + ".", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveExePath()
    {
        if (TryExistingFullPath(Environment.ProcessPath, out var processPath))
        {
            return processPath;
        }

        try
        {
            if (TryExistingFullPath(Process.GetCurrentProcess().MainModule?.FileName, out var modulePath))
            {
                return modulePath;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
        }

        return null;
    }

    public static bool IsAssociated(string extension)
    {
        var ext = NormalizeExtension(extension);
        var progId = ReadEffectiveProgId(ext);
        if (!IsOurProgId(progId, ext))
        {
            return false;
        }

        var exe = ResolveExePath();
        return exe is not null && CommandTargets(ReadOpenCommand(progId), exe);
    }

    public static void SetAssociated(string extension, bool associated)
    {
        var ext = NormalizeExtension(extension);
        if (associated)
        {
            Associate(ext);
            return;
        }

        Disassociate(ext);
    }

    private static void Associate(string ext)
    {
        var exe = ResolveExePath()
            ?? throw new InvalidOperationException(UiStrings.ErrFileAssociationNoExe);
        var progId = ProgIdFor(ext);
        var current = ReadEffectiveProgId(ext);
        if (!IsOurProgId(current, ext))
        {
            WritePreviousProgId(ext, current);
        }

        WriteProgId(progId, exe);
        WriteExtensionDefault(ext, progId);
        WriteOpenWith(ext, progId);
        WriteApplication(exe, ext, add: true);
        WriteCapabilities(ext, progId, add: true);
        TryDeleteUserChoice(ext);
        NotifyShell();
        if (!IsAssociated(ext))
        {
            throw new InvalidOperationException(UiStrings.ErrFileAssociationNotDefault);
        }
    }

    private static void Disassociate(string ext)
    {
        var progId = ProgIdFor(ext);
        var previous = ReadPreviousProgId(ext);
        if (IsOurProgId(ReadClassesProgId(ext, userOnly: true), ext))
        {
            RestoreExtensionDefault(ext, previous);
        }

        RemoveOpenWith(ext, progId);
        TryDeleteKey($@"{ClassesRoot}\{progId}");
        var exe = ResolveExePath();
        if (exe is not null)
        {
            WriteApplication(exe, ext, add: false);
        }

        WriteCapabilities(ext, progId, add: false);
        if (IsOurProgId(ReadUserChoiceProgId(ext), ext))
        {
            TryDeleteUserChoice(ext);
        }

        TryDeleteKey($@"{PreviousRoot}\{ext}");
        NotifyShell();
    }

    private static string? ReadEffectiveProgId(string ext)
    {
        var userChoice = ReadUserChoiceProgId(ext);
        if (!string.IsNullOrWhiteSpace(userChoice))
        {
            return userChoice;
        }

        return ReadClassesProgId(ext, userOnly: false);
    }

    private static string? ReadUserChoiceProgId(string ext)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{FileExtsRoot}\{ext}\UserChoice");
        return TrimValue(key?.GetValue("ProgId") as string);
    }

    private static string? ReadClassesProgId(string ext, bool userOnly)
    {
        using var user = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}");
        var value = TrimValue(user?.GetValue(null) as string);
        if (!string.IsNullOrWhiteSpace(value) || userOnly)
        {
            return value;
        }

        using var merged = Registry.ClassesRoot.OpenSubKey(ext);
        return TrimValue(merged?.GetValue(null) as string);
    }

    private static string? ReadOpenCommand(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{progId}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    private static void WriteProgId(string progId, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}");
        key.SetValue(null, AppVersion.ProductName);
        key.SetValue("FriendlyTypeName", AppVersion.ProductName);
        using (var icon = key.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue(null, $"\"{exe}\",0");
        }

        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(null, BuildOpenCommand(exe));
    }

    private static void WriteExtensionDefault(string ext, string progId)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ext}");
        key.SetValue(null, progId);
    }

    private static void RestoreExtensionDefault(string ext, string? previous)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ext}");
        if (string.IsNullOrWhiteSpace(previous) || IsOurProgId(previous))
        {
            try
            {
                key.DeleteValue(string.Empty, throwOnMissingValue: false);
            }
            catch (ArgumentException)
            {
                key.SetValue(null, string.Empty);
            }

            return;
        }

        key.SetValue(null, previous);
    }

    private static void WriteOpenWith(string ext, string progId)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{ext}\OpenWithProgids");
        key.SetValue(progId, string.Empty);
    }

    private static void RemoveOpenWith(string ext, string progId)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}\OpenWithProgids", writable: true);
        try
        {
            key?.DeleteValue(progId, throwOnMissingValue: false);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void WriteApplication(string exe, string ext, bool add)
    {
        var appKey = $@"{ClassesRoot}\Applications\{Path.GetFileName(exe)}";
        using var root = Registry.CurrentUser.CreateSubKey(appKey);
        root.SetValue("FriendlyAppName", AppVersion.ProductName);
        using (var command = root.CreateSubKey(@"shell\open\command"))
        {
            command.SetValue(null, BuildOpenCommand(exe));
        }

        using var types = root.CreateSubKey("SupportedTypes");
        if (add)
        {
            types.SetValue(ext, string.Empty);
            return;
        }

        try
        {
            types.DeleteValue(ext, throwOnMissingValue: false);
        }
        catch (ArgumentException)
        {
        }
    }

    private static void WriteCapabilities(string ext, string progId, bool add)
    {
        using var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesPath);
        caps.SetValue("ApplicationName", AppVersion.ProductName);
        caps.SetValue("ApplicationDescription", AppVersion.ProductName);
        using var files = caps.CreateSubKey("FileAssociations");
        if (add)
        {
            files.SetValue(ext, progId);
        }
        else
        {
            try
            {
                files.DeleteValue(ext, throwOnMissingValue: false);
            }
            catch (ArgumentException)
            {
            }
        }

        using var registered = Registry.CurrentUser.CreateSubKey(RegisteredApplications);
        registered.SetValue(AppVersion.ProductName, CapabilitiesPath);
    }

    private static void WritePreviousProgId(string ext, string? previous)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{PreviousRoot}\{ext}");
        key.SetValue("PreviousProgId", previous ?? string.Empty);
    }

    private static string? ReadPreviousProgId(string ext)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{PreviousRoot}\{ext}");
        return TrimValue(key?.GetValue("PreviousProgId") as string);
    }

    private static void TryDeleteUserChoice(string ext)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{FileExtsRoot}\{ext}", writable: true);
            key?.DeleteSubKeyTree("UserChoice", throwOnMissingSubKey: false);
            key?.DeleteSubKeyTree("UserChoiceLatest", throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }

    private static void TryDeleteKey(string relativePath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
        {
        }
    }

    private static void NotifyShell() =>
        SHChangeNotify(ShcneAssocChanged, ShcnfIdlist, IntPtr.Zero, IntPtr.Zero);

    private static bool TryExistingFullPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        fullPath = Path.GetFullPath(path);
        return true;
    }

    private static string? TrimValue(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
