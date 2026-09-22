using System.IO;
using System.Text.Json;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AppSettingsFileTests
{
    [Fact]
    public void Write_OmitsLibraryShuffle()
    {
        var json = JsonSerializer.Serialize(AppSettings.CreateDefault(), AppSettingsJsonContext.Default.AppSettings);
        Assert.DoesNotContain("LibraryShuffle", json, StringComparison.Ordinal);
        Assert.False(AppSettingsFile.ContainsLegacyLibraryShuffle(json));
    }

    [Fact]
    public void Load_RewritesLegacyLibraryShuffleOut()
    {
        var root = NewTempDir("shuffle");
        try
        {
            var path = Path.Combine(root, "settings.json");
            AppSettingsFile.Write(path, AppSettings.CreateDefault());
            var json = File.ReadAllText(path).Replace(
                "\"SettingsGeneration\": 1,",
                "\"SettingsGeneration\": 1,\n  \"LibraryShuffle\": true,",
                StringComparison.Ordinal);
            File.WriteAllText(path, json);
            Assert.True(AppSettingsFile.ContainsLegacyLibraryShuffle(json));

            var settings = AppSettingsFile.Load(path, leftoverSessionDocumentPath: null, sessionDirectory: null, out var reset);
            Assert.Equal(SettingsFileReset.None, reset);
            Assert.Equal(AppSettings.CurrentGeneration, settings.SettingsGeneration);
            Assert.False(AppSettingsFile.ContainsLegacyLibraryShuffle(File.ReadAllText(path)));
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
            Assert.DoesNotContain("LibraryShuffle", File.ReadAllText(path), StringComparison.Ordinal);
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
            original.AlwaysOnTop = true;
            AppSettingsFile.Write(path, original);

            var settings = AppSettingsFile.Load(path, leftoverSessionDocumentPath: null, sessionDirectory: null, out var reset);

            Assert.Equal(SettingsFileReset.None, reset);
            Assert.Equal(@"D:\keep.wav", settings.LastDocumentPath);
            Assert.Equal(@"D:\opens", settings.LastOpenFolder);
            Assert.Equal(@"D:\exports", settings.LastExportFolder);
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
