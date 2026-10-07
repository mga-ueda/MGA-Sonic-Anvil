using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryColumnFilterTests
{
    [Fact]
    public void Resolve_Empty_UsesDefaultsIncludingNameRateBitChParentFolderAndWaveform()
    {
        var resolved = LibraryColumnFilter.ResolveWave([]);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, resolved);
        Assert.Contains(LibraryFileColumn.Name, resolved);
        Assert.Contains(LibraryFileColumn.SampleRate, resolved);
        Assert.Contains(LibraryFileColumn.BitDepth, resolved);
        Assert.Contains(LibraryFileColumn.Channels, resolved);
        Assert.Contains(LibraryFileColumn.Duration, resolved);
        Assert.Contains(LibraryFileColumn.Date, resolved);
        Assert.Contains(LibraryFileColumn.ParentFolder, resolved);
        Assert.Contains(LibraryFileColumn.Waveform, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Title, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Artist, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Album, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Track, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Disc, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Year, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Genre, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Composer, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Comment, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Jacket, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Kind, resolved);
        Assert.DoesNotContain(LibraryFileColumn.AlbumArtist, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Folder, resolved);
    }

    [Fact]
    public void WaveDefaults_MatchStoredLibraryListColumnsOrder()
    {
        Assert.Equal(
            new[]
            {
                LibraryFileColumn.Name,
                LibraryFileColumn.SampleRate,
                LibraryFileColumn.BitDepth,
                LibraryFileColumn.Channels,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Date,
                LibraryFileColumn.ParentFolder,
                LibraryFileColumn.Waveform,
            },
            LibraryColumnFilter.WaveDefaults);
        Assert.Equal(
            LibraryColumnFilter.WaveDefaults,
            LibraryColumnFilter.All[..LibraryColumnFilter.WaveDefaults.Length]);
        Assert.Equal(
            [
                "Name", "SampleRate", "BitDepth", "Channels", "Duration", "Date", "ParentFolder",
                "Waveform",
            ],
            LibraryColumnFilter.Serialize(LibraryColumnFilter.WaveDefaults));
        Assert.True(Array.IndexOf(LibraryColumnFilter.WaveDefaults, LibraryFileColumn.SampleRate)
            < Array.IndexOf(LibraryColumnFilter.WaveDefaults, LibraryFileColumn.BitDepth));
        Assert.True(Array.IndexOf(LibraryColumnFilter.WaveDefaults, LibraryFileColumn.ParentFolder)
            < Array.IndexOf(LibraryColumnFilter.WaveDefaults, LibraryFileColumn.Waveform));
    }

    [Fact]
    public void SettingsCheckOrder_PutsDefaultsFirstThenFormatColumnsOnly()
    {
        var wave = LibraryColumnFilter.SettingsCheckOrder(
            LibraryColumnFilter.WaveDefaults,
            LibraryColumnFilter.WaveSettingsColumns).ToArray();
        Assert.Equal(LibraryFileColumn.SampleRate, wave[0]);
        Assert.Equal(LibraryFileColumn.BitDepth, wave[1]);
        Assert.Equal(LibraryFileColumn.Channels, wave[2]);
        Assert.Contains(LibraryFileColumn.Kind, wave);
        Assert.DoesNotContain(LibraryFileColumn.Name, wave);
        Assert.DoesNotContain(LibraryFileColumn.Title, wave);
        Assert.DoesNotContain(LibraryFileColumn.Jacket, wave);
        Assert.DoesNotContain(LibraryFileColumn.BitRate, wave);

        var mp3 = LibraryColumnFilter.SettingsCheckOrder(
            LibraryColumnFilter.Mp3Defaults,
            LibraryColumnFilter.Mp3SettingsColumns).ToArray();
        Assert.Equal(LibraryFileColumn.Title, mp3[0]);
        Assert.Equal(LibraryFileColumn.Album, mp3[1]);
        Assert.Contains(LibraryFileColumn.Jacket, mp3);
        Assert.Contains(LibraryFileColumn.SampleRate, mp3);
        Assert.DoesNotContain(LibraryFileColumn.BitDepth, mp3);
        Assert.True(Array.IndexOf(mp3, LibraryFileColumn.Waveform)
            < Array.IndexOf(mp3, LibraryFileColumn.SampleRate));
    }

    [Fact]
    public void Mp3Defaults_IncludeTagColumnsBeforeParentFolderWithoutWaveform()
    {
        Assert.Equal(
            new[]
            {
                LibraryFileColumn.Name,
                LibraryFileColumn.Title,
                LibraryFileColumn.Album,
                LibraryFileColumn.Artist,
                LibraryFileColumn.Composer,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Track,
                LibraryFileColumn.Disc,
                LibraryFileColumn.Year,
                LibraryFileColumn.Genre,
                LibraryFileColumn.Date,
                LibraryFileColumn.Comment,
                LibraryFileColumn.ParentFolder,
            },
            LibraryColumnFilter.Mp3Defaults);
        Assert.Equal(LibraryColumnFilter.Mp3Defaults, LibraryColumnFilter.ResolveMp3([]));
        Assert.DoesNotContain(LibraryFileColumn.SampleRate, LibraryColumnFilter.Mp3Defaults);
        Assert.DoesNotContain(LibraryFileColumn.Waveform, LibraryColumnFilter.Mp3Defaults);
    }

    [Fact]
    public void Resolve_StoredNames_KeepsNameEvenIfOmitted()
    {
        var resolved = LibraryColumnFilter.ResolveWave(["Title", "BitRate", "Unknown"]);
        Assert.Equal(
            [LibraryFileColumn.Name, LibraryFileColumn.Title, LibraryFileColumn.BitRate],
            resolved);
    }

    [Fact]
    public void Resolve_LegacyDefaultsWithoutDate_MovesToCurrentDefaults()
    {
        var resolved = LibraryColumnFilter.ResolveWave(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, resolved);
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
        Assert.Equal(LibraryFileColumn.ParentFolder, resolved[^2]);
        Assert.Equal(LibraryFileColumn.SampleRate, resolved[1]);
        Assert.Equal(LibraryFileColumn.BitDepth, resolved[2]);
        Assert.Equal(LibraryFileColumn.Channels, resolved[3]);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithDate_MovesToCurrentDefaults()
    {
        var resolved = LibraryColumnFilter.ResolveWave(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Date", "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, resolved);
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
        Assert.Equal(LibraryFileColumn.ParentFolder, resolved[^2]);
    }

    [Fact]
    public void Resolve_ParentFolderAfterName_MovesToDefaultsWithWaveform()
    {
        var resolved = LibraryColumnFilter.ResolveWave(
            [
                "Name", "ParentFolder", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc",
                "Year", "Genre", "Date", "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, resolved);
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithParentFolder_MovesToCurrentDefaults()
    {
        var resolved = LibraryColumnFilter.ResolveWave(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Date", "Comment", "ParentFolder",
            ]);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, resolved);
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithDateAfterComment_MovesToCurrentDefaults()
    {
        var resolved = LibraryColumnFilter.ResolveWave(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Comment", "Date",
            ]);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, resolved);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithDateBeforeGenre_MovesToCurrentDefaults()
    {
        var resolved = LibraryColumnFilter.ResolveWave(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Date",
                "Genre", "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, resolved);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithWaveform_WaveGoesToDefaults_Mp3KeepsWaveform()
    {
        string[] previous =
        [
            "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
            "Date", "Comment", "ParentFolder", "Waveform",
        ];
        Assert.Equal(LibraryColumnFilter.WaveDefaults, LibraryColumnFilter.ResolveWave(previous));
        Assert.Contains(LibraryFileColumn.Waveform, LibraryColumnFilter.ResolveMp3(previous));
        Assert.True(LibraryColumnFilter.IsLegacyDefaultStored(previous));
    }

    [Fact]
    public void Resolve_CustomOrder_KeepsDateWhereSaved()
    {
        var resolved = LibraryColumnFilter.ResolveWave(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Comment", "Date", "Folder",
            ]);
        Assert.Equal(
            [
                LibraryFileColumn.Name,
                LibraryFileColumn.Title,
                LibraryFileColumn.Album,
                LibraryFileColumn.Artist,
                LibraryFileColumn.Composer,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Track,
                LibraryFileColumn.Disc,
                LibraryFileColumn.Year,
                LibraryFileColumn.Genre,
                LibraryFileColumn.Comment,
                LibraryFileColumn.Date,
                LibraryFileColumn.Folder,
            ],
            resolved);
    }

    [Fact]
    public void Serialize_PreservesGivenOrder()
    {
        var stored = LibraryColumnFilter.Serialize(
            [LibraryFileColumn.Duration, LibraryFileColumn.Name, LibraryFileColumn.Title]);
        Assert.Equal(["Duration", "Name", "Title"], stored);
        Assert.Equal(
            [LibraryFileColumn.Duration, LibraryFileColumn.Name, LibraryFileColumn.Title],
            LibraryColumnFilter.ResolveWave(stored));
    }

    [Fact]
    public void Serialize_ThenResolve_RoundtripsVisibleOrder()
    {
        var stored = LibraryColumnFilter.Serialize(
            [LibraryFileColumn.Title, LibraryFileColumn.Folder, LibraryFileColumn.Name]);
        var resolved = LibraryColumnFilter.ResolveWave(stored);
        Assert.Equal(["Title", "Folder", "Name"], stored);
        Assert.Equal(stored, LibraryColumnFilter.Serialize(resolved));
        Assert.Equal(
            [LibraryFileColumn.Title, LibraryFileColumn.Folder, LibraryFileColumn.Name],
            resolved);
    }

    [Fact]
    public void Merge_KeepsPreviousOrderAndAppendsNew()
    {
        var merged = LibraryColumnFilter.Merge(
            [LibraryFileColumn.Title, LibraryFileColumn.Name, LibraryFileColumn.Duration],
            [
                LibraryFileColumn.Name,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Folder,
                LibraryFileColumn.Title,
            ]);
        Assert.Equal(
            [
                LibraryFileColumn.Title,
                LibraryFileColumn.Name,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Folder,
            ],
            merged);
    }

    [Fact]
    public void Merge_DropsUncheckedAndKeepsName()
    {
        var merged = LibraryColumnFilter.Merge(
            LibraryColumnFilter.WaveDefaults,
            [LibraryFileColumn.Name, LibraryFileColumn.Album]);
        Assert.Equal([LibraryFileColumn.Name, LibraryFileColumn.Album], merged);
    }

    [Fact]
    public void UsedColumns_EmptyPlaylist_ReturnsEmpty()
    {
        var used = LibraryColumnFilter.UsedColumns([]);
        Assert.NotNull(used);
        Assert.Empty(used);
        Assert.False(LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Name,
            LibraryColumnFilter.WaveDefaults,
            used));
    }

    [Fact]
    public void UsedColumns_WaveOnly_HidesEmptyTagColumns()
    {
        var row = new LibraryFileRow
        {
            Name = "kick.wav",
            Kind = "WAVE",
            DurationText = "0:01",
            SampleRateText = "48kHz",
            BitDepthText = "24-bit",
            ChannelsText = "2",
            SizeText = "1.0 KB",
            DateText = "2024-01-01",
            ParentFolder = "sfx",
            Folder = @"D:\sfx",
        };

        var used = LibraryColumnFilter.UsedColumns([row]);
        Assert.NotNull(used);
        Assert.Contains(LibraryFileColumn.Name, used);
        Assert.Contains(LibraryFileColumn.Waveform, used);
        Assert.Contains(LibraryFileColumn.Kind, used);
        Assert.Contains(LibraryFileColumn.SampleRate, used);
        Assert.Contains(LibraryFileColumn.BitDepth, used);
        Assert.Contains(LibraryFileColumn.Channels, used);
        Assert.Contains(LibraryFileColumn.ParentFolder, used);
        Assert.Contains(LibraryFileColumn.Duration, used);
        Assert.DoesNotContain(LibraryFileColumn.Title, used);
        Assert.DoesNotContain(LibraryFileColumn.Artist, used);
        Assert.DoesNotContain(LibraryFileColumn.Album, used);
        Assert.DoesNotContain(LibraryFileColumn.Track, used);
        Assert.DoesNotContain(LibraryFileColumn.Jacket, used);

        foreach (var column in LibraryColumnFilter.WaveDefaults)
        {
            Assert.True(
                LibraryColumnFilter.IsEffectivelyVisible(column, LibraryColumnFilter.WaveDefaults, used),
                column.ToString());
        }
    }

    [Fact]
    public void ClassifyPlaylistColumns_SeparatesWaveMp3AndMixed()
    {
        Assert.Equal(
            LibraryPlaylistColumnKind.Mixed,
            LibraryColumnFilter.ClassifyPlaylistColumns([]));
        Assert.Equal(
            LibraryPlaylistColumnKind.Wave,
            LibraryColumnFilter.ClassifyPlaylistColumns(
                [new LibraryFileRow { Name = "a.wav", Kind = "WAVE" }]));
        Assert.Equal(
            LibraryPlaylistColumnKind.Wave,
            LibraryColumnFilter.ClassifyPlaylistColumns(
                [
                    new LibraryFileRow { Name = "a.wav", Kind = "WAVE" },
                    new LibraryFileRow { Name = "b.aiff", Kind = "AIFF" },
                ]));
        Assert.Equal(
            LibraryPlaylistColumnKind.Mp3,
            LibraryColumnFilter.ClassifyPlaylistColumns(
                [
                    new LibraryFileRow { Name = "a.mp3", Kind = "MP3" },
                    new LibraryFileRow { Name = "b.m4a", Kind = "M4A" },
                ]));
        Assert.Equal(
            LibraryPlaylistColumnKind.Mp3,
            LibraryColumnFilter.ClassifyPlaylistColumns(
                [new LibraryFileRow { Name = "song.mp3", Kind = "WAVE" }]));
        Assert.Equal("MP3", LibraryColumnFilter.EffectiveKind(
            new LibraryFileRow { Name = "song.mp3", Kind = "WAVE" }));
        Assert.Equal(
            LibraryPlaylistColumnKind.Mp3,
            LibraryColumnFilter.ClassifyPlaylistColumns(
                [new LibraryFileRow { Name = "song.mp3", Kind = "Mp3" }]));
        Assert.Equal(
            LibraryPlaylistColumnKind.Mixed,
            LibraryColumnFilter.ClassifyPlaylistColumns(
                [
                    new LibraryFileRow { Name = "a.wav", Kind = "WAVE" },
                    new LibraryFileRow { Name = "b.mp3", Kind = "MP3" },
                ]));
    }

    [Fact]
    public void ResolveActive_UsesPresetOrUnion()
    {
        var wave = LibraryColumnFilter.WaveDefaults;
        var mp3 = LibraryColumnFilter.Mp3Defaults;
        Assert.Equal(
            wave,
            LibraryColumnFilter.ResolveActive(
                [new LibraryFileRow { Name = "a.wav", Kind = "WAVE" }],
                wave,
                mp3));
        Assert.Equal(
            mp3,
            LibraryColumnFilter.ResolveActive(
                [new LibraryFileRow { Name = "a.mp3", Kind = "MP3" }],
                wave,
                mp3));
        var mixed = LibraryColumnFilter.ResolveActive([], wave, mp3);
        Assert.Equal(LibraryColumnFilter.Union(wave, mp3), mixed);
        Assert.Equal(LibraryFileColumn.Name, mixed[0]);
        Assert.True(
            Array.IndexOf(mixed, LibraryFileColumn.SampleRate)
            < Array.IndexOf(mixed, LibraryFileColumn.Title));
        Assert.Contains(LibraryFileColumn.Title, mixed);
        Assert.Contains(LibraryFileColumn.SampleRate, mixed);
    }

    [Fact]
    public void ResolveActive_UsesStoredMixedWhenProvided()
    {
        var wave = LibraryColumnFilter.WaveDefaults;
        var mp3 = LibraryColumnFilter.Mp3Defaults;
        LibraryFileColumn[] mixed =
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.Title,
            LibraryFileColumn.SampleRate,
        ];
        Assert.Equal(
            mixed,
            LibraryColumnFilter.ResolveActive([], wave, mp3, mixed));
    }

    [Fact]
    public void ResolveMixed_EmptyUsesUnionOfCurrentPresets()
    {
        LibraryFileColumn[] wave =
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.Duration,
        ];
        LibraryFileColumn[] mp3 =
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.Title,
        ];
        Assert.Equal(
            LibraryColumnFilter.Union(wave, mp3),
            LibraryColumnFilter.ResolveMixed([], wave, mp3));
    }

    [Fact]
    public void ApplyVisibleChange_WaveOnlyUpdatesWavePreset()
    {
        LibraryColumnFilter.ApplyVisibleChange(
            LibraryPlaylistColumnKind.Wave,
            LibraryColumnFilter.WaveDefaults,
            LibraryColumnFilter.Mp3Defaults,
            LibraryColumnFilter.MixedDefaults,
            [LibraryFileColumn.Name, LibraryFileColumn.Duration, LibraryFileColumn.Title],
            out var wave,
            out var mp3,
            out var mixed);
        Assert.Equal(
            [LibraryFileColumn.Name, LibraryFileColumn.Duration, LibraryFileColumn.Title],
            wave);
        Assert.Equal(LibraryColumnFilter.Mp3Defaults, mp3);
        Assert.Equal(LibraryColumnFilter.Union(wave, mp3), mixed);
    }

    [Fact]
    public void ApplyVisibleChange_MixedOnlyUpdatesMixedPreset()
    {
        LibraryFileColumn[] next =
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.SampleRate,
            LibraryFileColumn.BitDepth,
            LibraryFileColumn.Title,
            LibraryFileColumn.Duration,
            LibraryFileColumn.Jacket,
        ];
        LibraryColumnFilter.ApplyVisibleChange(
            LibraryPlaylistColumnKind.Mixed,
            LibraryColumnFilter.WaveDefaults,
            LibraryColumnFilter.Mp3Defaults,
            LibraryColumnFilter.MixedDefaults,
            next,
            out var wave,
            out var mp3,
            out var mixed);
        Assert.Equal(LibraryColumnFilter.WaveDefaults, wave);
        Assert.Equal(LibraryColumnFilter.Mp3Defaults, mp3);
        Assert.Equal(next, mixed);
    }

    [Fact]
    public void AppSettings_ResolvedMixedEmptyFollowsUnion()
    {
        var settings = new AppSettings
        {
            LibraryListColumnsWave = ["Name", "Duration"],
            LibraryListColumnsMp3 = ["Name", "Title"],
        };
        Assert.Equal(
            LibraryColumnFilter.Union(
                settings.ResolvedLibraryListColumnsWave(),
                settings.ResolvedLibraryListColumnsMp3()),
            settings.ResolvedLibraryListColumnsMixed());
        Assert.Empty(settings.LibraryListColumnsMixed);
    }

    [Fact]
    public void AppSettings_ChangingWaveMp3ClearsMixedMemory()
    {
        var settings = new AppSettings
        {
            LibraryListColumnsWave = ["Name", "Duration"],
            LibraryListColumnsMp3 = ["Name", "Title"],
            LibraryListColumnsMixed = ["Name", "Title", "SampleRate"],
        };
        Assert.Equal(
            [LibraryFileColumn.Name, LibraryFileColumn.Title, LibraryFileColumn.SampleRate],
            settings.ResolvedLibraryListColumnsMixed());

        settings.ApplyLibraryListColumnPresets(
            [LibraryFileColumn.Name, LibraryFileColumn.BitDepth],
            [LibraryFileColumn.Name, LibraryFileColumn.Album]);
        Assert.Empty(settings.LibraryListColumnsMixed);
        Assert.Equal(
            LibraryColumnFilter.Union(
                settings.ResolvedLibraryListColumnsWave(),
                settings.ResolvedLibraryListColumnsMp3()),
            settings.ResolvedLibraryListColumnsMixed());
    }

    [Fact]
    public void AppSettings_MigratesLegacyDefaultToSeparatePresets()
    {
        var settings = new AppSettings
        {
            LibraryListColumns =
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Date", "Comment", "ParentFolder", "Waveform",
            ],
        };
        Assert.Equal(LibraryColumnFilter.WaveDefaults, settings.ResolvedLibraryListColumnsWave());
        Assert.Equal(LibraryColumnFilter.Mp3Defaults, settings.ResolvedLibraryListColumnsMp3());
        Assert.Empty(settings.LibraryListColumns);
    }

    [Fact]
    public void AppSettings_MigratesLegacyCustomToBothPresets()
    {
        var settings = new AppSettings
        {
            LibraryListColumns = ["Name", "Title", "BitRate"],
        };
        Assert.Equal(
            [LibraryFileColumn.Name, LibraryFileColumn.Title, LibraryFileColumn.BitRate],
            settings.ResolvedLibraryListColumnsWave());
        Assert.Equal(
            [LibraryFileColumn.Name, LibraryFileColumn.Title, LibraryFileColumn.BitRate],
            settings.ResolvedLibraryListColumnsMp3());
        Assert.Empty(settings.LibraryListColumns);
    }

    [Fact]
    public void UsedColumns_Mp3Only_KeepsParentFolderAndWaveformWhenPresent()
    {
        var row = new LibraryFileRow
        {
            Name = "song.mp3",
            Kind = "MP3",
            Title = "Song",
            ParentFolder = "album",
            DurationText = "0:01",
        };

        var used = LibraryColumnFilter.UsedColumns([row]);
        Assert.NotNull(used);
        Assert.Contains(LibraryFileColumn.ParentFolder, used);
        Assert.Contains(LibraryFileColumn.Waveform, used);
        Assert.Contains(LibraryFileColumn.Title, used);

        var mixed = LibraryColumnFilter.UsedColumns(
            [
                row,
                new LibraryFileRow { Name = "kick.wav", Kind = "WAVE", ParentFolder = "sfx" },
            ]);
        Assert.NotNull(mixed);
        Assert.Contains(LibraryFileColumn.ParentFolder, mixed!);
        Assert.Contains(LibraryFileColumn.Waveform, mixed);
    }

    [Fact]
    public void ResolveMp3_Mp3DefaultsWithWaveform_KeepsWaveform()
    {
        LibraryFileColumn[] withWave =
        [
            .. LibraryColumnFilter.Mp3Defaults,
            LibraryFileColumn.Waveform,
        ];
        var stored = LibraryColumnFilter.Serialize(withWave, LibraryColumnFilter.Mp3Defaults);
        var resolved = LibraryColumnFilter.ResolveMp3(stored);
        Assert.Contains(LibraryFileColumn.Waveform, resolved);
        Assert.Equal(withWave, resolved);
    }

    [Fact]
    public void UsedColumns_Mp3WithoutArt_HidesJacketColumn()
    {
        var row = new LibraryFileRow
        {
            Name = "song.mp3",
            Kind = "MP3",
            Title = "Song",
            DurationText = "0:01",
        };

        var used = LibraryColumnFilter.UsedColumns([row]);
        Assert.NotNull(used);
        Assert.DoesNotContain(LibraryFileColumn.Jacket, used);
        Assert.Contains(LibraryFileColumn.Title, used);
        Assert.True(LibraryColumnFilter.IsVideoFamilyKind("MOV"));
        Assert.True(LibraryColumnFilter.IsVisualFamilyKind("PDF"));
    }

    [Fact]
    public void UsedColumns_MovWithArt_ShowsJacketColumn()
    {
        var row = new LibraryFileRow
        {
            Name = "clip.mov",
            Kind = "MOV",
            HasArtwork = true,
            JacketText = UiStrings.LibraryJacketMark,
        };

        var used = LibraryColumnFilter.UsedColumns([row]);
        Assert.NotNull(used);
        Assert.Contains(LibraryFileColumn.Jacket, used);
    }

    [Fact]
    public void UsedColumns_WavePlusMp3_ShowsJacketColumn()
    {
        var wave = new LibraryFileRow { Name = "kick.wav", Kind = "WAVE" };
        var mp3 = new LibraryFileRow { Name = "song.mp3", Kind = "MP3", HasArtwork = true, JacketText = UiStrings.LibraryJacketMark };
        var used = LibraryColumnFilter.UsedColumns([wave, mp3]);
        Assert.NotNull(used);
        Assert.Contains(LibraryFileColumn.Jacket, used);
    }

    [Fact]
    public void IsEffectivelyVisible_ShowsWhenUsedOrNoFilter()
    {
        var enabled = new HashSet<LibraryFileColumn>
        {
            LibraryFileColumn.Name,
            LibraryFileColumn.Title,
            LibraryFileColumn.Artist,
        };
        Assert.True(LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Title,
            enabled,
            used: null));
        Assert.False(LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Title,
            enabled,
            new HashSet<LibraryFileColumn> { LibraryFileColumn.Name, LibraryFileColumn.Artist }));
        Assert.True(LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Name,
            enabled,
            new HashSet<LibraryFileColumn> { LibraryFileColumn.Name }));
        Assert.False(LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Album,
            enabled,
            used: null));
        Assert.False(LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Name,
            enabled,
            used: []));
    }
}
