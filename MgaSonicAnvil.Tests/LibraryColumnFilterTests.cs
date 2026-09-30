using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryColumnFilterTests
{
    [Fact]
    public void Resolve_Empty_UsesDefaultsIncludingNameParentFolderAndWaveform()
    {
        var resolved = LibraryColumnFilter.Resolve([]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
        Assert.Contains(LibraryFileColumn.Name, resolved);
        Assert.Contains(LibraryFileColumn.ParentFolder, resolved);
        Assert.Contains(LibraryFileColumn.Waveform, resolved);
        Assert.Contains(LibraryFileColumn.Title, resolved);
        Assert.Contains(LibraryFileColumn.Artist, resolved);
        Assert.Contains(LibraryFileColumn.Album, resolved);
        Assert.Contains(LibraryFileColumn.Track, resolved);
        Assert.Contains(LibraryFileColumn.Disc, resolved);
        Assert.Contains(LibraryFileColumn.Year, resolved);
        Assert.Contains(LibraryFileColumn.Genre, resolved);
        Assert.Contains(LibraryFileColumn.Composer, resolved);
        Assert.Contains(LibraryFileColumn.Duration, resolved);
        Assert.Contains(LibraryFileColumn.Comment, resolved);
        Assert.Contains(LibraryFileColumn.Date, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Jacket, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Kind, resolved);
        Assert.DoesNotContain(LibraryFileColumn.AlbumArtist, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Folder, resolved);
    }

    [Fact]
    public void Defaults_MatchStoredLibraryListColumnsOrder()
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
                LibraryFileColumn.Waveform,
            },
            LibraryColumnFilter.Defaults);
        Assert.Equal(
            LibraryColumnFilter.Defaults,
            LibraryColumnFilter.All[..LibraryColumnFilter.Defaults.Length]);
        Assert.Equal(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc",
                "Year", "Genre", "Date", "Comment", "ParentFolder", "Waveform",
            ],
            LibraryColumnFilter.Serialize(LibraryColumnFilter.Defaults));
        Assert.True(Array.IndexOf(LibraryColumnFilter.Defaults, LibraryFileColumn.ParentFolder)
            < Array.IndexOf(LibraryColumnFilter.Defaults, LibraryFileColumn.Waveform));
    }

    [Fact]
    public void Resolve_StoredNames_KeepsNameEvenIfOmitted()
    {
        var resolved = LibraryColumnFilter.Resolve(["Title", "BitRate", "Unknown"]);
        Assert.Equal(
            [LibraryFileColumn.Name, LibraryFileColumn.Title, LibraryFileColumn.BitRate],
            resolved);
    }

    [Fact]
    public void Resolve_LegacyDefaultsWithoutDate_AddsDateParentFolderAndWaveform()
    {
        var resolved = LibraryColumnFilter.Resolve(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
        Assert.Contains(LibraryFileColumn.Date, resolved);
        Assert.Contains(LibraryFileColumn.ParentFolder, resolved);
        Assert.Contains(LibraryFileColumn.Waveform, resolved);
        Assert.True(Array.IndexOf(resolved, LibraryFileColumn.Genre)
            < Array.IndexOf(resolved, LibraryFileColumn.Date));
        Assert.True(Array.IndexOf(resolved, LibraryFileColumn.Date)
            < Array.IndexOf(resolved, LibraryFileColumn.Comment));
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
        Assert.Equal(LibraryFileColumn.ParentFolder, resolved[^2]);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithDate_AddsParentFolderAndWaveform()
    {
        var resolved = LibraryColumnFilter.Resolve(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Date", "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
        Assert.Equal(LibraryFileColumn.ParentFolder, resolved[^2]);
    }

    [Fact]
    public void Resolve_ParentFolderAfterName_MovesToDefaultsWithWaveform()
    {
        var resolved = LibraryColumnFilter.Resolve(
            [
                "Name", "ParentFolder", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc",
                "Year", "Genre", "Date", "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithParentFolder_AddsWaveform()
    {
        var resolved = LibraryColumnFilter.Resolve(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Date", "Comment", "ParentFolder",
            ]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
        Assert.Equal(LibraryFileColumn.Waveform, resolved[^1]);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithDateAfterComment_MovesToCurrentDefaults()
    {
        var resolved = LibraryColumnFilter.Resolve(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Comment", "Date",
            ]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
    }

    [Fact]
    public void Resolve_PreviousDefaultWithDateBeforeGenre_MovesToCurrentDefaults()
    {
        var resolved = LibraryColumnFilter.Resolve(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Date",
                "Genre", "Comment",
            ]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
    }

    [Fact]
    public void Resolve_CustomOrder_KeepsDateWhereSaved()
    {
        var resolved = LibraryColumnFilter.Resolve(
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
            LibraryColumnFilter.Resolve(stored));
    }

    [Fact]
    public void Serialize_ThenResolve_RoundtripsVisibleOrder()
    {
        var stored = LibraryColumnFilter.Serialize(
            [LibraryFileColumn.Title, LibraryFileColumn.Folder, LibraryFileColumn.Name]);
        var resolved = LibraryColumnFilter.Resolve(stored);
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
            LibraryColumnFilter.Defaults,
            [LibraryFileColumn.Name, LibraryFileColumn.Album]);
        Assert.Equal([LibraryFileColumn.Name, LibraryFileColumn.Album], merged);
    }

    [Fact]
    public void UsedColumns_EmptyPlaylist_ReturnsNull()
    {
        Assert.Null(LibraryColumnFilter.UsedColumns([]));
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
        Assert.Contains(LibraryFileColumn.ParentFolder, used);
        Assert.Contains(LibraryFileColumn.Duration, used);
        Assert.DoesNotContain(LibraryFileColumn.Title, used);
        Assert.DoesNotContain(LibraryFileColumn.Artist, used);
        Assert.DoesNotContain(LibraryFileColumn.Album, used);
        Assert.DoesNotContain(LibraryFileColumn.Track, used);
        Assert.DoesNotContain(LibraryFileColumn.Jacket, used);
    }

    [Fact]
    public void UsedColumns_Mp3Only_CanHideParentFolderAndWaveform()
    {
        var row = new LibraryFileRow
        {
            Name = "song.mp3",
            Kind = "MP3",
            Title = "Song",
            ParentFolder = "album",
            DurationText = "0:01",
        };

        var used = LibraryColumnFilter.UsedColumns(
            [row],
            hideParentFolderForMp3Only: true,
            hideWaveformForMp3Only: true);
        Assert.NotNull(used);
        Assert.True(LibraryColumnFilter.IsMp3Only([row]));
        Assert.DoesNotContain(LibraryFileColumn.ParentFolder, used);
        Assert.DoesNotContain(LibraryFileColumn.Waveform, used);
        Assert.Contains(LibraryFileColumn.Title, used);

        var shown = LibraryColumnFilter.UsedColumns(
            [row],
            hideParentFolderForMp3Only: false,
            hideWaveformForMp3Only: false);
        Assert.NotNull(shown);
        Assert.Contains(LibraryFileColumn.ParentFolder, shown!);
        Assert.Contains(LibraryFileColumn.Waveform, shown);

        var mixed = LibraryColumnFilter.UsedColumns(
            [
                row,
                new LibraryFileRow { Name = "kick.wav", Kind = "WAVE", ParentFolder = "sfx" },
            ],
            hideParentFolderForMp3Only: true,
            hideWaveformForMp3Only: true);
        Assert.NotNull(mixed);
        Assert.False(LibraryColumnFilter.IsMp3Only(
            [
                row,
                new LibraryFileRow { Name = "kick.wav", Kind = "WAVE" },
            ]));
        Assert.Contains(LibraryFileColumn.ParentFolder, mixed!);
        Assert.Contains(LibraryFileColumn.Waveform, mixed);
    }

    [Fact]
    public void UsedColumns_Mp3WithoutArt_StillShowsJacketColumn()
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
        Assert.Contains(LibraryFileColumn.Jacket, used);
        Assert.Contains(LibraryFileColumn.Title, used);
        Assert.False(LibraryColumnFilter.JacketEligibleKind("WAVE"));
        Assert.False(LibraryColumnFilter.JacketEligibleKind("AIFF"));
        Assert.True(LibraryColumnFilter.JacketEligibleKind("MP3"));
        Assert.True(LibraryColumnFilter.JacketEligibleKind("M4A"));
    }

    [Fact]
    public void UsedColumns_WavePlusMp3_ShowsJacketColumn()
    {
        var wave = new LibraryFileRow { Name = "kick.wav", Kind = "WAVE" };
        var mp3 = new LibraryFileRow { Name = "song.mp3", Kind = "MP3" };
        var used = LibraryColumnFilter.UsedColumns([wave, mp3]);
        Assert.NotNull(used);
        Assert.Contains(LibraryFileColumn.Jacket, used);
    }

    [Fact]
    public void IsEffectivelyVisible_ShowsWhenUsedOrEmptyPlaylist()
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
    }
}
