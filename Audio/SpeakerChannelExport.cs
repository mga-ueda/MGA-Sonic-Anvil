namespace MgaSonicAnvil.Audio;

/// <summary>スピーカー配置の各レーンをモノラル Wave にする。ファイルの本数や名前は見ない。</summary>
internal static class SpeakerChannelExport
{
    public readonly record struct Lane(string Suffix, int FileChannel);

    public static Lane[] Plan(int fileChannels, ChannelLayout speaker, int[]? fileChannelMap)
    {
        fileChannels = Math.Max(1, fileChannels);
        var speakers = Math.Max(1, speaker.Channels);
        var map = ChannelRouter.Normalize(fileChannelMap, speakers, fileChannels);
        var lanes = new List<Lane>(speakers);
        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] < 0)
            {
                continue;
            }

            lanes.Add(new Lane(Suffix(speaker.LabelAt(i), i), map[i]));
        }

        return [.. lanes];
    }

    public static string FileBaseName(string baseName, string suffix) =>
        $"{AudioExport.SanitizeBaseName(baseName)}_{AudioExport.SanitizeBaseName(suffix)}";

    public static AudioDocument ExtractMono(AudioDocument document, int fileChannel)
    {
        var channels = Math.Max(1, document.Channels);
        fileChannel = Math.Clamp(fileChannel, 0, channels - 1);
        var frames = (int)Math.Clamp(document.FrameCount, 0, int.MaxValue);
        var dest = new float[frames];
        var source = document.Interleaved;
        var limit = Math.Min(document.SampleCount, source.Length);
        for (var frame = 0; frame < frames; frame++)
        {
            var index = frame * channels + fileChannel;
            dest[frame] = (uint)index < (uint)limit ? source[index] : 0;
        }

        return new AudioDocument(
            dest,
            document.SampleRate,
            channels: 1,
            document.BitsPerSample,
            AudioFileKind.Wave,
            null);
    }

    private static string Suffix(string label, int index)
    {
        var suffix = AudioExport.SanitizeBaseName(label);
        return suffix == "untitled" ? (index + 1).ToString() : suffix;
    }
}
