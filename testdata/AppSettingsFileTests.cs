using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AppSettingsFileTests
{
    [Fact]
    public void WallpaperPreviewPath_AppliesWhenEmptyOrExisting()
    {
        Assert.True(WallpaperBitmap.TryPreviewPath("  ", out var empty));
        Assert.Equal(string.Empty, empty);
        Assert.False(WallpaperBitmap.TryPreviewPath(@"D:\missing\wallpaper.jpg", out _));
        var path = Path.Combine(Path.GetTempPath(), "mga-wallpaper-preview-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "x");
            Assert.True(WallpaperBitmap.TryPreviewPath("\"" + path + "\"", out var found));
            Assert.Equal(path, found);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void WallpaperTryLoad_DownscalesWideImagesWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-wallpaper-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            // 幅 2400 の小さな PNG。DecodeWidth 以下へ落とせることを確認する。
            WriteSolidPng(path, width: 2400, height: 8);
            var bitmap = WallpaperBitmap.TryLoad(path);
            Assert.NotNull(bitmap);
            Assert.True(bitmap!.PixelWidth <= WallpaperBitmap.DecodeWidth);
            Assert.True(bitmap.PixelWidth >= 1);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Wallpaper_DefaultsToNoneAtTenPercent()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal(string.Empty, settings.ResolvedWallpaperPath());
        Assert.Equal(0.10, settings.ResolvedWallpaperOpacity());
        Assert.Equal("10", AppSettings.FormatWallpaperOpacityPercent(settings.WallpaperOpacity));
        Assert.True(AppSettings.TryParseWallpaperOpacityPercent("10", out var opacity));
        Assert.Equal(0.10, opacity);
        Assert.False(AppSettings.TryParseWallpaperOpacityPercent("101", out _));
        Assert.Equal(1, AppSettings.ClampWallpaperOpacity(2));
        Assert.Equal(11, AppSettings.NudgeWallpaperOpacityPercent(10, 1));
        Assert.Equal(9, AppSettings.NudgeWallpaperOpacityPercent(10, -1));
        Assert.Equal(0, AppSettings.NudgeWallpaperOpacityPercent(0, -1));
        Assert.Equal(100, AppSettings.NudgeWallpaperOpacityPercent(100, 1));
        Assert.Equal(AppSettings.WallpaperOpacityDefault, AppSettings.ClampWallpaperOpacity(double.NaN));
    }

    [Fact]
    public void Write_PersistsWallpaperPathAndOpacity()
    {
        var settings = AppSettings.CreateDefault();
        settings.WallpaperPath = @"D:\Pictures\lake.jpg";
        settings.WallpaperOpacity = 0.10;
        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(back);
        Assert.Equal(@"D:\Pictures\lake.jpg", back!.ResolvedWallpaperPath());
        Assert.Equal(0.10, back.ResolvedWallpaperOpacity());
    }

    [Fact]
    public void Write_PersistsLibraryShuffleAndAutoPlayNext()
    {
        var settings = AppSettings.CreateDefault();
        settings.LibraryShuffle = true;
        settings.LibraryAutoPlayNext = true;
        settings.LibraryAutoLoop = true;
        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(back);
        Assert.True(back!.LibraryShuffle);
        Assert.True(back.LibraryAutoPlayNext);
        Assert.True(back.LibraryAutoLoop);
    }

    [Fact]
    public void LibraryListSort_EmptyIsAlbumAscending_NoneClears()
    {
        var settings = AppSettings.CreateDefault();
        Assert.Equal(
            new LibraryPlaylistSort(LibraryFileColumn.Album, LibrarySortDirection.Ascending),
            settings.ResolvedLibraryListSort());

        settings.ApplyLibraryListSort(new LibraryPlaylistSort(LibraryFileColumn.Name, LibrarySortDirection.Descending));
        Assert.Equal("Name", settings.LibraryListSortColumn);
        Assert.Equal("Descending", settings.LibraryListSortDirection);
        Assert.Equal(
            new LibraryPlaylistSort(LibraryFileColumn.Name, LibrarySortDirection.Descending),
            settings.ResolvedLibraryListSort());

        settings.ApplyLibraryListSort(new LibraryPlaylistSort(LibraryFileColumn.Album, LibrarySortDirection.Ascending));
        Assert.Equal(string.Empty, settings.LibraryListSortColumn);
        Assert.Equal(string.Empty, settings.LibraryListSortDirection);

        settings.ApplyLibraryListSort(new LibraryPlaylistSort(null, LibrarySortDirection.Ascending));
        Assert.Equal("None", settings.LibraryListSortColumn);
        Assert.Null(settings.ResolvedLibraryListSort().Column);
    }

    [Fact]
    public void Load_KeepsLibraryShuffle()
    {
        var root = NewTempDir("shuffle");
        try
        {
            var path = Path.Combine(root, "settings.json");
            var settings = AppSettings.CreateDefault();
            settings.LibraryShuffle = true;
            settings.LibraryAutoPlayNext = true;
            settings.LibraryAutoLoop = true;
            AppSettingsFile.Write(path, settings);

            var loaded = AppSettingsFile.Load(path, leftoverSessionDocumentPath: null, sessionDirectory: null, out var reset);
            Assert.Equal(SettingsFileReset.None, reset);
            Assert.True(loaded.LibraryShuffle);
            Assert.True(loaded.LibraryAutoPlayNext);
            Assert.True(loaded.LibraryAutoLoop);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryReadGeneration_MissingField_IsZero()
    {
        Assert.True(AppSettingsFile.TryReadGeneration("""{"AudioApi":"WaveOut"}""", out var generation));
        Assert.Equal(0, generation);
    }

    [Fact]
    public void TryReadGeneration_InvalidJson_Fails()
    {
        Assert.False(AppSettingsFile.TryReadGeneration("{", out _));
    }

    [Fact]
    public void Load_MissingFile_CreatesDefaultsWithoutReset()
    {
        var root = NewTempDir("missing");
        try
        {
            var path = Path.Combine(root, "settings.json");
            var settings = AppSettingsFile.Load(path, leftoverSessionDocumentPath: null, sessionDirectory: null, out var reset);
            Assert.Equal(SettingsFileReset.None, reset);
            Assert.Equal(AppSettings.CurrentGeneration, settings.SettingsGeneration);
            Assert.Equal(SpeakerPreset.DefaultId, settings.ActiveSpeakerPresetId);
            Assert.Equal(48000, settings.DefaultSampleRate);
            Assert.Equal(24, settings.DefaultBitsPerSample);
            Assert.Equal("Stereo", settings.DefaultChannelLayout);
            Assert.Equal(48000, settings.ResolvedDefaultSampleRate());
            Assert.Equal(24, settings.ResolvedDefaultBitsPerSample());
            Assert.Equal("Stereo", settings.ResolvedDefaultChannelLayout().Id);
            Assert.Equal(20, settings.ResolvedClickGuardFadeMs());
            Assert.Equal(500, settings.ResolvedWwisePrefetchLengthMs());
            Assert.Equal(500, settings.ResolvedWwiseLookAheadTimeMs());
            Assert.Equal(100, settings.ResolvedUiScalePercent());
            Assert.Equal(1, settings.ResolvedUiScale());
            Assert.False(File.Exists(path));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Load_OldGeneration_DeletesAndRecreates()
    {
        var root = NewTempDir("old");
        try
        {
            var path = Path.Combine(root, "settings.json");
            var leftover = Path.Combine(root, "last-document.wav");
            var sessionDir = Path.Combine(root, DocumentSessionStore.SessionDirectoryName);
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(path, """{"AudioApi":"Asio","LastDocumentPath":"C:\\old.wav"}""");
            File.WriteAllText(leftover, "wav");
            File.WriteAllText(Path.Combine(sessionDir, "doc-0.wav"), "session");

            var settings = AppSettingsFile.Load(path, leftover, sessionDir, out var reset);

            Assert.Equal(SettingsFileReset.Outdated, reset);
            Assert.Equal(AppSettings.CurrentGeneration, settings.SettingsGeneration);
            Assert.Equal(string.Empty, settings.LastDocumentPath);
            Assert.Equal("WaveOut", settings.AudioApi);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(leftover));
            Assert.False(File.Exists(Path.Combine(sessionDir, "doc-0.wav")));
            var written = JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsJsonContext.Default.AppSettings);
            Assert.NotNull(written);
            Assert.Equal(AppSettings.CurrentGeneration, written!.SettingsGeneration);
            Assert.Contains("\"LibraryShuffle\": false", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Load_InvalidJson_DeletesAndRecreates()
    {
        var root = NewTempDir("bad");
        try
        {
            var path = Path.Combine(root, "settings.json");
            File.WriteAllText(path, "{ not json");

            var settings = AppSettingsFile.Load(path, leftoverSessionDocumentPath: null, sessionDirectory: null, out var reset);

            Assert.Equal(SettingsFileReset.Invalid, reset);
            Assert.Equal(AppSettings.CurrentGeneration, settings.SettingsGeneration);
            Assert.True(File.Exists(path));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Load_CurrentGenerationWithoutWwiseTiming_UsesDefaults()
    {
        var root = NewTempDir("wwise-default");
        try
        {
            var path = Path.Combine(root, "settings.json");
            File.WriteAllText(path, """{"SettingsGeneration":1,"AudioApi":"WaveOut"}""");

            var settings = AppSettingsFile.Load(path, leftoverSessionDocumentPath: null, sessionDirectory: null, out var reset);

            Assert.Equal(SettingsFileReset.None, reset);
            Assert.Equal(500, settings.ResolvedWwisePrefetchLengthMs());
            Assert.Equal(500, settings.ResolvedWwiseLookAheadTimeMs());
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void ResolvedWwiseTiming_ClampsOutOfRange()
    {
        var settings = AppSettings.CreateDefault();
        settings.WwisePrefetchLengthMs = -10;
        settings.WwiseLookAheadTimeMs = 99999;
        Assert.Equal(0, settings.ResolvedWwisePrefetchLengthMs());
        Assert.Equal(10000, settings.ResolvedWwiseLookAheadTimeMs());
    }

    [Fact]
    public void Load_CurrentGeneration_KeepsValues()
    {
        var root = NewTempDir("current");
        try
        {
            var path = Path.Combine(root, "settings.json");
            var original = AppSettings.CreateDefault();
            original.LastDocumentPath = @"D:\keep.wav";
            original.LastOpenFolder = @"D:\opens";
            original.LastExportFolder = @"D:\exports";
            original.LastBouncePath = @"D:\exports\Bounce.wav";
            original.AlwaysOnTop = true;
            AppSettingsFile.Write(path, original);

            var settings = AppSettingsFile.Load(path, leftoverSessionDocumentPath: null, sessionDirectory: null, out var reset);

            Assert.Equal(SettingsFileReset.None, reset);
            Assert.Equal(@"D:\keep.wav", settings.LastDocumentPath);
            Assert.Equal(@"D:\opens", settings.LastOpenFolder);
            Assert.Equal(@"D:\exports", settings.LastExportFolder);
            Assert.Equal(@"D:\exports\Bounce.wav", settings.LastBouncePath);
            Assert.True(settings.AlwaysOnTop);
            Assert.Equal(AppSettings.CurrentGeneration, settings.SettingsGeneration);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static string NewTempDir(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mga-settings-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteSolidPng(string path, int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;
            pixels[i + 1] = 80;
            pixels[i + 2] = 140;
            pixels[i + 3] = 255;
        }

        var bmp = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 一時ディレクトリの後始末失敗は無視。
        }
    }
}
