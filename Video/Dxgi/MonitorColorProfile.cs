using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// ウィンドウが載っているモニタの ICC プロファイルを読み、「sRGB 線形 → モニタ RGB 線形」の 3x3 と
/// 表示ガンマ（1 本）に近似する。行列／TRC 型（mntr、PCS=XYZ）だけ解析し、LUT 型（A2B0）は未対応。
/// D3DImage 出力は WPF が色管理しないので、この近似をシェーダで掛ける。
/// TODO: LUT 型プロファイルと 3D LUT（WCS）対応。
/// </summary>
internal sealed class MonitorColorProfile
{
    private const int MONITOR_DEFAULTTONEAREST = 2;

    private static readonly object CacheGate = new();
    private static string? _cachedPath;
    private static DateTime _cachedWriteTime;
    private static MonitorColorProfile? _cached;

    private MonitorColorProfile(bool parsed, float gamma, float[] matrix)
    {
        Parsed = parsed;
        Gamma = gamma;
        DisplayFromSrgbLinear = matrix;
        IsSrgbLike = parsed && VideoColorMath.IsIdentity(matrix, 0.01f) && Math.Abs(gamma - 2.2f) < 0.06f;
    }

    /// <summary>行列／TRC が読めた。false なら <see cref="DisplayFromSrgbLinear"/> は恒等で、使わないこと。</summary>
    public bool Parsed { get; }

    /// <summary>表示側 TRC を 1 本のべき指数に近似した値（R/G/B の平均）。</summary>
    public float Gamma { get; }

    /// <summary>sRGB（BT.709）線形 RGB → モニタ線形 RGB。白は白に写るよう正規化済み。行優先。</summary>
    public float[] DisplayFromSrgbLinear { get; }

    /// <summary>sRGB と見分けがつかない（掛けても意味が無い）。</summary>
    public bool IsSrgbLike { get; }

    /// <summary>hwnd のモニタ（0 ならプライマリ）のプロファイルを読む。無ければ null。</summary>
    public static MonitorColorProfile? Load(IntPtr hwndOrZero)
    {
        string? path;
        try
        {
            path = GetProfilePath(hwndOrZero, out _);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        lock (CacheGate)
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            if (_cached is not null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) && _cachedWriteTime == writeTime)
            {
                return _cached;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            var parsed = TryParse(bytes, out var gamma, out var matrix);
            _cached = new MonitorColorProfile(parsed, gamma, matrix);
            _cachedPath = path;
            _cachedWriteTime = writeTime;
            return _cached;
        }
    }

    /// <summary>hwnd のモニタのデバイス名（\\.\DISPLAYn）。切り替え検知用。</summary>
    public static string? GetMonitorDeviceName(IntPtr hwndOrZero)
    {
        try
        {
            var monitor = hwndOrZero == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(hwndOrZero, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return null;
            }

            var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            return GetMonitorInfoW(monitor, ref info) ? info.DeviceName : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>GetICMProfile でモニタ既定の ICC パスを取る。</summary>
    internal static string? GetProfilePath(IntPtr hwndOrZero, out string? deviceName)
    {
        deviceName = GetMonitorDeviceName(hwndOrZero);
        var hdc = CreateDCW("DISPLAY", deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero && deviceName is not null)
        {
            hdc = CreateDCW("DISPLAY", null, null, IntPtr.Zero);
        }

        if (hdc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var size = 1024u;
            var buffer = new char[size];
            if (!GetICMProfileW(hdc, ref size, buffer))
            {
                return null;
            }

            var len = Array.IndexOf(buffer, '\0');
            return new string(buffer, 0, len < 0 ? buffer.Length : len);
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    /// <summary>
    /// ICC（v2／v4）の rXYZ/gXYZ/bXYZ と rTRC/gTRC/bTRC から近似を作る。
    /// 行列は「sRGB 線形 → XYZ(D50) → モニタ線形」。白を (1,1,1) に正規化して白色点ずれを吸収する。
    /// </summary>
    internal static bool TryParse(ReadOnlySpan<byte> icc, out float gamma, out float[] displayFromSrgbLinear)
    {
        gamma = 2.2f;
        displayFromSrgbLinear = (float[])VideoColorMath.Identity.Clone();
        if (icc.Length < 132 || ReadU32(icc, 36) != 0x61637370 /* acsp */)
        {
            return false;
        }

        // データ色空間 'RGB '、PCS 'XYZ '（LUT 型は PCS=Lab のことが多く、ここでは扱わない）。
        if (ReadU32(icc, 16) != 0x52474220 || ReadU32(icc, 20) != 0x58595A20)
        {
            return false;
        }

        var tagCount = ReadU32(icc, 128);
        if (tagCount > 4096 || 132 + (tagCount * 12) > icc.Length)
        {
            return false;
        }

        var tags = new Dictionary<uint, (int Offset, int Size)>();
        for (var i = 0; i < tagCount; i++)
        {
            var at = 132 + (i * 12);
            var sig = ReadU32(icc, at);
            var offset = (int)ReadU32(icc, at + 4);
            var size = (int)ReadU32(icc, at + 8);
            if (offset >= 0 && size >= 0 && offset + size <= icc.Length)
            {
                tags[sig] = (offset, size);
            }
        }

        if (!TryReadXyz(icc, tags, 0x7258595A /* rXYZ */, out var r)
            || !TryReadXyz(icc, tags, 0x6758595A /* gXYZ */, out var g)
            || !TryReadXyz(icc, tags, 0x6258595A /* bXYZ */, out var b))
        {
            return false;
        }

        // 列 = colorant: M_d * (R,G,B)_display = XYZ。
        double[] md =
        [
            r.X, g.X, b.X,
            r.Y, g.Y, b.Y,
            r.Z, g.Z, b.Z,
        ];
        var white = r.Y + g.Y + b.Y;
        if (white < 0.8 || white > 1.2 || !VideoColorMath.TryInvert(md, out var inv))
        {
            return false;
        }

        var m = VideoColorMath.Multiply(inv, VideoColorMath.SrgbToXyzD50);

        // 白の正規化（v2 の D65 colorant などで白が色づくのを防ぐ）。
        for (var row = 0; row < 3; row++)
        {
            var sum = m[row * 3] + m[(row * 3) + 1] + m[(row * 3) + 2];
            if (sum < 0.5 || sum > 2.0 || double.IsNaN(sum))
            {
                return false;
            }

            for (var col = 0; col < 3; col++)
            {
                m[(row * 3) + col] /= sum;
            }
        }

        var gammas = new List<float>(3);
        foreach (var sig in new[] { 0x72545243u, 0x67545243u, 0x62545243u })
        {
            if (TryReadTrcGamma(icc, tags, sig, out var gm))
            {
                gammas.Add(gm);
            }
        }

        gamma = gammas.Count > 0 ? gammas.Average() : 2.2f;
        if (gamma < 1f || gamma > 3.5f || float.IsNaN(gamma))
        {
            gamma = 2.2f;
        }

        displayFromSrgbLinear = new float[9];
        for (var i = 0; i < 9; i++)
        {
            displayFromSrgbLinear[i] = (float)m[i];
        }

        return true;
    }

    private static bool TryReadXyz(ReadOnlySpan<byte> icc, Dictionary<uint, (int Offset, int Size)> tags, uint sig, out (double X, double Y, double Z) xyz)
    {
        xyz = default;
        if (!tags.TryGetValue(sig, out var tag) || tag.Size < 20 || ReadU32(icc, tag.Offset) != 0x58595A20)
        {
            return false;
        }

        xyz = (ReadS15F16(icc, tag.Offset + 8), ReadS15F16(icc, tag.Offset + 12), ReadS15F16(icc, tag.Offset + 16));
        return true;
    }

    /// <summary>curv／para を 0.25・0.5・0.75 で評価してべき指数にあてはめる。</summary>
    private static bool TryReadTrcGamma(ReadOnlySpan<byte> icc, Dictionary<uint, (int Offset, int Size)> tags, uint sig, out float gamma)
    {
        gamma = 0;
        if (!tags.TryGetValue(sig, out var tag) || tag.Size < 12)
        {
            return false;
        }

        var type = ReadU32(icc, tag.Offset);
        Func<double, double>? curve = null;
        if (type == 0x63757276 /* curv */)
        {
            var count = (int)ReadU32(icc, tag.Offset + 8);
            if (count == 0)
            {
                gamma = 1f;
                return true;
            }

            if (count == 1 && tag.Size >= 14)
            {
                gamma = ReadU16(icc, tag.Offset + 12) / 256f;
                return gamma > 0;
            }

            if (tag.Size < 12 + (count * 2))
            {
                return false;
            }

            var start = tag.Offset + 12;
            var table = icc.Slice(start, count * 2).ToArray();
            curve = x =>
            {
                var pos = x * (count - 1);
                var i0 = Math.Clamp((int)Math.Floor(pos), 0, count - 1);
                var i1 = Math.Min(i0 + 1, count - 1);
                var t = pos - i0;
                var v0 = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(i0 * 2)) / 65535.0;
                var v1 = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(i1 * 2)) / 65535.0;
                return v0 + ((v1 - v0) * t);
            };
        }
        else if (type == 0x70617261 /* para */)
        {
            var fn = ReadU16(icc, tag.Offset + 8);
            var paramCount = fn switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 7, _ => -1 };
            if (paramCount < 0 || tag.Size < 12 + (paramCount * 4))
            {
                return false;
            }

            var p = new double[7];
            for (var i = 0; i < paramCount; i++)
            {
                p[i] = ReadS15F16(icc, tag.Offset + 12 + (i * 4));
            }

            if (fn == 0)
            {
                gamma = (float)p[0];
                return gamma > 0;
            }

            curve = x => fn switch
            {
                1 => x >= -p[2] / p[1] ? Math.Pow(Math.Max(0, (p[1] * x) + p[2]), p[0]) : 0,
                2 => x >= -p[2] / p[1] ? Math.Pow(Math.Max(0, (p[1] * x) + p[2]), p[0]) + p[3] : p[3],
                3 => x >= p[4] ? Math.Pow(Math.Max(0, (p[1] * x) + p[2]), p[0]) : p[3] * x,
                _ => x >= p[4] ? Math.Pow(Math.Max(0, (p[1] * x) + p[2]), p[0]) + p[5] : (p[3] * x) + p[6],
            };
        }

        if (curve is null)
        {
            return false;
        }

        var sum = 0.0;
        var n = 0;
        foreach (var x in new[] { 0.25, 0.5, 0.75 })
        {
            var y = curve(x);
            if (y > 0 && y < 1 && !double.IsNaN(y))
            {
                sum += Math.Log(y) / Math.Log(x);
                n++;
            }
        }

        if (n == 0)
        {
            return false;
        }

        gamma = (float)(sum / n);
        return gamma > 0;
    }

    private static uint ReadU32(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt32BigEndian(s.Slice(at, 4));

    private static ushort ReadU16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt16BigEndian(s.Slice(at, 2));

    private static double ReadS15F16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadInt32BigEndian(s.Slice(at, 4)) / 65536.0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEXW info);

    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDCW(string? driver, string? device, string? output, IntPtr initData);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern bool GetICMProfileW(IntPtr hdc, ref uint bufferSize, [Out] char[] filename);
}
