using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MgaSonicAnvil.Config;

/// <summary>
/// Windows 10 以降の FileExts UserChoice 用ハッシュ。
/// 入力は拡張子・SID・ProgId・キーの最終更新（分単位）と、公開されている Experience 文字列。
/// </summary>
internal static class UserChoiceHash
{
    public const string Experience =
        "User Choice set via Windows User Experience {D18B6DD5-6124-4341-9318-804003BAFA0B}";

    public const long FileTimeTicksPerMinute = 600_000_000L;

    public static long ClampFileTimeToMinute(long fileTime)
    {
        if (fileTime < 0)
        {
            return 0;
        }

        return fileTime - (fileTime % FileTimeTicksPerMinute);
    }

    public static string Compute(string extension, string userSid, string progId, long fileTime)
    {
        var input = string.Concat(
                extension,
                userSid,
                progId,
                fileTime.ToString("x16", CultureInfo.InvariantCulture),
                Experience)
            .ToLowerInvariant();
        var bytes = Encoding.Unicode.GetBytes(input + "\0");
        return ComputeFromUnicodeBytes(bytes);
    }

    internal static string ComputeFromUnicodeBytes(byte[] bytes)
    {
        var md5 = MD5.HashData(bytes);
        var md0 = BitConverter.ToUInt32(md5, 0);
        var md1 = BitConverter.ToUInt32(md5, 4);
        var blockCount = bytes.Length / 8;
        uint h0 = 0;
        uint h1 = 0;
        uint h0Total = 0;
        uint h1Total = 0;
        ReadOnlySpan<uint> firstEven = [md0 | 1u, 0xCF98B111u, 0x87085B9Fu, 0x12CEB96Du, 0x257E1D83u];
        ReadOnlySpan<uint> firstOdd = [md1 | 1u, 0xA27416F5u, 0xD38396FFu, 0x7C932B89u, 0xBFA49F69u];
        ReadOnlySpan<uint> secondEven = [md0 | 1u, 0xEF0569FBu, 0x689B6B9Fu, 0x79F8A395u, 0xC3EFEA97u];
        ReadOnlySpan<uint> secondOdd = [md1 | 1u, 0xC31713DBu, 0xDDCD1F0Fu, 0x59C3AF2Du, 0x35BD1EC9u];

        for (var block = 0; block < blockCount; block++)
        {
            Mix(bytes, block * 8, firstEven, secondEven, ref h0, ref h1, ref h0Total, ref h1Total);
            Mix(bytes, block * 8 + 4, firstOdd, secondOdd, ref h0, ref h1, ref h0Total, ref h1Total);
        }

        Span<byte> hash = stackalloc byte[8];
        BitConverter.TryWriteBytes(hash, h0 ^ h1);
        BitConverter.TryWriteBytes(hash[4..], h0Total ^ h1Total);
        return Convert.ToBase64String(hash);
    }

    private static void Mix(
        byte[] bytes,
        int offset,
        ReadOnlySpan<uint> first,
        ReadOnlySpan<uint> second,
        ref uint h0,
        ref uint h1,
        ref uint h0Total,
        ref uint h1Total)
    {
        var value = BitConverter.ToUInt32(bytes, offset);
        h0 += value;
        h0 *= first[0];
        h0 = Rotate16(h0) * first[1];
        h0 = Rotate16(h0) * first[2];
        h0 = Rotate16(h0) * first[3];
        h0 = Rotate16(h0) * first[4];
        h0Total += h0;

        h1 += value;
        h1 = Rotate16(h1) * second[1] + h1 * second[0];
        h1 = (h1 >> 16) * second[2] + h1 * second[3];
        h1 = Rotate16(h1) * second[4] + h1;
        h1Total += h1;
    }

    private static uint Rotate16(uint value) => (value >> 16) | (value << 16);
}
