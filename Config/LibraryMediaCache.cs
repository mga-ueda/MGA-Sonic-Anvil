using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MgaSonicAnvil.Config;

/// <summary>
/// プレイリストで一度表示した波形棒・ジャケットをディスクに残す。
/// 先読みはしない。size/mtime が変わったら無効。
/// </summary>
internal static class LibraryMediaCache
{
    public const string CacheFolderName = "library-media-cache";
    private const uint WaveMagic = 0x57415631; // WAV1
    private const uint ArtMagic = 0x41525431; // ART1
    private const int WaveVersion = 1;
    private const int ArtVersion = 1;

    private static readonly object Gate = new();
    private static string? _directoryOverride;

    private static string RootDirectory =>
        Path.Combine(_directoryOverride ?? AppStorage.RootDirectory, CacheFolderName);

    internal static void UseDirectoryForTests(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        lock (Gate)
        {
            _directoryOverride = Path.GetFullPath(directory);
            Directory.CreateDirectory(RootDirectory);
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _directoryOverride = null;
        }
    }

    public static bool TryGetWaveform(string path, int barCount, out float[] bars)
    {
        bars = [];
        if (!TryStat(path, out var full, out var size, out var mtime) || barCount <= 0)
        {
            return false;
        }

        var file = WavePath(full, barCount);
        try
        {
            if (!File.Exists(file))
            {
                return false;
            }

            var bytes = File.ReadAllBytes(file);
            if (bytes.Length < 32)
            {
                return false;
            }

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0));
            var version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            var storedBars = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
            var storedSize = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(12));
            var storedMtime = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(20));
            if (magic != WaveMagic
                || version != WaveVersion
                || storedBars != barCount
                || storedSize != size
                || storedMtime != mtime)
            {
                return false;
            }

            var need = 28 + (barCount * sizeof(float));
            if (bytes.Length < need)
            {
                return false;
            }

            bars = new float[barCount];
            Buffer.BlockCopy(bytes, 28, bars, 0, barCount * sizeof(float));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public static void PutWaveform(string path, int barCount, float[] bars)
    {
        if (bars is null
            || barCount <= 0
            || bars.Length < barCount
            || !TryStat(path, out var full, out var size, out var mtime))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(RootDirectory);
            var file = WavePath(full, barCount);
            var bytes = new byte[28 + (barCount * sizeof(float))];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), WaveMagic);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), WaveVersion);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), barCount);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(12), size);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(20), mtime);
            Buffer.BlockCopy(bars, 0, bytes, 28, barCount * sizeof(float));
            var tmp = file + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    /// <summary>
    /// ジャケット探査結果。true なら探査済み。<paramref name="hasImage"/> が実画像の有無。
    /// </summary>
    public static bool TryGetArtworkProbe(string path, out bool hasImage)
    {
        hasImage = false;
        if (!TryReadArtRecord(path, out var payload, out _))
        {
            return false;
        }

        hasImage = payload > 0;
        return true;
    }

    public static bool TryGetArtwork(string path, out byte[] image)
    {
        image = [];
        if (!TryReadArtRecord(path, out var payload, out var bytes) || payload <= 0 || bytes is null)
        {
            return false;
        }

        image = bytes.AsSpan(28, payload).ToArray();
        return image.Length > 0;
    }

    public static void PutArtwork(string path, byte[] image)
    {
        if (image is not { Length: > 0 })
        {
            return;
        }

        WriteArtRecord(path, image);
    }

    /// <summary>ジャケット列向け。画像が無いことも探査済みとして残す。</summary>
    public static void PutNoArtwork(string path) => WriteArtRecord(path, []);

    private static bool TryReadArtRecord(string path, out int payload, out byte[]? bytes)
    {
        payload = 0;
        bytes = null;
        if (!TryStat(path, out var full, out var size, out var mtime))
        {
            return false;
        }

        var file = ArtPath(full);
        try
        {
            if (!File.Exists(file))
            {
                return false;
            }

            bytes = File.ReadAllBytes(file);
            if (bytes.Length < 28)
            {
                return false;
            }

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0));
            var version = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            payload = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
            var storedSize = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(12));
            var storedMtime = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(20));
            if (magic != ArtMagic
                || version != ArtVersion
                || payload < 0
                || storedSize != size
                || storedMtime != mtime
                || bytes.Length < 28 + payload)
            {
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void WriteArtRecord(string path, byte[] image)
    {
        if (!TryStat(path, out var full, out var size, out var mtime))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(RootDirectory);
            var file = ArtPath(full);
            var payload = image.Length;
            var bytes = new byte[28 + payload];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), ArtMagic);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), ArtVersion);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), payload);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(12), size);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(20), mtime);
            if (payload > 0)
            {
                Buffer.BlockCopy(image, 0, bytes, 28, payload);
            }

            var tmp = file + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static string WavePath(string fullPath, int barCount)
    {
        var hash = HashKey(fullPath + "\u0001w\u0001" + barCount.ToString());
        return Path.Combine(RootDirectory, hash + ".wavbars");
    }

    private static string ArtPath(string fullPath)
    {
        var hash = HashKey(fullPath + "\u0001a");
        return Path.Combine(RootDirectory, hash + ".art");
    }

    private static string HashKey(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static bool TryStat(string path, out string full, out long size, out long mtime)
    {
        full = string.Empty;
        size = 0;
        mtime = 0;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path.Trim());
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                return false;
            }

            size = info.Length;
            mtime = info.LastWriteTimeUtc.Ticks;
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }
}
