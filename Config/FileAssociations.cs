using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
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
    private const int AssocStrCommand = 1;
    private const int AssocStrExecutable = 2;
    private const int ErrorInsufficientBuffer = unchecked((int)0x8007007A);

    public static IReadOnlyList<string> Extensions => AudioCodec.PlayerOpenExtensions;

    /// <summary>編集オープンではなく、プレイヤー起動専用の拡張子か。</summary>
    public static bool IsPlayerOnlyExtension(string extension) =>
        NormalizeExtension(extension).Equals(".m4a", StringComparison.OrdinalIgnoreCase);

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
            ".m4a" => "M4A",
            ".aif" or ".aiff" => "AIFF",
            _ => "Wave",
        };
        var label = $"{kind} ({ext})";
        if (IsPlayerOnlyExtension(ext))
        {
            label = $"{label} — {UiStrings.LabelFileAssociationPlayerOnly}";
        }

        return label;
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

    public static bool CommandTargets(string? command, string exePath) =>
        TryGetCommandExePath(command, out var fromCommand) && SameAppPath(fromCommand, exePath, namesOnly: false);

    public static bool TargetsThisApp(string? commandOrPath, string exePath)
    {
        if (string.IsNullOrWhiteSpace(commandOrPath) || string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        if (TryGetCommandExePath(commandOrPath, out var fromCommand)
            && SameAppPath(fromCommand, exePath, namesOnly: true))
        {
            return true;
        }

        var trimmed = commandOrPath.Trim().Trim('"');
        return trimmed.Length > 0 && SameAppPath(trimmed, exePath, namesOnly: true);
    }

    public static bool IsApplicationsProgId(string? progId, string exePath) =>
        IsApplicationsProgIdName(progId, Path.GetFileName(exePath));

    public static bool IsApplicationsProgIdName(string? progId, string exeFileName)
    {
        const string prefix = @"Applications\";
        if (string.IsNullOrWhiteSpace(progId)
            || string.IsNullOrWhiteSpace(exeFileName)
            || !progId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(progId[prefix.Length..], exeFileName, StringComparison.OrdinalIgnoreCase);
    }

    public static string CurrentExeFileName()
    {
        var exe = ResolveExePath();
        if (exe is not null && !IsRuntimeHost(exe))
        {
            return Path.GetFileName(exe);
        }

        return "MGA Sonic Anvil.exe";
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
        if (TryExistingFullPath(Environment.ProcessPath, out var processPath) && !IsRuntimeHost(processPath))
        {
            return processPath;
        }

        try
        {
            if (TryExistingFullPath(Process.GetCurrentProcess().MainModule?.FileName, out var modulePath)
                && !IsRuntimeHost(modulePath))
            {
                return modulePath;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
        }

        if (TryExistingFullPath(Path.Combine(AppContext.BaseDirectory, AppVersion.ProductName + ".exe"), out var local))
        {
            return local;
        }

        return TryExistingFullPath(Environment.ProcessPath, out processPath) ? processPath : null;
    }

    public static bool IsRuntimeHost(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var name = Path.GetFileName(path);
        return name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("testhost.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("testhost.dll", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAssociated(string extension)
    {
        var ext = NormalizeExtension(extension);
        var exeName = CurrentExeFileName();
        var exe = ResolveExePath();
        var progId = ReadEffectiveProgId(ext);
        if (IsOurProgId(progId, ext) || IsApplicationsProgIdName(progId, exeName))
        {
            return true;
        }

        if (TargetsThisApp(QueryAssoc(ext, AssocStrExecutable), exe ?? exeName)
            || TargetsThisApp(QueryAssoc(ext, AssocStrCommand), exe ?? exeName))
        {
            return true;
        }

        return FileNameEquals(QueryAssoc(ext, AssocStrExecutable), exeName)
            || FileNameEquals(ReadOpenCommand(progId), exeName);
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
        if (!IsOurProgId(current, ext) && !IsApplicationsProgId(current, exe))
        {
            WritePreviousProgId(ext, current);
        }

        WriteProgId(progId, exe, ext);
        WriteExtensionDefault(ext, progId);
        WriteOpenWith(ext, progId);
        WriteApplication(exe, ext, add: true);
        WriteCapabilities(ext, progId, add: true);
        WriteUserChoice(ext, progId);
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
        var userChoice = ReadUserChoiceProgId(ext);
        if (IsOurProgId(userChoice, ext) || IsApplicationsProgIdName(userChoice, CurrentExeFileName()))
        {
            TryDeleteUserChoice(ext);
        }

        TryDeleteKey($@"{PreviousRoot}\{ext}");
        NotifyShell();
        if (IsAssociated(ext))
        {
            throw new InvalidOperationException(UiStrings.ErrFileAssociationStillDefault);
        }
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
        return TrimValue(key?.GetValue("ProgId")?.ToString());
    }

    private static string? ReadClassesProgId(string ext, bool userOnly)
    {
        using var user = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{ext}");
        var value = ReadDefault(user);
        if (!string.IsNullOrWhiteSpace(value) || userOnly)
        {
            return value;
        }

        using var merged = Registry.ClassesRoot.OpenSubKey(ext);
        return ReadDefault(merged);
    }

    private static string? ReadOpenCommand(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            return null;
        }

        using var user = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{progId}\shell\open\command");
        var value = ReadDefault(user);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        using var merged = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        return ReadDefault(merged);
    }

    private static string? QueryAssoc(string ext, int assocStr)
    {
        foreach (var extra in new[] { "open", null })
        {
            uint length = 0;
            var hr = AssocQueryStringW(0, assocStr, ext, extra, null, ref length);
            if (length == 0)
            {
                continue;
            }

            if (hr < 0 && hr != ErrorInsufficientBuffer)
            {
                continue;
            }

            var buffer = new StringBuilder((int)length);
            hr = AssocQueryStringW(0, assocStr, ext, extra, buffer, ref length);
            if (hr is 0 or 1)
            {
                var text = TrimValue(buffer.ToString());
                if (text is not null)
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static void WriteProgId(string progId, string exe, string extension)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{progId}");
        var typeName = FormatTypeName(extension);
        key.SetValue(null, typeName);
        key.SetValue("FriendlyTypeName", typeName);
        using (var icon = key.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue(null, $"\"{exe}\",0");
        }

        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(null, BuildOpenCommand(exe));
    }

    /// <summary>エクスプローラー等に出す種類名。</summary>
    internal static string FormatTypeName(string extension)
    {
        var ext = NormalizeExtension(extension);
        var kind = ext switch
        {
            ".mp3" => "MP3",
            ".m4a" => "M4A",
            ".aif" or ".aiff" => "AIFF",
            _ => "Wave",
        };
        var name = $"{AppVersion.ProductName} {kind}";
        if (IsPlayerOnlyExtension(ext))
        {
            name = $"{name} ({UiStrings.LabelFileAssociationPlayerOnly})";
        }

        return name;
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

    private static void WriteUserChoice(string ext, string progId)
    {
        using var parent = Registry.CurrentUser.CreateSubKey($@"{FileExtsRoot}\{ext}");
        UnlockDeniedUserChoice(parent, "UserChoice");
        UnlockDeniedUserChoice(parent, "UserChoiceLatest");
        parent.DeleteSubKeyTree("UserChoiceLatest", throwOnMissingSubKey: false);

        var key = parent.OpenSubKey("UserChoice", writable: true);
        if (key is null)
        {
            parent.DeleteSubKeyTree("UserChoice", throwOnMissingSubKey: false);
            key = parent.CreateSubKey("UserChoice");
        }

        try
        {
            WriteUserChoiceValues(key, ext, progId);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            key.Dispose();
            key = null;
            parent.DeleteSubKeyTree("UserChoice", throwOnMissingSubKey: false);
            using var created = parent.CreateSubKey("UserChoice");
            WriteUserChoiceValues(created, ext, progId);
        }
        finally
        {
            key?.Dispose();
        }
    }

    private static void WriteUserChoiceValues(RegistryKey key, string ext, string progId)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException(UiStrings.ErrFileAssociationNotDefault);
        var fileTime = UserChoiceHash.ClampFileTimeToMinute(DateTime.Now.ToFileTime());
        key.SetValue("ProgId", progId);
        key.SetValue("Hash", UserChoiceHash.Compute(ext, sid, progId, fileTime));
        TrySetKeyWriteTime(key, fileTime);
    }

    private static void TrySetKeyWriteTime(RegistryKey key, long fileTime)
    {
        try
        {
            var handle = key.Handle.DangerousGetHandle();
            _ = NtSetInformationKey(handle, 0, ref fileTime, sizeof(long));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private static void TryDeleteUserChoice(string ext)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{FileExtsRoot}\{ext}", writable: true);
            if (key is null)
            {
                return;
            }

            foreach (var name in new[] { "UserChoice", "UserChoiceLatest" })
            {
                UnlockDeniedUserChoice(key, name);
                key.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }

    /// <summary>
    /// Windows は UserChoice に現在ユーザーの SetValue Deny を付ける。
    /// 書き込みオープンも DeleteSubKeyTree もそこで黙って失敗するので、先に Deny を外す。
    /// </summary>
    private static void UnlockDeniedUserChoice(RegistryKey parent, string name)
    {
        try
        {
            using var key = parent.OpenSubKey(
                name,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ChangePermissions | RegistryRights.ReadKey);
            if (key is null)
            {
                return;
            }

            var security = key.GetAccessControl();
            var changed = false;
            foreach (RegistryAccessRule rule in security.GetAccessRules(
                includeExplicit: true,
                includeInherited: false,
                typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Deny)
                {
                    continue;
                }

                security.RemoveAccessRuleSpecific(rule);
                changed = true;
            }

            if (changed)
            {
                key.SetAccessControl(security);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or IdentityNotMappedException)
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

    private static bool SameAppPath(string left, string right, bool namesOnly)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            if (string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return namesOnly
            && string.Equals(Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase);
    }

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

    private static bool FileNameEquals(string? commandOrPath, string exeFileName)
    {
        if (string.IsNullOrWhiteSpace(commandOrPath) || string.IsNullOrWhiteSpace(exeFileName))
        {
            return false;
        }

        if (TryGetCommandExePath(commandOrPath, out var fromCommand)
            && string.Equals(Path.GetFileName(fromCommand), exeFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = commandOrPath.Trim().Trim('"');
        return string.Equals(Path.GetFileName(trimmed), exeFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadDefault(RegistryKey? key)
    {
        if (key is null)
        {
            return null;
        }

        var raw = key.GetValue(null);
        return TrimValue(raw as string ?? raw?.ToString());
    }

    private static string? TrimValue(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationKey(
        IntPtr keyHandle,
        int keySetInformationClass,
        ref long lastWriteTime,
        int dataLength);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryStringW(
        int flags,
        int str,
        string pszAssoc,
        string? pszExtra,
        StringBuilder? pszOut,
        ref uint pcchOut);
}
