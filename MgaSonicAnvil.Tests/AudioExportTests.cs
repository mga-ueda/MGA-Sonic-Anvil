using System.IO;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioExportTests
{
    [Fact]
    public void SuggestBaseName_UsesSourceStem()
    {
        Assert.Equal("tone", AudioExport.SuggestBaseName(Path.Combine(Path.GetTempPath(), "tone.wav"), "tone.wav"));
        Assert.Equal("untitled", AudioExport.SuggestBaseName(null, "   "));
        Assert.Equal("a_b", AudioExport.SuggestBaseName(null, "a:b"));
    }

    [Fact]
    public void SanitizeBaseName_ReplacesInvalidChars()
    {
        Assert.Equal("a_b", AudioExport.SanitizeBaseName("a:b"));
        Assert.Equal("untitled", AudioExport.SanitizeBaseName("   "));
        Assert.Equal("untitled", AudioExport.SanitizeBaseName("???"));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 4)]
    [InlineData(32, 8)]
    [InlineData(64, 16)]
    public void AutoWorkers_IsQuarterOfCores(int cores, int expected)
    {
        Assert.Equal(expected, AudioExport.AutoWorkers(cores));
        Assert.Equal(expected, AudioExport.ResolveWorkers(AudioExport.AutoParallelism, cores));
        Assert.True(AudioExport.AutoWorkers(cores) <= AudioExport.MaxWorkers(cores));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    [InlineData(16, 8)]
    [InlineData(32, 16)]
    [InlineData(64, 32)]
    public void MaxWorkers_IsHalfCores(int cores, int expected)
    {
        Assert.Equal(expected, AudioExport.MaxWorkers(cores));
    }

    [Fact]
    public void ResolveWorkers_ClampsToMax()
    {
        Assert.Equal(4, AudioExport.ResolveWorkers(99, 8));
        Assert.Equal(1, AudioExport.ResolveWorkers(1, 8));
        Assert.Equal(16, AudioExport.ResolveWorkers(99, 32));
        Assert.Equal(32, AudioExport.ResolveWorkers(99, 64));
    }

    [Fact]
    public void WorkerCount_UsesSettingAndJobCount()
    {
        Assert.Equal(1, AudioExport.WorkerCount(1, AudioExport.AutoParallelism, 16));
        Assert.Equal(4, AudioExport.WorkerCount(16, AudioExport.AutoParallelism, 16));
        Assert.Equal(8, AudioExport.WorkerCount(16, 8, 16));
        Assert.Equal(16, AudioExport.WorkerCount(32, AudioExport.AutoParallelism, 64));
        Assert.Equal(16, AudioExport.WorkerCount(32, 16, 64));
        Assert.Equal(32, AudioExport.WorkerCount(32, 32, 64));
        Assert.Equal(1, AudioExport.WorkerCount(16, 1, 16));
        Assert.Equal(2, AudioExport.WorkerCount(3, 2, 8));
    }

    [Fact]
    public void ExportProgressTracker_WeightsByFrameCount()
    {
        var tracker = new ExportProgressTracker(["short", "long"], [100, 900]);
        tracker.Report(1, 0.5, ExportJobState.Running);
        var snap = tracker.Capture();
        Assert.Equal(0.45, snap.Overall, 3);
        Assert.Equal(0, snap.Finished);
        var running = Assert.Single(snap.Running);
        Assert.Equal("long", running.Name);

        tracker.Report(1, 1, ExportJobState.Done);
        snap = tracker.Capture();
        Assert.Equal(0.9, snap.Overall, 3);
        Assert.Equal(1, snap.Finished);
        Assert.Empty(snap.Running);
    }

    [Fact]
    public void ExportProgressTracker_ReportsEveryRunningJob()
    {
        var names = Enumerable.Range(1, 32).Select(i => $"t{i}").ToArray();
        var frames = Enumerable.Repeat(1000L, 32).ToArray();
        var tracker = new ExportProgressTracker(names, frames);
        for (var i = 0; i < 32; i++)
        {
            tracker.Report(i, i / 32d, ExportJobState.Running);
        }

        var snap = tracker.Capture();
        Assert.Equal(32, snap.Running.Count);
        Assert.Equal("t1", snap.Running[0].Name);
        Assert.Equal("t32", snap.Running[31].Name);
    }

    [Fact]
    public void FormatOverwritePreview_ListsAllWhenFew()
    {
        var text = AudioExport.FormatOverwritePreview(["a.wav", "b.wav", "c.wav"]);
        Assert.Equal($"a.wav{Environment.NewLine}b.wav{Environment.NewLine}c.wav", text);
        Assert.DoesNotContain("32", text);
    }

    [Fact]
    public void FormatOverwritePreview_TruncatesWhenMany()
    {
        var names = Enumerable.Range(1, 32).Select(i => $"f{i}.wav").ToArray();
        var text = AudioExport.FormatOverwritePreview(names);
        Assert.Contains("f1.wav", text);
        Assert.Contains($"f{AudioExport.OverwritePreviewLimit}.wav", text);
        Assert.DoesNotContain($"f{AudioExport.OverwritePreviewLimit + 1}.wav", text);
        Assert.Contains("32", text);
        Assert.Contains($"{32 - AudioExport.OverwritePreviewLimit}", text);
    }

    [Fact]
    public void UniqueInDirectory_StartsAtBaseName()
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dir = Path.GetTempPath();
        var a = AudioExport.UniqueInDirectory(dir, "untitled", ".wav", reserved);
        var b = AudioExport.UniqueInDirectory(dir, "untitled", ".wav", reserved);
        Assert.Equal("untitled.wav", Path.GetFileName(a));
        Assert.Equal("untitled 2.wav", Path.GetFileName(b));
    }
}
