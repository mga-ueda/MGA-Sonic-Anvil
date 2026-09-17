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

    public bool HasArtwork { get; init; }
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

    public int LengthMillis { get; set; }

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
            HasArtwork = HasArtwork,
        };
    }
}
