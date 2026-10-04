using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using NAudio.MediaFoundation;
using NAudio.Wave;

const int SampleRate = 44100;
const int Channels = 2;
const float ToneHz = 440f;
const float ClickHz = 2000f;
const double ClickPeriod = 0.25;
const double ClickWidth = 0.02;
const int SecondsEach = 2;
const int LameFieldMax = 4095;

var repo = FindRepoRoot();
var outDir = Path.Combine(repo, "testdata", "gapless");
Directory.CreateDirectory(outDir);

var ffmpeg = ResolveFfmpeg();
if (ffmpeg is null)
{
    Console.Error.WriteLine("ffmpeg が見つかりません（PATH に libmp3lame 付きの ffmpeg が必要です）。");
    return 1;
}

// 2 秒ちょうどだと 440 Hz がゼロ交差するので、ピーク付近で割る。
var totalFrames = SampleRate * SecondsEach * 2;
var split = SampleRate * SecondsEach + 25;
var pcm = new float[totalFrames * Channels];
for (var i = 0; i < totalFrames; i++)
{
    var t = i / (double)SampleRate;
    var tone = 0.28 * Math.Sin(2 * Math.PI * ToneHz * t);
    var inClick = t % ClickPeriod < ClickWidth;
    var click = inClick ? 0.72 * Math.Sin(2 * Math.PI * ClickHz * t) : 0;
    var s = (float)Math.Clamp(tone + click, -1, 1);
    pcm[i * Channels] = s;
    pcm[i * Channels + 1] = s;
}

var wavProbe = Path.Combine(outDir, "_probe.wav");
var mp3Probe = Path.Combine(outDir, "_probe.mp3");
WriteWav(wavProbe, pcm, 0, split);
EncodeMp3(ffmpeg, wavProbe, mp3Probe, "probe");
if (!TryReadLame(mp3Probe, out var probeDelay, out var probePad, out _))
{
    Console.Error.WriteLine("LAME / Lavc の delay・padding が読めませんでした。");
    return 1;
}

var extraEnd = LameFieldMax - probePad;
var extraStart = LameFieldMax - probeDelay;
var wavA = Path.Combine(outDir, "_src-a.wav");
var wavB = Path.Combine(outDir, "_src-b.wav");
WriteWav(wavA, WithSilence(pcm, 0, split, lead: 0, tail: extraEnd), 0, split + extraEnd);
WriteWav(wavB, WithSilence(pcm, split, totalFrames - split, lead: extraStart, tail: 0), 0, extraStart + totalFrames - split);

var mp3A = Path.Combine(outDir, "01-tone-a.mp3");
var mp3B = Path.Combine(outDir, "02-tone-b.mp3");
EncodeMp3(ffmpeg, wavA, mp3A, "01 tone A (first half)");
EncodeMp3(ffmpeg, wavB, mp3B, "02 tone B (second half)");
if (!TryReadLame(mp3A, out var encDelayA, out var encPadA, out var lamePosA)
    || !TryReadLame(mp3B, out var encDelayB, out var encPadB, out var lamePosB))
{
    Console.Error.WriteLine("エンコード後の LAME タグが読めませんでした。");
    return 1;
}

var delayA = encDelayA;
var padA = Math.Min(LameFieldMax, encPadA + extraEnd);
var delayB = Math.Min(LameFieldMax, encDelayB + extraStart);
var padB = encPadB;
PatchLame(mp3A, lamePosA, delayA, padA);
PatchLame(mp3B, lamePosB, delayB, padB);

MediaFoundationApi.Startup();
var decodedA = DecodePcm(mp3A);
var decodedB = DecodePcm(mp3B);

var off = Concat(decodedA.Samples, decodedB.Samples);
var on = Concat(Trim(decodedA, delayA, padA), Trim(decodedB, delayB, padB));
WritePcm16Wav(Path.Combine(outDir, "listen-gapless-off.wav"), off, off.Length / Channels);
WritePcm16Wav(Path.Combine(outDir, "listen-gapless-on.wav"), on, on.Length / Channels);

var joinOff = JoinSilenceMs(decodedA, decodedB, trim: false, delayA, padA, delayB, padB);
var joinOn = JoinSilenceMs(decodedA, decodedB, trim: true, delayA, padA, delayB, padB);

File.WriteAllText(
    Path.Combine(outDir, "how-to-listen.txt"),
    BuildHowTo(delayA, padA, delayB, padB, decodedA.Frames, decodedB.Frames, joinOff, joinOn, extraStart, extraEnd),
    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

foreach (var leftover in new[] { wavProbe, mp3Probe, wavA, wavB })
{
    File.Delete(leftover);
}

Console.WriteLine(outDir);
Console.WriteLine($"A delay={delayA} padding={padA} decodedFrames={decodedA.Frames}");
Console.WriteLine($"B delay={delayB} padding={padB} decodedFrames={decodedB.Frames}");
Console.WriteLine($"join silence off≈{joinOff:0.0} ms  on≈{joinOn:0.0} ms");
return 0;

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "MgaSonicAnvil.csproj")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static string? ResolveFfmpeg()
{
    var fromPath = FindOnPath("ffmpeg.exe") ?? FindOnPath("ffmpeg");
    return File.Exists(fromPath ?? "") ? fromPath : null;
}

static string? FindOnPath(string name)
{
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            continue;
        }

        var candidate = Path.Combine(dir.Trim(), name);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static float[] WithSilence(float[] interleaved, int startFrame, int frames, int lead, int tail)
{
    var dest = new float[(lead + frames + tail) * Channels];
    Array.Copy(interleaved, startFrame * Channels, dest, lead * Channels, frames * Channels);
    return dest;
}

static void WriteWav(string path, float[] interleaved, int startFrame, int frames)
{
    var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
    using var writer = new WaveFileWriter(path, format);
    writer.WriteSamples(interleaved, startFrame * Channels, frames * Channels);
}

static void WritePcm16Wav(string path, float[] interleaved, int frames)
{
    var format = new WaveFormat(SampleRate, 16, Channels);
    using var writer = new WaveFileWriter(path, format);
    var count = frames * Channels;
    var pcm16 = new short[count];
    for (var i = 0; i < count; i++)
    {
        pcm16[i] = (short)Math.Clamp((int)Math.Round(interleaved[i] * 32767f), short.MinValue, short.MaxValue);
    }

    writer.WriteSamples(pcm16, 0, pcm16.Length);
}

static void EncodeMp3(string ffmpeg, string wav, string mp3, string title)
{
    var args = $"-y -i \"{wav}\" -c:a libmp3lame -b:a 192k -write_xing 1 -id3v2_version 3 -metadata title=\"{title}\" -metadata album=\"gapless listen test\" \"{mp3}\"";
    var start = new ProcessStartInfo(ffmpeg, args)
    {
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
    };
    using var proc = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg を起動できません。");
    proc.WaitForExit();
    if (proc.ExitCode != 0)
    {
        throw new InvalidOperationException(proc.StandardError.ReadToEnd());
    }
}

static Decoded DecodePcm(string path)
{
    using var reader = new MediaFoundationReader(path);
    var provider = reader.ToSampleProvider();
    var channels = Math.Max(1, provider.WaveFormat.Channels);
    var buffer = new float[4096];
    var all = new List<float>();
    int n;
    while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
    {
        for (var i = 0; i < n; i++)
        {
            all.Add(buffer[i]);
        }
    }

    var samples = all.ToArray();
    return new Decoded(samples, channels, samples.Length / channels);
}

static float[] Trim(Decoded decoded, int delay, int padding)
{
    var start = Math.Clamp(delay, 0, Math.Max(0, decoded.Frames - 1));
    var end = Math.Clamp(decoded.Frames - Math.Max(0, padding), start, decoded.Frames);
    var frames = end - start;
    var dest = new float[frames * decoded.Channels];
    Array.Copy(decoded.Samples, start * decoded.Channels, dest, 0, dest.Length);
    return dest;
}

static float[] Concat(float[] a, float[] b)
{
    var dest = new float[a.Length + b.Length];
    a.CopyTo(dest, 0);
    b.CopyTo(dest, a.Length);
    return dest;
}

static double JoinSilenceMs(
    Decoded a,
    Decoded b,
    bool trim,
    int delayA,
    int padA,
    int delayB,
    int padB)
{
    var aSamples = trim ? Trim(a, delayA, padA) : a.Samples;
    var bSamples = trim ? Trim(b, delayB, padB) : b.Samples;
    var ch = a.Channels;
    var tail = CountEdge(aSamples, ch, fromEnd: true);
    var head = CountEdge(bSamples, ch, fromEnd: false);
    return (tail + head) * 1000.0 / SampleRate;
}

static int CountEdge(float[] interleaved, int channels, bool fromEnd)
{
    var frames = interleaved.Length / channels;
    var count = 0;
    for (var i = 0; i < frames; i++)
    {
        var f = fromEnd ? frames - 1 - i : i;
        var peak = 0f;
        for (var c = 0; c < channels; c++)
        {
            peak = Math.Max(peak, Math.Abs(interleaved[f * channels + c]));
        }

        if (peak > 0.02f)
        {
            break;
        }

        count++;
    }

    return count;
}

static void PatchLame(string path, long lameInfoPosition, int delay, int padding)
{
    using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
    stream.Position = lameInfoPosition + 21;
    stream.WriteByte((byte)((delay >> 4) & 0xFF));
    stream.WriteByte((byte)(((delay & 0x0F) << 4) | ((padding >> 8) & 0x0F)));
    stream.WriteByte((byte)(padding & 0xFF));
}

static bool TryReadLame(string path, out int delay, out int padding, out long lameInfoPosition)
{
    delay = 0;
    padding = 0;
    lameInfoPosition = 0;
    using var stream = File.OpenRead(path);
    if (!SkipId3(stream))
    {
        return false;
    }

    var header = new byte[4];
    if (stream.Read(header, 0, 4) != 4 || header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
    {
        return false;
    }

    var mpeg1 = (header[1] & 0x08) != 0;
    var channelMode = (header[3] >> 6) & 3;
    var channels = channelMode == 3 ? 1 : 2;
    var side = mpeg1 ? (channels == 1 ? 17 : 32) : (channels == 1 ? 9 : 17);
    stream.Position += side;
    var tag = new byte[4];
    if (stream.Read(tag, 0, 4) != 4)
    {
        return false;
    }

    var id = Encoding.ASCII.GetString(tag);
    if (id is not ("Xing" or "Info"))
    {
        return false;
    }

    var flagsBytes = new byte[4];
    if (stream.Read(flagsBytes, 0, 4) != 4)
    {
        return false;
    }

    var flags = BinaryPrimitives.ReadInt32BigEndian(flagsBytes);
    if ((flags & 1) != 0)
    {
        stream.Position += 4;
    }

    if ((flags & 2) != 0)
    {
        stream.Position += 4;
    }

    if ((flags & 4) != 0)
    {
        stream.Position += 100;
    }

    if ((flags & 8) != 0)
    {
        stream.Position += 4;
    }

    lameInfoPosition = stream.Position;
    var lame = new byte[24];
    if (stream.Read(lame, 0, 24) != 24)
    {
        return false;
    }

    var marker = Encoding.ASCII.GetString(lame, 0, 4);
    if (marker is not ("LAME" or "GOGO" or "Lavc" or "Lavf"))
    {
        return false;
    }

    delay = (lame[21] << 4) | (lame[22] >> 4);
    padding = ((lame[22] & 0x0F) << 8) | lame[23];
    return delay > 0 || padding > 0;
}

static bool SkipId3(Stream stream)
{
    var h = new byte[10];
    if (stream.Read(h, 0, 10) != 10)
    {
        return false;
    }

    if (h[0] == 'I' && h[1] == 'D' && h[2] == '3')
    {
        var size = ((h[6] & 0x7F) << 21) | ((h[7] & 0x7F) << 14) | ((h[8] & 0x7F) << 7) | (h[9] & 0x7F);
        stream.Position = 10 + size;
        return true;
    }

    stream.Position = 0;
    return true;
}

static string BuildHowTo(
    int delayA,
    int padA,
    int delayB,
    int padB,
    int framesA,
    int framesB,
    double joinOff,
    double joinOn,
    int extraStart,
    int extraEnd) =>
    """
    ギャップレス聞き比べ用（440 Hz の連続正弦 + 0.25 秒おきのクリック）

    元は約 4 秒の一本の音を二つに割ったものです。聞き分けしやすいよう、1 曲目の尻と 2 曲目の頭に
    無音を足し、LAME タグの delay／padding にその量を足してあります（12 bit の上限 4095 サンプル）。
    本物の CD リップでは数十 ms 程度なので、普段はここまで大きく途切れません。

    【いちばん分かりやすい】WAV を直接聞く（アプリ不要）
      listen-gapless-off.wav … デコード結果をそのままつなぐ（設定オフ相当）
      listen-gapless-on.wav  … LAME の delay/padding を外してつなぐ（設定オン相当）
    オフは継ぎ目で 440 Hz が落ち、クリックの間隔も乱れます。オンはほぼ一本の音です。
    オンでもごく短い切れ目は残ることがあります。

    【アプリで確認】
    1. 01-tone-a.mp3 → 02-tone-b.mp3 の順でプレイリストへ。
    2. 設定 → プレイヤー → ギャップレス再生をオフ。1 曲目から連続再生。2 秒付近で無音がはっきり入る。
    3. ギャップレス再生をオンにして同じ場所をもう一度。切れ目が小さくなる。
    4. 再生はストリーム開始から。波形の完成は待たない。

    このファイルを作ったときのタグ（サンプル）
    """ + $"""

      足した無音 B頭={extraStart} / A尻={extraEnd}
      01 delay={delayA} padding={padA}  decoded={framesA}
      02 delay={delayB} padding={padB}  decoded={framesB}
      継ぎ目の無音 オフ≈{joinOff:0.0} ms / オン≈{joinOn:0.0} ms

    再生成: dotnet run --project tools/MakeGaplessTestMp3.csproj
    """;

readonly record struct Decoded(float[] Samples, int Channels, int Frames);
