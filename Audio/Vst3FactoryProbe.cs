using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// VST3 の GetPluginFactory → IPluginFactory2.getClassInfo2 で種別を取る。
/// Nuendo / Cubase と同じ情報源。DLL 読み込みで落ちうるので、既定は別プロセスで実行する。
/// </summary>
internal static class Vst3FactoryProbe
{
    public const string CliSwitch = "--vst3-probe";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed class ProbeResult
    {
        public string? Name { get; set; }

        public string? Vendor { get; set; }

        public string? Category { get; set; }

        public string? Version { get; set; }

        public string? Error { get; set; }
    }

    /// <summary>
    /// 別プロセスで工場を問い合わせる。失敗・タイムアウトは null。
    /// 自プロセスでは呼ばない（プラグイン側のクラッシュを隔離するため）。
    /// </summary>
    public static ProbeResult? ProbeOutOfProcess(
        string pluginPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginPath))
        {
            return null;
        }

        if (!IsOutOfProcessAvailable())
        {
            return null;
        }

        if (!TryResolveBinary(pluginPath, out var binary))
        {
            return null;
        }

        // Waves WaveShell は LoadLibrary／工場で AV することがある。子プロセスでも時間が消えるので飛ばす。
        if (IsWaveShellPath(pluginPath) || IsWaveShellPath(binary))
        {
            return null;
        }

        // ダミー／非 PE は起動コストを払わない。
        if (!LooksLikePe(binary))
        {
            return null;
        }

        if (!TryBuildProbeStartInfo(binary, out var start))
        {
            return null;
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            if (!process.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }

                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var json = stdoutTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<ProbeResult>(json.Trim(), JsonOptions);
            return result is null || string.IsNullOrWhiteSpace(result.Category) ? null : result;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
            or JsonException or OperationCanceledException or InvalidOperationException)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return null;
        }
    }

    /// <summary>CLI 入口。標準出力に JSON を書き、成否を終了コードで返す。</summary>
    public static int RunCli(string pluginPath)
    {
        // WinExe では Console.OutputEncoding が IOException になることがあるので使わない。
        ProbeResult result;
        try
        {
            result = ProbeInProcess(pluginPath) ?? new ProbeResult { Error = "probe-failed" };
        }
        catch (Exception ex)
        {
            result = new ProbeResult { Error = ex.GetType().Name + ":" + ex.Message };
        }

        try
        {
            var json = JsonSerializer.Serialize(result, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(bytes, 0, bytes.Length);
            stdout.Flush();
        }
        catch
        {
        }

        return string.IsNullOrWhiteSpace(result.Category) ? 2 : 0;
    }

    /// <summary>同一プロセスで工場を読む。テストと CLI 用。製品スキャンからは直接呼ばない。</summary>
    internal static ProbeResult? ProbeInProcess(string pluginPath)
    {
        if (!TryResolveBinary(pluginPath, out var binary))
        {
            return new ProbeResult { Error = "no-binary" };
        }

        if (IsWaveShellPath(pluginPath) || IsWaveShellPath(binary))
        {
            return new ProbeResult { Error = "waveshell-skipped" };
        }

        var module = Native.LoadLibraryEx(binary, IntPtr.Zero, Native.LoadWithAlteredSearchPath);
        if (module == IntPtr.Zero)
        {
            return new ProbeResult { Error = "load-failed" };
        }

        try
        {
            var proc = Native.GetProcAddress(module, "GetPluginFactory");
            if (proc == IntPtr.Zero)
            {
                return new ProbeResult { Error = "no-export" };
            }

            var getFactory = Marshal.GetDelegateForFunctionPointer<Native.GetFactoryFn>(proc);
            var factory = getFactory();
            if (factory == IntPtr.Zero)
            {
                return new ProbeResult { Error = "null-factory" };
            }

            try
            {
                return ReadFactory(factory);
            }
            finally
            {
                Native.Release(factory);
            }
        }
        finally
        {
            Native.FreeLibrary(module);
        }
    }

    private static ProbeResult? ReadFactory(IntPtr factory)
    {
        // GetPluginFactory の戻りがすでに Factory2/3 のことが多い。QI に失敗しても直接呼ぶ。
        var targets = new List<(IntPtr Ptr, bool Release)>(3) { (factory, false) };
        if (Native.QueryInterface(factory, Native.IPluginFactory2Iid, out var factory2) == Native.KResultOk
            && factory2 != IntPtr.Zero)
        {
            targets.Add((factory2, true));
        }

        if (Native.QueryInterface(factory, Native.IPluginFactory3Iid, out var factory3) == Native.KResultOk
            && factory3 != IntPtr.Zero)
        {
            targets.Add((factory3, true));
        }

        try
        {
            var usable = new List<(ClassInfo2 Info, string Sub)>();
            string? lastError = null;
            foreach (var (ptr, _) in targets)
            {
                var count = Native.CountClasses(ptr);
                if (count <= 0)
                {
                    lastError = "no-classes";
                    continue;
                }

                for (var i = 0; i < count; i++)
                {
                    if (!Native.TryGetClassInfo2(ptr, i, out var info))
                    {
                        lastError = "getClassInfo2-failed";
                        continue;
                    }

                    // Softube などは "Audio Mix Processor"。Controller 等以外で subCategories があれば採る。
                    if (!Vst3EffectClass.IsUsableEffectClass(info.Category, info.SubCategories))
                    {
                        if (!Vst3EffectClass.IsSkippableCategory(info.Category)
                            && string.IsNullOrWhiteSpace(info.SubCategories))
                        {
                            lastError = "empty-subcategories";
                        }

                        continue;
                    }

                    usable.Add((info, info.SubCategories));
                }
            }

            if (usable.Count == 0)
            {
                return new ProbeResult { Error = lastError ?? "no-audio-class" };
            }

            var preferred = usable
                .OrderBy(u => Vst3EffectClass.NamePreference(u.Info.Category))
                .ThenByDescending(u => u.Info.Name.Length)
                .First();
            // クラスの Vendor が空なことが多い（Softube / Melda）。工場情報の vendor を使う。
            var factoryVendor = Native.TryGetFactoryVendor(factory, out var fv) ? fv : string.Empty;
            return new ProbeResult
            {
                Name = preferred.Info.Name,
                Vendor = FirstNonEmpty(preferred.Info.Vendor, factoryVendor),
                Category = Vst3EffectClass.MergeSubCategories(usable.Select(u => u.Sub)),
                Version = preferred.Info.Version,
            };
        }
        finally
        {
            foreach (var (ptr, release) in targets)
            {
                if (release)
                {
                    Native.Release(ptr);
                }
            }
        }
    }

    /// <summary>
    /// 子プロセス起動が可能か。testhost 上の単体テストでは false（誤ってテストランナーを起動しない）。
    /// </summary>
    internal static bool IsOutOfProcessAvailable()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var file = Path.GetFileNameWithoutExtension(processPath);
        if (file.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetProbeAssemblyPath(out _);
        }

        return file.Contains("MGA Sonic Anvil", StringComparison.OrdinalIgnoreCase)
            || file.Equals("MgaSonicAnvil", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetProbeAssemblyPath(out string dll)
    {
        // single-file では Assembly.Location が空になるため BaseDirectory だけ見る。
        dll = Path.Combine(AppContext.BaseDirectory, "MGA Sonic Anvil.dll");
        return File.Exists(dll);
    }

    internal static bool IsWaveShellPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.StartsWith("WaveShell", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePe(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 0x40)
            {
                return false;
            }

            Span<byte> dos = stackalloc byte[0x40];
            if (stream.Read(dos) < dos.Length || dos[0] != (byte)'M' || dos[1] != (byte)'Z')
            {
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool TryResolveBinary(string pluginPath, out string binary)
    {
        binary = string.Empty;
        try
        {
            if (File.Exists(pluginPath))
            {
                binary = Path.GetFullPath(pluginPath);
                return true;
            }

            if (!Directory.Exists(pluginPath))
            {
                return false;
            }

            var contents = Path.Combine(pluginPath, "Contents");
            foreach (var folder in new[] { "x86_64-win", "arm64x-win", "arm64ec-win", "arm64-win", "x86-win" })
            {
                var dir = Path.Combine(contents, folder);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var found = Directory.EnumerateFiles(dir, "*.vst3").FirstOrDefault()
                    ?? Directory.EnumerateFiles(dir, "*.dll").FirstOrDefault();
                if (found is not null)
                {
                    binary = found;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        return false;
    }

    private static bool TryBuildProbeStartInfo(string binaryPath, out ProcessStartInfo start)
    {
        start = null!;
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            return false;
        }

        // 開発時は dotnet.exe 経由。本番 single-file は exe 直。
        var fileName = processPath;
        var args = new StringBuilder();
        if (string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetProbeAssemblyPath(out var dll))
            {
                return false;
            }

            args.Append('"').Append(dll).Append("\" ");
        }

        args.Append(CliSwitch).Append(' ').Append('"').Append(binaryPath).Append('"');
        start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args.ToString(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        return true;
    }

    private static class Native
    {
        public const uint LoadWithAlteredSearchPath = 0x00000008;
        public const int KResultOk = 0;

        // Steinberg::IPluginFactory2::iid {0007B650-F24B-4C0B-A464-EDB9F1F58F36}
        public static readonly byte[] IPluginFactory2Iid =
        [
            0x50, 0xB6, 0x07, 0x00, 0x4B, 0xF2, 0x0B, 0x4C,
            0xA4, 0x64, 0xED, 0xB9, 0xF1, 0xF5, 0x8F, 0x36,
        ];

        // Steinberg::IPluginFactory3::iid {00B47B28-B24B-4EA6-B02C-A575D704D6AC}
        public static readonly byte[] IPluginFactory3Iid =
        [
            0x28, 0x7B, 0xB4, 0x00, 0x4B, 0xB2, 0xA6, 0x4E,
            0xB0, 0x2C, 0xA5, 0x75, 0xD7, 0x04, 0xD6, 0xAC,
        ];

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr GetFactoryFn();

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        public static int QueryInterface(IntPtr obj, byte[] iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (obj == IntPtr.Zero)
            {
                return -1;
            }

            unsafe
            {
                var vt = *(IntPtr**)obj;
                var qi = Marshal.GetDelegateForFunctionPointer<QiFn>(vt[0]);
                fixed (byte* iidPtr = iid)
                {
                    return qi(obj, iidPtr, out result);
                }
            }
        }

        public static void Release(IntPtr obj)
        {
            if (obj == IntPtr.Zero)
            {
                return;
            }

            unsafe
            {
                var vt = *(IntPtr**)obj;
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseFn>(vt[2]);
                release(obj);
            }
        }

        public static int CountClasses(IntPtr factory2)
        {
            unsafe
            {
                var vt = *(IntPtr**)factory2;
                // FUnknown(3) + IPluginFactory.getFactoryInfo, countClasses
                var countClasses = Marshal.GetDelegateForFunctionPointer<CountClassesFn>(vt[4]);
                return countClasses(factory2);
            }
        }

        /// <summary>PFactoryInfo.vendor（Softube / Melda などクラス側が空のとき用）。</summary>
        public static bool TryGetFactoryVendor(IntPtr factory, out string vendor)
        {
            vendor = string.Empty;
            if (factory == IntPtr.Zero)
            {
                return false;
            }

            unsafe
            {
                var raw = new byte[FactoryInfo.Size];
                fixed (byte* p = raw)
                {
                    var vt = *(IntPtr**)factory;
                    var getFactoryInfo = Marshal.GetDelegateForFunctionPointer<GetFactoryInfoFn>(vt[3]);
                    if (getFactoryInfo(factory, p) != KResultOk)
                    {
                        return false;
                    }
                }

                vendor = FactoryInfo.ReadVendor(raw);
                return !string.IsNullOrWhiteSpace(vendor);
            }
        }

        public static bool TryGetClassInfo2(IntPtr factory2, int index, out ClassInfo2 info)
        {
            info = default;
            unsafe
            {
                var vt = *(IntPtr**)factory2;
                // FUnknown(3) + IPluginFactory(4) + getClassInfo2 = index 7
                var getClassInfo2 = Marshal.GetDelegateForFunctionPointer<GetClassInfo2Fn>(vt[7]);
                var raw = new byte[ClassInfo2.Size];
                fixed (byte* p = raw)
                {
                    if (getClassInfo2(factory2, index, p) != KResultOk)
                    {
                        return false;
                    }
                }

                info = ClassInfo2.Parse(raw);
                return true;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int QiFn(IntPtr self, byte* iid, out IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CountClassesFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int GetFactoryInfoFn(IntPtr self, byte* info);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int GetClassInfo2Fn(IntPtr self, int index, byte* info);
    }

    /// <summary>PFactoryInfo: vendor[64] + url[256] + email[128] + flags(int32)。</summary>
    private static class FactoryInfo
    {
        public const int Size = 64 + 256 + 128 + 4;

        public static string ReadVendor(byte[] raw) => ReadCString(raw, 0, 64);
    }

    internal readonly struct ClassInfo2
    {
        // TUID(16) + int32 + category[32] + name[64] + uint32 + sub[128] + vendor[64] + version[64] + sdk[64]
        public const int Size = 16 + 4 + 32 + 64 + 4 + 128 + 64 + 64 + 64;

        public string Category { get; init; }
        public string Name { get; init; }
        public string SubCategories { get; init; }
        public string Vendor { get; init; }
        public string Version { get; init; }

        public static ClassInfo2 Parse(byte[] raw)
        {
            var offset = 16 + 4; // cid + cardinality
            var category = ReadCString(raw, offset, 32);
            offset += 32;
            var name = ReadCString(raw, offset, 64);
            offset += 64;
            offset += 4; // classFlags
            var sub = ReadCString(raw, offset, 128);
            offset += 128;
            var vendor = ReadCString(raw, offset, 64);
            offset += 64;
            var version = ReadCString(raw, offset, 64);
            return new ClassInfo2
            {
                Category = category,
                Name = name,
                SubCategories = sub,
                Vendor = vendor,
                Version = version,
            };
        }
    }

    private static string FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a! : b ?? string.Empty;

    private static string ReadCString(byte[] raw, int offset, int max)
    {
        var end = offset;
        var limit = Math.Min(raw.Length, offset + max);
        while (end < limit && raw[end] != 0)
        {
            end++;
        }

        return end == offset ? string.Empty : Encoding.UTF8.GetString(raw, offset, end - offset).Trim();
    }
}
