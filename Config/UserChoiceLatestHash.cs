using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Config;

/// <summary>
/// Windows 11 の UserChoiceLatest 用ハッシュ。
/// 照合表と計算は cssxn/UserChoiceLatestHash (MIT) に合わせてある。
/// </summary>
internal static class UserChoiceLatestHash
{
    private const ulong RepeatSentinel = 0xFBFFFFFFFFFFFFFFUL;
    private const ulong RepeatTokenA = 0xFE00000000000000UL;
    private const ulong RepeatTokenB = 0xFE00800000000000UL;
    private const ulong RepeatTokenC = 0xFF00000000000000UL;
    private const ulong RepeatTokenD = 0xFE007DFFFFFFFFFFUL;
    private const ulong RepeatTokenE = 0xFE00801F7FFFFFFFUL;
    private const uint SeenModulo = 0x2803U;

    private const string Salt3822 =
        "Copyright (C) Microsoft. All rights reserved {3822B7CA-C2F4-4889-B8CC-4CE39A8FB81C}";

    private const string SaltD185 =
        "Copyright (C) Microsoft. All rights reserved {D185E0A1-E265-4724-AA21-3A17B038D72E}";

    private const string Salt97B6 =
        "Copyright (C) Microsoft. All rights reserved {97B6BCF4-C367-4577-95BE-73BD3053A5E0}";

    private static readonly uint[] SeedA = LoadSeed(
        [0x00, 0x00, 0xFB, 0x69, 0x95, 0xA3, 0xF8, 0x79, 0x25, 0xEC, 0xE8, 0x3C, 0x05, 0x96, 0xFA, 0x10]);

    private static readonly uint[] SeedB = LoadSeed(
        [0x6D, 0xB9, 0xCE, 0x12, 0x00, 0x00, 0x89, 0x2B, 0x00, 0x00, 0x9F, 0x5B, 0x01, 0x69, 0x7C, 0x2C]);

    private static readonly Tables Lookup = Tables.Load();

    private static bool? _inEffect;

    internal static bool IsInEffect()
    {
        if (_inEffect is not null)
        {
            return _inEffect.Value;
        }

        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            using var key = sid is null
                ? null
                : Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\SystemProtectedUserData\{sid}\AnyoneRead\AppDefaults");
            _inEffect = key?.GetValue("HashVersion") is int version && version >= 1;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _inEffect = false;
        }

        return _inEffect.Value;
    }

    internal static string? ReadMachineId()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\SQMClient");
        var value = key?.GetValue("MachineId") as string;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal static string Compute(
        string extension,
        string userSid,
        string progId,
        string machineId,
        long progIdKeyFileTime)
    {
        var machine = StripBraces(machineId);
        var timestamp = FormatTimestamp(NormalizeFileTime(progIdKeyFileTime));
        return ComputeCanonical(BuildCanonical(extension, userSid, progId, machine, timestamp));
    }

    internal static string ComputeCanonical(string canonical)
    {
        var lowered = canonical.ToLowerInvariant();
        var chars = new ushort[lowered.Length];
        for (var i = 0; i < lowered.Length; i++)
        {
            chars[i] = lowered[i];
        }

        var state = new EncoderState();
        var consumed = FirstPass(state, chars, 0, chars.Length);
        SecondPass(state, chars, consumed, chars.Length);
        state.Entries[0] = (ulong)state.TotalUnits | (state.Count << 32);

        var packed = new List<ushort>();
        for (var idx = 0UL; idx < state.Count; idx++)
        {
            var value = state.Entries[idx];
            for (var shift = 0; shift < 64; shift += 16)
            {
                var part = (ushort)(value >> shift);
                if (part != 0)
                {
                    packed.Add(part);
                }
            }
        }

        packed.Add(0);
        var packedBytes = new byte[packed.Count * 2];
        for (var i = 0; i < packed.Count; i++)
        {
            packedBytes[i * 2] = (byte)packed[i];
            packedBytes[i * 2 + 1] = (byte)(packed[i] >> 8);
        }

        var md5 = System.Security.Cryptography.MD5.HashData(packedBytes);
        var md5Words = new uint[4];
        for (var i = 0; i < 4; i++)
        {
            md5Words[i] = BitConverter.ToUInt32(md5, i * 4);
        }

        var byteLen = packed.Count * 2;
        var dwordLen = byteLen >> 2;
        var mixCount = (byteLen & 4) == 0 ? dwordLen : dwordLen - 1;
        var packedDwords = new uint[(packed.Count + 1) / 2];
        for (var i = 0; i < packedDwords.Length; i++)
        {
            var lo = packed[i * 2];
            var hi = i * 2 + 1 < packed.Count ? packed[i * 2 + 1] : (ushort)0;
            packedDwords[i] = lo | ((uint)hi << 16);
        }

        var pairA = new uint[2];
        var pairB = new uint[2];
        if (mixCount >= 2)
        {
            MixA(packedDwords, mixCount, md5Words, SeedA, pairA);
            MixB(packedDwords, mixCount, md5Words, SeedB, pairB);
        }

        var final8 = new byte[8];
        var low = pairA[0] ^ pairB[0];
        var high = pairA[1] ^ pairB[1];
        BitConverter.TryWriteBytes(final8, low);
        BitConverter.TryWriteBytes(final8.AsSpan(4), high);
        return Convert.ToBase64String(final8);
    }

    internal static string BuildCanonical(
        string extension,
        string userSid,
        string progId,
        string machineIdTrimmed,
        string timestampHex)
    {
        var mod = machineIdTrimmed[^1] % 3;
        var salt = mod switch
        {
            0 => Salt3822,
            1 => SaltD185,
            _ => Salt97B6,
        };
        if (mod == 0)
        {
            return string.Concat(salt, extension, timestampHex, machineIdTrimmed, progId, userSid);
        }

        if (mod == 1)
        {
            return string.Concat(salt, timestampHex, extension, userSid, machineIdTrimmed, progId);
        }

        return string.Concat(userSid, timestampHex, salt, extension, machineIdTrimmed, progId);
    }

    internal static long NormalizeFileTime(long fileTime)
    {
        if (!FileTimeToSystemTime(ref fileTime, out var system))
        {
            return fileTime;
        }

        return SystemTimeToFileTime(ref system, out var normalized) ? normalized : fileTime;
    }

    internal static string FormatTimestamp(long fileTime)
    {
        var high = (uint)(fileTime >> 32);
        var low = (uint)fileTime;
        return high.ToString("x8") + low.ToString("x8");
    }

    private static string StripBraces(string machineId)
    {
        var text = machineId.Trim();
        if (text.Length >= 2 && text[0] == '{' && text[^1] == '}')
        {
            return text[1..^1];
        }

        return text;
    }

    private static uint[] LoadSeed(byte[] bytes)
    {
        var words = new uint[4];
        for (var i = 0; i < 4; i++)
        {
            words[i] = BitConverter.ToUInt32(bytes, i * 4);
        }

        return words;
    }

    private static int FirstPass(EncoderState state, ushort[] input, int start, int end)
    {
        var cursor = start;
        var consumed = 0;
        var result = (2249744775UL * (uint)(end - start)) >> 32;
        var quota = (result + (((ulong)(end - start) - result) >> 1)) >> 7;
        while ((uint)quota > 1)
        {
            var limit = (uint)quota - 1U;
            uint processed = 0;
            while (processed < limit)
            {
                if (!BuildToken(input, cursor, end, out var token, out var kind, out var tokenLen))
                {
                    break;
                }

                consumed += tokenLen;
                cursor += tokenLen;
                EmitFirstPass(state, input, ref cursor, end, token, kind);
                processed++;
            }

            result = (2249744775UL * (uint)(end - cursor)) >> 32;
            quota = ((uint)result + (((uint)(end - cursor) - (uint)result) >> 1)) >> 7;
        }

        return consumed;
    }

    private static void SecondPass(EncoderState state, ushort[] input, int start, int end)
    {
        var cursor = start;
        while (cursor < end)
        {
            if (!BuildTokenSecondPass(input, cursor, end, out var token, out var kind, out var tokenLen))
            {
                break;
            }

            state.TotalUnits += tokenLen;
            cursor += tokenLen;
            EmitSecondPass(state, ref cursor, token, kind);
        }

        FlushSecondPass(state);
    }

    private static bool BuildToken(
        ushort[] input,
        int cursor,
        int end,
        out ulong token,
        out byte kind,
        out byte tokenLen)
    {
        token = 0;
        kind = 0;
        tokenLen = 0;
        if (cursor >= end)
        {
            return false;
        }

        var code = (uint)Lookup.TokenRegion[ClassFromWord(input[cursor]) + 256];
        var length = (uint)Lookup.TokenRegion[code];
        if (cursor + length > (uint)end)
        {
            return false;
        }

        var tableBase = 168U * code;
        for (;;)
        {
            uint pos = 0;
            if (length != 0)
            {
                while ((ulong)input[cursor + (int)pos] < (1UL << Lookup.TokenBitWidth[tableBase + pos]))
                {
                    pos++;
                    if (pos >= length)
                    {
                        goto done;
                    }
                }
            }

            code++;
            tableBase = 168U * code;
            length = Lookup.TokenRegion[code];
            if (length > (uint)(end - cursor))
            {
                break;
            }
        }

    done:
        token = PackToken(input, cursor, code, length, tableBase);
        kind = (byte)code;
        tokenLen = (byte)length;
        return true;
    }

    private static bool BuildTokenSecondPass(
        ushort[] input,
        int cursor,
        int end,
        out ulong token,
        out byte kind,
        out byte tokenLen)
    {
        token = 0;
        kind = 0;
        tokenLen = 0;
        if (cursor >= end)
        {
            return false;
        }

        var remain = (uint)(end - cursor);
        var code = (uint)Lookup.TokenRegion[ClassFromWord(input[cursor]) + 256];
        var length = (uint)Lookup.TokenRegion[code];
        if (length > remain)
        {
            code = Lookup.FallbackTokenCode[remain];
            length = Lookup.TokenRegion[code];
        }

        var tableBase = 168U * code;
        while (true)
        {
            uint pos = 0;
            var retry = false;
            if (length != 0)
            {
                var probe = cursor;
                while (probe < end)
                {
                    if ((ulong)input[probe] >= (1UL << Lookup.TokenBitWidth[tableBase + pos]))
                    {
                        code++;
                        tableBase = 168U * code;
                        length = Lookup.TokenRegion[code];
                        if (length <= remain)
                        {
                            retry = true;
                        }

                        break;
                    }

                    pos++;
                    probe++;
                    if (pos >= length)
                    {
                        break;
                    }
                }
            }

            if (!retry)
            {
                break;
            }
        }

        token = PackToken(input, cursor, code, length, tableBase, end);
        kind = (byte)code;
        tokenLen = (byte)length;
        return true;
    }

    private static ulong PackToken(ushort[] input, int cursor, uint code, uint length, uint tableBase, int end = int.MaxValue)
    {
        ulong token = (ulong)(byte)code << 56;
        var shift = 56;
        for (uint i = 0; i < (byte)length; i++)
        {
            if (cursor + i >= end)
            {
                break;
            }

            shift -= Lookup.TokenBitWidth[tableBase + i];
            token |= (ulong)input[cursor + (int)i] << shift;
        }

        return token;
    }

    private static void EmitFirstPass(
        EncoderState state,
        ushort[] input,
        ref int cursor,
        int end,
        ulong token,
        byte tokenKind)
    {
        if (token == state.Last && state.Repeat < 0xFF)
        {
            state.Repeat++;
            return;
        }

        if (state.Repeat != 0)
        {
            if (FindPrevious(state, state.Last, out var distance))
            {
                var repMinusOne = state.Repeat - 1U;
                ulong entry;
                if (distance >= 0x800)
                {
                    var packed = (token >> 17) | ((ulong)repMinusOne << 48) | RepeatTokenA;
                    MarkSeen(state, packed);
                    PushEntry(state, packed);
                    entry = state.Last;
                }
                else
                {
                    var span = ((distance - 1UL) & 0x03FFUL) | ((ulong)repMinusOne << 11);
                    entry = (token >> 27) | (span << 37) | RepeatTokenB;
                }

                MarkSeen(state, entry);
                PushEntry(state, entry);
                state.Last = RepeatSentinel;
                state.Repeat = 0;
                cursor -= distance >= 0x800
                    ? Lookup.LongRewind[tokenKind]
                    : Lookup.ShortRewind[tokenKind];
                return;
            }
        }
        else if (FindPrevious(state, token, out var distance) && cursor < end)
        {
            var nextKind = Lookup.TokenRegion[ClassFromWord(input[cursor]) + 256];
            var entry = distance >= 0x800
                ? token
                : (token >> 18) | ((distance - 1UL) << 46) | RepeatTokenC;
            MarkSeen(state, entry);
            PushEntry(state, entry);
            if (distance < 0x800)
            {
                cursor -= Lookup.ShortRewind[nextKind + 256];
            }

            state.Last = token;
            return;
        }

        MarkSeen(state, token);
        PushEntry(state, token);
        state.Last = token;
    }

    private static void EmitSecondPass(EncoderState state, ref int cursor, ulong token, byte tokenKind)
    {
        if (token == state.Last && state.Repeat < 0xFF)
        {
            state.Repeat++;
            return;
        }

        if (state.Repeat != 0 && FindPrevious(state, state.Last, out var distance))
        {
            var repMinusOne = state.Repeat - 1U;
            ulong entry;
            if (distance >= 0x800)
            {
                var packed = (token >> 17) | ((ulong)repMinusOne << 48) | RepeatTokenA;
                MarkSeen(state, packed);
                PushEntry(state, packed);
                entry = state.Last;
            }
            else
            {
                var span = ((distance - 1UL) & 0x03FFUL) | ((ulong)repMinusOne << 11);
                entry = (token >> 27) | (span << 37) | RepeatTokenB;
            }

            MarkSeen(state, entry);
            PushEntry(state, entry);
            state.Last = RepeatSentinel;
            state.Repeat = 0;
            cursor -= distance >= 0x800
                ? Lookup.LongRewind[tokenKind]
                : Lookup.ShortRewind[tokenKind];
            return;
        }

        MarkSeen(state, token);
        PushEntry(state, token);
        state.Last = token;
    }

    private static void FlushSecondPass(EncoderState state)
    {
        if (state.Repeat == 0)
        {
            return;
        }

        ulong entry;
        if (state.Last == RepeatSentinel)
        {
            entry = ((ulong)state.Repeat << 48) | RepeatTokenD;
        }
        else if (FindPrevious(state, state.Last, out var distance) && distance != 0)
        {
            var span = ((distance - 1UL) & 0x03FFUL) | ((ulong)(state.Repeat - 1) << 11);
            entry = (span << 37) | RepeatTokenE;
        }
        else
        {
            var repeatToken = ((ulong)state.Repeat << 48) | RepeatTokenD;
            MarkSeen(state, repeatToken);
            PushEntry(state, repeatToken);
            entry = state.Last;
        }

        MarkSeen(state, entry);
        PushEntry(state, entry);
        state.Repeat = 0;
        state.Last = RepeatSentinel;
    }

    private static bool MixA(uint[] words, int dwordCount, uint[] md5Words, uint[] seed, uint[] outPair)
    {
        if ((dwordCount & 1) != 0)
        {
            return false;
        }

        var md0 = md5Words[0] | 1U;
        var md1 = (md5Words[1] | 1U) + 0x13DB0000U;
        var seed0 = seed[0];
        var seed1 = seed[1];
        var seed2 = seed[2];
        var seed3 = seed[3];
        uint sum = 0;
        uint acc = 0;
        var index = 0;
        var remaining = dwordCount;
        var loops = ((uint)(remaining - 2) >> 1) + 1U;
        if (loops != 0)
        {
            var mul = md0 + seed0;
            while (loops-- != 0)
            {
                acc += words[index];
                remaining -= 2;
                var x = acc;
                var hi = x >> 16;
                index += 2;
                var t = Mul(x, mul) - Mul(hi, seed3);
                mul = md0 + seed0;
                var s = Mul(t, seed1) + Mul(t >> 16, 0x689B6B9FU);
                var part1 = Mul(s, 0xEA970001U) - Mul(s >> 16, 0x3C101569U);
                var w = words[index - 1] + part1;
                var t2 = Mul(w, md1) - Mul(w >> 16, seed2);
                var temp = Mul(t2, 0x59C3AF2DU) - Mul(t2 >> 16, 0x2232E0F1U);
                acc = Mul(temp >> 16, 0x35BD1EC9U) + Mul(temp, 0x1EC90001U);
                sum += part1 + acc;
            }
        }

        if (remaining == 1)
        {
            acc += words[index];
            var t = Mul(acc, md0 + seed0) - Mul(acc >> 16, seed3);
            var part = Mul(t, seed1) - Mul(t >> 16, 0x689B6B9FU);
            part += Mul(t >> 16, 0x39646B9FU);
            var t2 = Mul(part, md1) - Mul(part >> 16, seed2);
            var temp = Mul(t2, 0x59C3AF2DU) - Mul(t2 >> 16, 0x2232E0F1U);
            var extra = Mul(t2, 0x2A18AF2DU) + Mul(t2 >> 16, 0x02941F0FU) + Mul(temp >> 16, 0x35BD1EC9U);
            acc = extra;
            sum += part + acc;
        }

        outPair[0] = acc;
        outPair[1] = sum;
        return true;
    }

    private static bool MixB(uint[] words, int dwordCount, uint[] md5Words, uint[] seed, uint[] outPair)
    {
        if ((dwordCount & 1) != 0)
        {
            return false;
        }

        var seed0 = seed[0];
        var seed1 = seed[1];
        var seed2 = seed[2];
        var seed3 = seed[3];
        var md1 = md5Words[1] | 1U;
        var md0 = md5Words[0] | 1U;
        uint sum = 0;
        uint acc = 0;
        var index = 0;
        var remaining = dwordCount;
        var loops = ((uint)(remaining - 2) >> 1) + 1U;
        if (loops != 0)
        {
            while (loops-- != 0)
            {
                var x = words[index] + acc;
                remaining -= 2;
                index += 2;
                var t = Mul(x, md0);
                var a = Mul(t, 0xB1110000U) - Mul(t >> 16, 0x30674EEFU);
                var b = Mul(a, seed2);
                var c = b - Mul(a >> 16, 0x78F7A461U);
                c = Mul(c >> 16, seed0) - Mul(a >> 16, 0x164D0000U) - Mul(b, 0x46930000U);
                var d = Mul(c >> 16, 0x257E1D83U) + Mul(c, 0x1D830000U);
                var e = words[index - 1] + d;
                var f = Mul(e, md1);
                var g = Mul(f, 0x16F50000U) - Mul(f >> 16, 0x5D8BE90BU);
                var h = Mul(g >> 16, seed3);
                var i = Mul(g, 0x69010000U) + h;
                var j = Mul(i, seed1);
                var k = ((0U - h) - Mul(g, 0x69010000U)) >> 16;
                var m = Mul(k, 0x7C932B89U) - j;
                var n = Mul(k, 0xF2310000U) - Mul(m >> 16, 0x405B6097U);
                n += Mul(j, 0x60970000U);
                acc = n;
                sum += d + acc;
            }
        }

        if (remaining == 1)
        {
            var x = words[index] + acc;
            var t = Mul(x, md0);
            var a = Mul(t, 0xB1110000U) - Mul(t >> 16, 0x30674EEFU);
            var b = Mul(a, seed2);
            var c = Mul(a >> 16, 0xE9B30000U) - Mul(b, 0x46930000U) + Mul((b - Mul(a >> 16, 0x78F7A461U)) >> 16, seed0);
            var d = Mul(c >> 16, 0x257E1D83U) + Mul(c, 0x1D830000U);
            var e = Mul(d, md1);
            var f = Mul(e, 0x16F50000U) - Mul(e >> 16, 0x5D8BE90BU);
            var h = Mul(f >> 16, seed3);
            var i = Mul(f, 0x69010000U) + h;
            var j = Mul(i, seed1);
            var k = ((0U - h) - Mul(f, 0x69010000U)) >> 16;
            var m = Mul(k, 0x7C932B89U) - j;
            var n = Mul(k, 0xF2310000U) - Mul(m >> 16, 0x405B6097U);
            n += Mul(j, 0x60970000U);
            acc = n;
            sum += d + acc;
        }

        outPair[0] = acc;
        outPair[1] = sum;
        return true;
    }

    private static uint Mul(uint a, uint b) => unchecked((uint)((ulong)a * b));

    private static byte ClassFromWord(ushort ch)
    {
        var hi = (byte)(ch >> 8);
        if (hi != 0)
        {
            return (byte)(Lookup.ByteClass[hi] + 8);
        }

        return Lookup.ByteClass[(byte)ch];
    }

    private static uint HashIndex(ulong value) => (uint)(value % SeenModulo);

    private static bool TestSeen(EncoderState state, ulong value)
    {
        var idx = HashIndex(value);
        return (state.Seen[idx >> 6] & (1UL << (int)(idx & 63))) != 0;
    }

    private static void MarkSeen(EncoderState state, ulong value)
    {
        var idx = HashIndex(value);
        state.Seen[idx >> 6] |= 1UL << (int)(idx & 63);
    }

    private static void PushEntry(EncoderState state, ulong value)
    {
        if (state.Count >= (ulong)state.Entries.Length)
        {
            Array.Resize(ref state.Entries, state.Entries.Length + 1024);
        }

        state.Entries[state.Count++] = value;
    }

    private static bool FindPrevious(EncoderState state, ulong value, out ulong distance)
    {
        distance = 0;
        if (!TestSeen(state, value) || state.Count == 0)
        {
            return false;
        }

        var start = state.Count > 0x400 ? state.Count - 0x400 : 0UL;
        for (var idx = state.Count - 1;; idx--)
        {
            if (state.Entries[idx] == value)
            {
                distance = state.Count - idx;
                return true;
            }

            if (idx == start)
            {
                break;
            }
        }

        return false;
    }

    private static byte[] DecodeHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    [DllImport("kernel32.dll")]
    private static extern bool FileTimeToSystemTime(ref long fileTime, out SystemTime systemTime);

    [DllImport("kernel32.dll")]
    private static extern bool SystemTimeToFileTime(ref SystemTime systemTime, out long fileTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    private sealed class EncoderState
    {
        public ulong[] Entries = new ulong[4096];
        public readonly ulong[] Seen = new ulong[161];
        public ulong Count = 1;
        public ulong Last = RepeatSentinel;
        public uint Repeat;
        public uint TotalUnits;
    }

    private sealed class Tables
    {
        public required byte[] TokenRegion { get; init; }
        public required byte[] LongRewind { get; init; }
        public required byte[] FallbackTokenCode { get; init; }
        public required byte[] ByteClass { get; init; }
        public required byte[] TokenBitWidth { get; init; }
        public required byte[] ShortRewind { get; init; }

        public static Tables Load() => new()
        {
            TokenRegion = DecodeHex(UserChoiceLatestTables.TokenRegion),
            LongRewind = DecodeHex(UserChoiceLatestTables.LongRewindTable),
            FallbackTokenCode = DecodeHex(UserChoiceLatestTables.FallbackTokenCodeTable),
            ByteClass = DecodeHex(UserChoiceLatestTables.ByteClassTable),
            TokenBitWidth = DecodeHex(UserChoiceLatestTables.TokenBitWidthTable),
            ShortRewind = DecodeHex(UserChoiceLatestTables.ShortRewindTable),
        };
    }
}
