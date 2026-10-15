namespace MgaSonicAnvil.Audio;

/// <summary>ヘッダ／タグだけから取った音楽情報。PCM は含まない。</summary>
internal sealed class AudioFileTags
{
    public static AudioFileTags Unprobed { get; } = new();

    public static AudioFileTags Empty { get; } = new() { Probed = true };

    public bool Probed { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    public string AlbumArtist { get; init; } = string.Empty;

    public string Track { get; init; } = string.Empty;

    public int TrackNumber { get; init; }

    public string Disc { get; init; } = string.Empty;

    public int DiscNumber { get; init; }

    public string Year { get; init; } = string.Empty;

    public int YearNumber { get; init; }

    public string Genre { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;

    public string Composer { get; init; } = string.Empty;

    public double DurationSeconds { get; init; }

    public int SampleRate { get; init; }

    public int BitsPerSample { get; init; }

    public int Channels { get; init; }

    public int BitRateKbps { get; init; }

    /// <summary>映像の横ピクセル。音声のみは 0。</summary>
    public int VideoWidth { get; init; }

    /// <summary>映像の縦ピクセル。音声のみは 0。</summary>
    public int VideoHeight { get; init; }

    /// <summary>映像のフレームレート。取れなければ 0。</summary>
    public double FrameRate { get; init; }

    /// <summary>映像 fourcc（avc1 など）。無ければ空。</summary>
    public string VideoCodec { get; init; } = string.Empty;

    /// <summary>音声 fourcc（mp4a / ipcm など）。無ければ空。</summary>
    public string AudioCodec { get; init; } = string.Empty;

    /// <summary>Integrated LUFS。未計測は null（カタログ／遅延計測で埋める）。</summary>
    public float? IntegratedLufs { get; init; }

    /// <summary>LAME Xing／Info または iTunSMPB／M4A elst の encoder delay（サンプル）。未記載は 0。</summary>
    public int EncoderDelayFrames { get; init; }

    /// <summary>LAME Xing／Info または iTunSMPB／M4A elst の padding（サンプル）。未記載は 0。</summary>
    public int EncoderPaddingFrames { get; init; }

    /// <summary>エンコード前の長さ（サンプル）。iTunSMPB または LAME から。未記載は 0。</summary>
    public long EncoderOriginalFrames { get; init; }

    public bool HasArtwork { get; init; }

    public AudioFileTags WithDurationSeconds(double seconds) =>
        WithPlaybackDisplay(seconds, SampleRate, Channels, BitsPerSample);

    public AudioFileTags WithAudioFormat(int sampleRate, int channels, int bitsPerSample, double durationSeconds) =>
        WithPlaybackDisplay(
            DurationSeconds > 0 ? DurationSeconds : durationSeconds,
            sampleRate,
            channels,
            bitsPerSample);

    private AudioFileTags WithPlaybackDisplay(double durationSeconds, int sampleRate, int channels, int bitsPerSample) =>
        new()
        {
            Probed = true,
            Title = Title,
            Artist = Artist,
            Album = Album,
            AlbumArtist = AlbumArtist,
            Track = Track,
            TrackNumber = TrackNumber,
            Disc = Disc,
            DiscNumber = DiscNumber,
            Year = Year,
            YearNumber = YearNumber,
            Genre = Genre,
            Comment = Comment,
            Composer = Composer,
            DurationSeconds = durationSeconds,
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample,
            Channels = channels,
            BitRateKbps = BitRateKbps,
            VideoWidth = VideoWidth,
            VideoHeight = VideoHeight,
            FrameRate = FrameRate,
            VideoCodec = VideoCodec,
            AudioCodec = AudioCodec,
            IntegratedLufs = IntegratedLufs,
            EncoderDelayFrames = EncoderDelayFrames,
            EncoderPaddingFrames = EncoderPaddingFrames,
            EncoderOriginalFrames = EncoderOriginalFrames,
            HasArtwork = HasArtwork,
        };

    public AudioFileTags WithIntegratedLufs(float? integratedLufs) =>
        new()
        {
            Probed = Probed,
            Title = Title,
            Artist = Artist,
            Album = Album,
            AlbumArtist = AlbumArtist,
            Track = Track,
            TrackNumber = TrackNumber,
            Disc = Disc,
            DiscNumber = DiscNumber,
            Year = Year,
            YearNumber = YearNumber,
            Genre = Genre,
            Comment = Comment,
            Composer = Composer,
            DurationSeconds = DurationSeconds,
            SampleRate = SampleRate,
            BitsPerSample = BitsPerSample,
            Channels = Channels,
            BitRateKbps = BitRateKbps,
            VideoWidth = VideoWidth,
            VideoHeight = VideoHeight,
            FrameRate = FrameRate,
            VideoCodec = VideoCodec,
            AudioCodec = AudioCodec,
            IntegratedLufs = integratedLufs,
            EncoderDelayFrames = EncoderDelayFrames,
            EncoderPaddingFrames = EncoderPaddingFrames,
            EncoderOriginalFrames = EncoderOriginalFrames,
            HasArtwork = HasArtwork,
        };
}

internal sealed class AudioFileTagsBuilder
{
    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    public string AlbumArtist { get; set; } = string.Empty;

    public string Track { get; set; } = string.Empty;

    public string Disc { get; set; } = string.Empty;

    public string Year { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public string Composer { get; set; } = string.Empty;

    public double DurationSeconds { get; set; }

    public int SampleRate { get; set; }

    public int BitsPerSample { get; set; }

    public int Channels { get; set; }

    public int BitRateKbps { get; set; }

    public int VideoWidth { get; set; }

    public int VideoHeight { get; set; }

    public double FrameRate { get; set; }

    public string VideoCodec { get; set; } = string.Empty;

    public string AudioCodec { get; set; } = string.Empty;

    public int LengthMillis { get; set; }

    public int EncoderDelayFrames { get; set; }

    public int EncoderPaddingFrames { get; set; }

    public long EncoderOriginalFrames { get; set; }

    public bool HasArtwork { get; set; }

    public AudioFileTags ToTags()
    {
        if (DurationSeconds <= 0 && LengthMillis > 0)
        {
            DurationSeconds = LengthMillis / 1000d;
        }

        if (BitRateKbps <= 0
            && SampleRate > 0
            && Channels > 0
            && BitsPerSample > 0)
        {
            BitRateKbps = (int)Math.Round(SampleRate * Channels * BitsPerSample / 1000d);
        }

        return new AudioFileTags
        {
            Probed = true,
            Title = Title,
            Artist = Artist,
            Album = Album,
            AlbumArtist = AlbumArtist,
            Track = Track,
            TrackNumber = AudioTagProbe.ParseLeadingInt(Track),
            Disc = Disc,
            DiscNumber = AudioTagProbe.ParseLeadingInt(Disc),
            Year = Year,
            YearNumber = AudioTagProbe.ParseLeadingInt(Year),
            Genre = Genre,
            Comment = Comment,
            Composer = Composer,
            DurationSeconds = DurationSeconds,
            SampleRate = SampleRate,
            BitsPerSample = BitsPerSample,
            Channels = Channels,
            BitRateKbps = BitRateKbps,
            VideoWidth = VideoWidth,
            VideoHeight = VideoHeight,
            FrameRate = FrameRate,
            VideoCodec = VideoCodec ?? string.Empty,
            AudioCodec = AudioCodec ?? string.Empty,
            IntegratedLufs = null,
            EncoderDelayFrames = EncoderDelayFrames,
            EncoderPaddingFrames = EncoderPaddingFrames,
            EncoderOriginalFrames = EncoderOriginalFrames,
            HasArtwork = HasArtwork,
        };
    }
}
