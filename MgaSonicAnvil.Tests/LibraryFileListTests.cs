using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryFileListTests
{
    [Fact]
    public void Sort_Duration_UsesSecondsNotText()
    {
        var rows = new[]
        {
            Row("b.wav", duration: 12, kind: "WAVE"),
            Row("a.mp3", duration: 3, kind: "MP3"),
            Row("c.wav", duration: 8, kind: "WAVE"),
        };

        var sorted = LibraryFileList.Sort(rows, LibraryFileColumn.Duration, LibrarySortDirection.Ascending);
        Assert.Equal(["a.mp3", "c.wav", "b.wav"], sorted.Select(row => row.Name).ToArray());

        sorted = LibraryFileList.Sort(rows, LibraryFileColumn.Duration, LibrarySortDirection.Descending);
        Assert.Equal(["b.wav", "c.wav", "a.mp3"], sorted.Select(row => row.Name).ToArray());
    }

    [Fact]
    public void Sort_KindThenName_IsStableOnNameTieBreak()
    {
        var rows = new[]
        {
            Row("kick.wav", kind: "WAVE", rate: 48000),
            Row("snare.mp3", kind: "MP3", rate: 44100),
            Row("hat.wav", kind: "WAVE", rate: 48000),
        };

        var sorted = LibraryFileList.Sort(rows, LibraryFileColumn.Kind, LibrarySortDirection.Ascending);
        Assert.Equal(["snare.mp3", "hat.wav", "kick.wav"], sorted.Select(row => row.Name).ToArray());
    }

    [Fact]
    public void GroupLabel_UsesFolderOrUntitled()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.English);
            var withFolder = Row("a.wav", folder: @"D:\sfx");
            var untitled = Row("b.wav", folder: "");
            Assert.Equal("WAVE", LibraryFileList.GroupLabel(withFolder, LibraryFileGroup.Kind));
            Assert.Equal("48kHz", LibraryFileList.GroupLabel(withFolder, LibraryFileGroup.SampleRate));
            Assert.Equal(@"D:\sfx", LibraryFileList.GroupLabel(withFolder, LibraryFileGroup.Folder));
            Assert.Equal("(Untitled)", LibraryFileList.GroupLabel(untitled, LibraryFileGroup.Folder));
            Assert.Equal(string.Empty, LibraryFileList.GroupLabel(withFolder, LibraryFileGroup.None));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void ApplyGroupKeys_WritesKind()
    {
        var rows = new[]
        {
            Row("a.wav", kind: "WAVE"),
            Row("b.mp3", kind: "MP3"),
        };
        LibraryFileList.ApplyGroupKeys(rows, LibraryFileGroup.Kind);
        Assert.Equal("WAVE", rows[0].GroupKey);
        Assert.Equal("MP3", rows[1].GroupKey);
    }

    [Fact]
    public void ParseGroup_EmptyOrUnknown_IsAlbum()
    {
        Assert.Equal(LibraryFileGroup.Album, LibraryFileList.ParseGroup(null));
        Assert.Equal(LibraryFileGroup.Album, LibraryFileList.ParseGroup(""));
        Assert.Equal(LibraryFileGroup.Album, LibraryFileList.ParseGroup("nope"));
        Assert.Equal(LibraryFileGroup.None, LibraryFileList.ParseGroup("None"));
        Assert.Equal(LibraryFileGroup.Album, LibraryFileList.ParseGroup("Album"));
        Assert.Equal("Album", LibraryFileList.SerializeGroup(LibraryFileGroup.Album));
    }

    [Fact]
    public void Sort_Jacket_PutsArtworkFirstWhenDescending()
    {
        var rows = new[]
        {
            Row("plain.wav", artwork: false),
            Row("cover.mp3", artwork: true),
        };
        var sorted = LibraryFileList.Sort(rows, LibraryFileColumn.Jacket, LibrarySortDirection.Descending);
        Assert.Equal("cover.mp3", sorted[0].Name);
    }

    [Fact]
    public void Sort_TitleThenName_UsesTitle()
    {
        var rows = new[]
        {
            Row("b.wav", title: "Zebra"),
            Row("a.wav", title: "Alpha"),
            Row("c.wav", title: "Alpha"),
        };
        var sorted = LibraryFileList.Sort(rows, LibraryFileColumn.Title, LibrarySortDirection.Ascending);
        Assert.Equal(["a.wav", "c.wav", "b.wav"], sorted.Select(row => row.Name).ToArray());
    }

    [Fact]
    public void Sort_Date_UsesTicksNotText()
    {
        var early = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
        var mid = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local);
        var late = new DateTime(2026, 9, 20, 23, 12, 0, DateTimeKind.Local);
        var rows = new[]
        {
            Row("b.wav", date: late),
            Row("a.wav", date: early),
            Row("c.wav", date: mid),
        };
        var sorted = LibraryFileList.Sort(rows, LibraryFileColumn.Date, LibrarySortDirection.Ascending);
        Assert.Equal(["a.wav", "c.wav", "b.wav"], sorted.Select(row => row.Name).ToArray());

        sorted = LibraryFileList.Sort(rows, LibraryFileColumn.Date, LibrarySortDirection.Descending);
        Assert.Equal(["b.wav", "c.wav", "a.wav"], sorted.Select(row => row.Name).ToArray());
    }

    [Fact]
    public void GroupLabel_BlankTitle_UsesPlaceholder()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.English);
            var blank = Row("a.wav", title: "");
            var titled = Row("b.wav", title: "Song");
            Assert.Equal("(None)", LibraryFileList.GroupLabel(blank, LibraryFileGroup.Title));
            Assert.Equal("Song", LibraryFileList.GroupLabel(titled, LibraryFileGroup.Title));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void SameContent_IgnoresTagAndKeepsDisplayFields()
    {
        var left = Row("a.wav", title: "Song", artwork: true);
        var right = Row("a.wav", title: "Song", artwork: true);
        Assert.True(LibraryFileList.SameContent(left, right));
        Assert.False(LibraryFileList.SameContent(left, Row("a.wav", title: "Other")));
        Assert.False(LibraryFileList.SameContent(left, Row("b.wav", title: "Song", artwork: true)));
    }

    [Fact]
    public void FormatKind_MatchesLibraryBrowser()
    {
        Assert.Equal("WAVE", MgaSonicAnvil.UI.LibraryBrowserView.FormatKind(MgaSonicAnvil.Audio.AudioFileKind.Wave));
        Assert.Equal("MP3", MgaSonicAnvil.UI.LibraryBrowserView.FormatKind(MgaSonicAnvil.Audio.AudioFileKind.Mp3));
        Assert.Equal("M4A", MgaSonicAnvil.UI.LibraryBrowserView.FormatKind(MgaSonicAnvil.Audio.AudioFileKind.M4a));
        Assert.Equal("AIFF", MgaSonicAnvil.UI.LibraryBrowserView.FormatKind(MgaSonicAnvil.Audio.AudioFileKind.Aiff));
    }

    private static LibraryFileRow Row(
        string name,
        double duration = 1,
        string kind = "WAVE",
        int rate = 48000,
        string folder = "",
        bool artwork = false,
        string title = "",
        DateTime date = default) =>
        new()
        {
            Name = name,
            Title = title,
            Kind = kind,
            DurationSeconds = duration,
            DurationText = duration.ToString("0"),
            SampleRate = rate,
            SampleRateText = UiStrings.FormatSampleRate(rate),
            BitDepth = 24,
            BitDepthText = UiStrings.FormatBitDepth(24),
            Channels = 2,
            ChannelsText = UiStrings.FormatChannels(2),
            FileBytes = 1000,
            SizeText = "1.0 KB",
            FileDate = date,
            DateText = UiStrings.FormatFileDate(date),
            Folder = folder,
            HasArtwork = artwork,
            JacketText = artwork ? UiStrings.LibraryJacketMark : string.Empty,
        };
}
