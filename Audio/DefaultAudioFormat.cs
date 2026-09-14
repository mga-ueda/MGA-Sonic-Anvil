namespace MgaSonicAnvil.Audio;

/// <summary>新規ファイルなどの既定フォーマット。既定は 48 kHz / 24 bit / Stereo。</summary>
internal static class DefaultAudioFormat
{
    public const int SampleRate = 48000;
    public const int BitsPerSample = 24;
    public const string ChannelLayoutId = "Stereo";

    public static readonly int[] BitDepths = [8, 16, 24];

    public static int ClampSampleRate(int rate) =>
        FormatConvert.IsValidSampleRate(rate) ? rate : SampleRate;

    public static int ClampBitDepth(int bits) =>
        Array.IndexOf(BitDepths, bits) >= 0 ? bits : BitsPerSample;

    public static ChannelLayout ClampLayout(string? id) =>
        ChannelLayout.TryGet(id, out var layout) ? layout : ChannelLayout.Stereo;

    /// <summary>表示項目で有効な配置に Mono を足す。Mono は常に選べる。</summary>
    public static ChannelLayout[] PickerLayouts(IEnumerable<string>? visibleIds)
    {
        var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ChannelLayout.Mono.Id,
        };
        if (visibleIds is not null)
        {
            foreach (var id in visibleIds)
            {
                if (ChannelLayout.TryGet(id, out var layout))
                {
                    want.Add(layout.Id);
                }
            }
        }

        var list = new List<ChannelLayout>(want.Count);
        foreach (var layout in ChannelLayout.All)
        {
            if (want.Contains(layout.Id))
            {
                list.Add(layout);
            }
        }

        return list.Count > 0 ? [.. list] : [ChannelLayout.Mono];
    }

    public static ChannelLayout ClampPickerLayout(string? id, IEnumerable<string>? visibleIds)
    {
        var layouts = PickerLayouts(visibleIds);
        if (!string.IsNullOrWhiteSpace(id))
        {
            foreach (var layout in layouts)
            {
                if (layout.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    return layout;
                }
            }
        }

        foreach (var layout in layouts)
        {
            if (layout.Id.Equals(ChannelLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                return layout;
            }
        }

        return layouts[0];
    }

    public static Spec Resolve(
        int sampleRate,
        int bitsPerSample,
        string? channelLayoutId,
        IEnumerable<string>? visibleIds = null) =>
        new(
            ClampSampleRate(sampleRate),
            ClampBitDepth(bitsPerSample),
            visibleIds is null
                ? ClampLayout(channelLayoutId)
                : ClampPickerLayout(channelLayoutId, visibleIds));

    internal readonly record struct Spec(int SampleRate, int BitsPerSample, ChannelLayout Layout)
    {
        public int Channels => Layout.Channels;
    }
}
