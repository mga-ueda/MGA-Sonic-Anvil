using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Wwise;

/// <summary>
/// Wwise プロジェクトを開く／既に開いていれば前面化する。
/// TimeCaster と同じく、クリック直後に既存 Authoring を前面化し、
/// WAAPI が使えるときは RPC、だめなときは Wwise.exe を直接起動する。
/// </summary>
internal static class WwiseProjectActivator
{
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// 書き出し先（Originals 配下）から親を辿って .wproj を探す。
    /// </summary>
    public static string TryFindProjectFileNearDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        try
        {
            var current = Path.GetFullPath(directory.Trim().Trim('"'));
            for (var i = 0; i < 8; i++)
            {
                if (!Directory.Exists(current))
                {
                    current = Path.GetDirectoryName(current) ?? string.Empty;
                    if (current.Length == 0)
                    {
                        break;
                    }

                    continue;
                }

                var matches = Directory.GetFiles(current, "*.wproj");
                if (matches.Length == 1)
                {
                    return matches[0];
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent;
            }
        }
        catch
        {
            // 探索失敗は未検出として扱う。
        }

        return string.Empty;
    }

    public static async Task<(bool Ok, string Message)> OpenOrFocusAsync(
        WaapiSettings settings,
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        var path = projectFilePath.Trim();
        if (path.Length == 0)
        {
            return (false, UiStrings.LogWwiseProjectPathMissing);
        }

        if (!File.Exists(path))
        {
            return (false, UiStrings.LogWwiseProjectFileMissing(path));
        }

        // クリック直後のフォアグラウンド権限のうちに、既に開いている Authoring を前面化する。
        var focused = TryFocusExistingAuthoring(path);

        var waapiReachable = false;
        try
        {
            using var client = new WaapiHttpClient(
                settings.Url,
                TimeSpan.FromMilliseconds(Math.Max(settings.TimeoutMs, 3000)));

            var info = await WaapiCoreCalls.GetInfoAsync(client, cancellationToken)
                .ConfigureAwait(false);
            waapiReachable = true;
            if (TryGetProcessId(info, out var processId))
            {
                _ = AllowSetForegroundWindow(processId);
            }

            var currentPath = string.Empty;
            try
            {
                var project = await WaapiCoreCalls.GetProjectInfoAsync(client, cancellationToken)
                    .ConfigureAwait(false);
                currentPath = WaapiJson.ReadProjectFilePath(project);
            }
            catch
            {
                // プロジェクト未ロード
            }

            if (PathsEqual(currentPath, path))
            {
                await TryBringToForegroundAsync(client, info, cancellationToken).ConfigureAwait(false);
                TryFocusExistingAuthoring(path);
                return (true, UiStrings.LogWwiseProjectBroughtToFront(Path.GetFileNameWithoutExtension(path)));
            }

            await client.CallAsync(
                    WaapiUris.UiProjectOpen,
                    new Dictionary<string, object?> { ["path"] = path },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await TryBringToForegroundAsync(client, info, cancellationToken).ConfigureAwait(false);
            TryFocusExistingAuthoring(path);
            return (true, UiStrings.LogWwiseProjectOpened(Path.GetFileNameWithoutExtension(path)));
        }
        catch (Exception ex)
        {
            if (TryFocusExistingAuthoring(path) || focused)
            {
                return (true, UiStrings.LogWwiseProjectBroughtToFront(Path.GetFileNameWithoutExtension(path)));
            }

            if (waapiReachable)
            {
                var fallback = OpenViaAuthoringOrShell(path);
                if (fallback.Ok)
                {
                    return fallback;
                }

                return (false, UiStrings.LogWwiseProjectOpenRequestFailed(ex.Message));
            }

            return OpenViaAuthoringOrShell(path);
        }
    }

    /// <summary>
    /// 接続中の Wwise Authoring を前面化する（プロジェクトの開閉はしない）。
    /// </summary>
    public static async Task<(bool Ok, string Message)> BringToForegroundAsync(
        WaapiSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new WaapiHttpClient(
                settings.Url,
                TimeSpan.FromMilliseconds(Math.Max(settings.TimeoutMs, 10000)));

            var info = await WaapiCoreCalls.GetInfoAsync(client, cancellationToken)
                .ConfigureAwait(false);
            await TryBringToForegroundAsync(client, info, cancellationToken).ConfigureAwait(false);
            TryFocusExistingAuthoring(projectFilePath: null);
            return (true, UiStrings.LogWwiseBroughtToFront);
        }
        catch (Exception ex)
        {
            if (TryFocusExistingAuthoring(projectFilePath: null))
            {
                return (true, UiStrings.LogWwiseBroughtToFront);
            }

            return (false, UiStrings.LogWwiseBringToFrontFailed(ex.Message));
        }
    }

    private static async Task TryBringToForegroundAsync(
        WaapiHttpClient client,
        JsonElement info,
        CancellationToken cancellationToken)
    {
        if (TryGetProcessId(info, out var processId))
        {
            _ = AllowSetForegroundWindow(processId);
        }

        try
        {
            await WaapiCoreCalls.BringToForegroundAsync(client, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Windows の前面化制限で WAAPI だけでは失敗することがある。
        }

        TryFocusProcess(info);
    }

    /// <summary>
    /// 既に起動している Wwise Authoring を前面化する。
    /// プロジェクト名がタイトルに含まれるウィンドウを優先する。
    /// </summary>
    public static bool TryFocusExistingAuthoring(string? projectFilePath)
    {
        _ = AllowSetForegroundWindow(AsfwAny);
        var file = string.IsNullOrWhiteSpace(projectFilePath)
            ? string.Empty
            : Path.GetFileName(projectFilePath);
        var name = string.IsNullOrWhiteSpace(projectFilePath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(projectFilePath);

        IntPtr matched = IntPtr.Zero;
        IntPtr any = IntPtr.Zero;
        var authoringCount = 0;
        foreach (var process in Process.GetProcessesByName("Wwise"))
        {
            try
            {
                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                authoringCount++;
                any = handle;
                var title = process.MainWindowTitle ?? string.Empty;
                if ((file.Length > 0 && title.Contains(file, StringComparison.OrdinalIgnoreCase))
                    || (name.Length > 0 && title.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    matched = handle;
                    break;
                }
            }
            catch
            {
                // 終了直後
            }
        }

        var target = matched != IntPtr.Zero
            ? matched
            : authoringCount == 1
                ? any
                : IntPtr.Zero;
        if (target == IntPtr.Zero)
        {
            return false;
        }

        TryForceForeground(target);
        return true;
    }

    public static void TryForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = AllowSetForegroundWindow(AsfwAny);
        if (IsIconic(hwnd))
        {
            _ = ShowWindow(hwnd, SwRestore);
        }
        else
        {
            _ = ShowWindow(hwnd, SwShow);
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var thisThread = GetCurrentThreadId();
        var attachedFore = foregroundThread != 0
                           && foregroundThread != thisThread
                           && AttachThreadInput(thisThread, foregroundThread, true);
        var attachedTarget = targetThread != 0
                             && targetThread != thisThread
                             && targetThread != foregroundThread
                             && AttachThreadInput(thisThread, targetThread, true);
        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                _ = AttachThreadInput(thisThread, targetThread, false);
            }

            if (attachedFore)
            {
                _ = AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }

    private static void TryFocusProcess(JsonElement info)
    {
        if (!TryGetProcessId(info, out var processId) || processId <= 0)
        {
            return;
        }

        try
        {
            _ = AllowSetForegroundWindow(processId);
            using var process = Process.GetProcessById(processId);
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero)
            {
                process.Refresh();
                handle = process.MainWindowHandle;
            }

            if (handle == IntPtr.Zero)
            {
                return;
            }

            TryForceForeground(handle);
        }
        catch
        {
            // プロセスが終了している場合は無視
        }
    }

    /// <summary>
    /// WAAPI 不通時: インストール済み Wwise.exe を .wproj の版に合わせて直接起動する。
    /// （.wproj の既定関連付けは Wwise Launcher のため、シェル実行だけでは Authoring が開かないことがある。）
    /// </summary>
    private static (bool Ok, string Message) OpenViaAuthoringOrShell(string path)
    {
        if (TryFocusExistingAuthoring(path))
        {
            return (true, UiStrings.LogWwiseProjectBroughtToFront(Path.GetFileNameWithoutExtension(path)));
        }

        try
        {
            if (TryFindWwiseExecutable(path, out var wwiseExe))
            {
                StartAuthoring(wwiseExe, path);
                return (true, UiStrings.LogWwiseProjectShellOpen(Path.GetFileNameWithoutExtension(path)));
            }

            if (AnyAuthoringRunning())
            {
                return (false, UiStrings.LogWwiseProjectOpenRequestFailed(
                    "Wwise is already running but the window could not be activated."));
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return (true, UiStrings.LogWwiseProjectShellOpen(Path.GetFileNameWithoutExtension(path)));
        }
        catch (Exception ex)
        {
            return (false, UiStrings.LogWwiseProjectOpenFailed(ex.Message));
        }
    }

    private static void StartAuthoring(string wwiseExe, string projectPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = wwiseExe,
            WorkingDirectory = Path.GetDirectoryName(wwiseExe) ?? string.Empty,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(projectPath);
        Process.Start(start);
    }

    private static bool AnyAuthoringRunning()
    {
        try
        {
            return Process.GetProcessesByName("Wwise").Any(p =>
            {
                try
                {
                    return !p.HasExited;
                }
                catch
                {
                    return false;
                }
            });
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindWwiseExecutable(string projectFilePath, out string wwiseExe)
    {
        wwiseExe = string.Empty;
        TryReadProjectVersion(projectFilePath, out var version, out var build);

        var versionKey = version.Trim().TrimStart('v', 'V');
        string? exact = null;
        string? versionMatch = null;

        foreach (var root in EnumerateAudiokineticRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var dir in Directory.EnumerateDirectories(root, "Wwise*"))
            {
                var exe = Path.Combine(dir, "Authoring", "x64", "Release", "bin", "Wwise.exe");
                if (!File.Exists(exe))
                {
                    continue;
                }

                var folder = Path.GetFileName(dir) ?? string.Empty;
                if (versionKey.Length > 0
                    && build.Length > 0
                    && (folder.Equals($"Wwise{versionKey}.{build}", StringComparison.OrdinalIgnoreCase)
                        || folder.Equals($"Wwise_{versionKey}.{build}", StringComparison.OrdinalIgnoreCase)
                        || folder.Equals($"Wwise_{versionKey}_{build}", StringComparison.OrdinalIgnoreCase)))
                {
                    exact = exe;
                    break;
                }

                if (versionMatch is null
                    && versionKey.Length > 0
                    && (folder.StartsWith($"Wwise{versionKey}", StringComparison.OrdinalIgnoreCase)
                        || folder.StartsWith($"Wwise_{versionKey}", StringComparison.OrdinalIgnoreCase)))
                {
                    versionMatch = exe;
                }
            }

            if (exact is not null)
            {
                break;
            }
        }

        wwiseExe = exact ?? versionMatch ?? string.Empty;
        return wwiseExe.Length > 0;
    }

    private static IEnumerable<string> EnumerateAudiokineticRoots()
    {
        yield return @"C:\Audiokinetic";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Audiokinetic");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Audiokinetic");
    }

    private static bool TryReadProjectVersion(
        string projectFilePath,
        out string version,
        out string build)
    {
        version = string.Empty;
        build = string.Empty;
        try
        {
            using var reader = XmlReader.Create(
                projectFilePath,
                new XmlReaderSettings
                {
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.Name, "WwiseDocument", StringComparison.Ordinal))
                {
                    version = reader.GetAttribute("WwiseVersion")?.Trim() ?? string.Empty;
                    build = reader.GetAttribute("WwiseBuild")?.Trim() ?? string.Empty;
                    return version.Length > 0 || build.Length > 0;
                }

                if (reader.Depth > 0)
                {
                    break;
                }
            }
        }
        catch
        {
            // 版が取れなくてもシェル関連付けへフォールバックできる。
        }

        return false;
    }

    private static bool PathsEqual(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        try
        {
            var left = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var right = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (Directory.Exists(left) && File.Exists(right))
            {
                return string.Equals(
                    left,
                    Path.GetDirectoryName(right),
                    StringComparison.OrdinalIgnoreCase);
            }

            if (Directory.Exists(right) && File.Exists(left))
            {
                return string.Equals(
                    right,
                    Path.GetDirectoryName(left),
                    StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        catch
        {
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool TryGetProcessId(JsonElement info, out int processId)
    {
        processId = 0;
        if (info.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (info.TryGetProperty("processId", out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out processId)
            && processId > 0)
        {
            return true;
        }

        if (info.TryGetProperty("pid", out prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out processId)
            && processId > 0)
        {
            return true;
        }

        return false;
    }
}
