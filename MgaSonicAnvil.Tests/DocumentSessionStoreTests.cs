using System.IO;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DocumentSessionStoreTests
{
    [Fact]
    public void CaptureAndApply_RestoresMarkersLoopRegionsAndSelection()
    {
        var source = MakeDocument();
        source.ReplaceMarkers([new MarkerSnapshot(10, "A"), new MarkerSnapshot(40, "B")], markDirty: false);
        source.SetSampleLoop(new WaveSelection(5, 20), markDirty: false);
        source.SetRegions([new WaveRegion(new WaveSelection(12, 18), "intro")], markDirty: false);
        source.Selection = new WaveSelection(2, 8);
        source.MarkUnsaved(@"D:\src\tone.wav");

        var view = new SessionViewState(30, 2.5, 1.5, 12, false, [40]);
        var snap = DocumentSessionStore.Capture(source, view, index: 1);

        Assert.True(snap.Dirty);
        Assert.Equal(@"D:\src\tone.wav", snap.SourcePath);
        Assert.Equal("doc-1.wav", snap.SessionFileName);
        Assert.Equal(30, snap.CursorFrame);
        Assert.Equal(2.5, snap.TimeZoom);
        Assert.False(snap.LoopEnabled);
        Assert.Equal([40], snap.SelectedMarkerFrames);

        var target = MakeDocument();
        DocumentSessionStore.ApplyMeta(target, snap);

        Assert.Equal(2, target.Markers.Count);
        Assert.Equal(10, target.Markers[0].Frame);
        Assert.Equal("A", target.Markers[0].Comment);
        Assert.Equal(5, target.SampleLoop.StartFrame);
        Assert.Equal(20, target.SampleLoop.EndFrame);
        Assert.Single(target.SnapshotRegions());
        Assert.Equal("intro", target.SnapshotRegions()[0].Name);
        Assert.Equal(2, target.Selection.StartFrame);
        Assert.Equal(8, target.Selection.EndFrame);
        Assert.Equal(30, target.CursorFrame);
        Assert.False(target.IsDirty);
    }

    [Fact]
    public void Capture_CleanNamedDocument_DoesNotNeedSessionFile()
    {
        var document = MakeDocument();
        document.SourcePath = @"C:\clean.wav";
        var snap = DocumentSessionStore.Capture(document, DefaultView(), index: 0);

        Assert.False(snap.Dirty);
        Assert.Equal(string.Empty, snap.SessionFileName);
        Assert.False(DocumentSessionStore.NeedsSessionAudio(snap.Dirty, snap.SourcePath));
    }

    [Fact]
    public void SanitizeSessionFileName_KeepsFileNameOnly()
    {
        Assert.Equal("doc-0.wav", DocumentSessionStore.SanitizeSessionFileName(@"..\session\doc-0.wav"));
        Assert.Null(DocumentSessionStore.SanitizeSessionFileName("doc-0.txt"));
        Assert.Null(DocumentSessionStore.SanitizeSessionFileName(""));
    }

    [Fact]
    public void ResolveSessionAudioPath_UsesLegacyRootForOldFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-session-path");
        var legacy = new OpenDocumentSnapshot { SessionFileName = DocumentSessionStore.LegacySessionFileName };
        var current = new OpenDocumentSnapshot { SessionFileName = "doc-2.wav" };

        Assert.Equal(
            Path.Combine(root, DocumentSessionStore.LegacySessionFileName),
            DocumentSessionStore.ResolveSessionAudioPath(root, legacy));
        Assert.Equal(
            Path.Combine(root, DocumentSessionStore.SessionDirectoryName, "doc-2.wav"),
            DocumentSessionStore.ResolveSessionAudioPath(root, current));
    }

    [Fact]
    public void TryResolveLoadPath_PrefersDirtySessionFile()
    {
        var root = NewTempDir("resolve");
        try
        {
            var sessionDir = Path.Combine(root, DocumentSessionStore.SessionDirectoryName);
            Directory.CreateDirectory(sessionDir);
            var sessionPath = Path.Combine(sessionDir, "doc-0.wav");
            var sourcePath = Path.Combine(root, "source.wav");
            File.WriteAllText(sessionPath, "session");
            File.WriteAllText(sourcePath, "source");

            var snap = new OpenDocumentSnapshot
            {
                SourcePath = sourcePath,
                Dirty = true,
                SessionFileName = "doc-0.wav",
            };

            Assert.True(DocumentSessionStore.TryResolveLoadPath(root, snap, out var path, out var fromSession));
            Assert.True(fromSession);
            Assert.Equal(sessionPath, path);

            snap.Dirty = false;
            Assert.True(DocumentSessionStore.TryResolveLoadPath(root, snap, out path, out fromSession));
            Assert.False(fromSession);
            Assert.Equal(sourcePath, path);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void ResolveOpenDocuments_PrefersMultiTabOverLegacy()
    {
        var settings = new AppSettings
        {
            LastDocumentPath = @"C:\old.wav",
            OpenDocuments = [new OpenDocumentSnapshot { SourcePath = @"C:\a.wav" }],
        };

        var docs = DocumentSessionStore.ResolveOpenDocuments(settings);
        Assert.Single(docs);
        Assert.Equal(@"C:\a.wav", docs[0].SourcePath);
    }

    [Fact]
    public void ResolveOpenDocuments_FallsBackToLegacySingleDocument()
    {
        var settings = new AppSettings
        {
            LastDocumentPath = @"C:\legacy.wav",
            LastDocumentDirty = true,
            LastCursorFrame = 99,
            LastTimeZoom = 3,
        };

        var docs = DocumentSessionStore.ResolveOpenDocuments(settings);
        Assert.Single(docs);
        Assert.Equal(@"C:\legacy.wav", docs[0].SourcePath);
        Assert.True(docs[0].Dirty);
        Assert.Equal(DocumentSessionStore.LegacySessionFileName, docs[0].SessionFileName);
        Assert.Equal(99, docs[0].CursorFrame);
        Assert.Equal(3, docs[0].TimeZoom);
        Assert.True(docs[0].LoopEnabled);
    }

    [Fact]
    public void SettingsJson_RoundTripsOpenDocuments()
    {
        var settings = new AppSettings
        {
            ActiveDocumentIndex = 1,
            OpenDocuments =
            [
                new OpenDocumentSnapshot
                {
                    SourcePath = @"C:\a.wav",
                    Dirty = true,
                    SessionFileName = "doc-0.wav",
                    TimeZoom = 2,
                    SelectedMarkerFrames = [8, 16],
                },
                new OpenDocumentSnapshot
                {
                    SourcePath = @"C:\b.wav",
                    MarkerFrames = [4],
                    MarkerComments = ["hit"],
                },
            ],
        };

        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(back);
        Assert.Equal(1, back.ActiveDocumentIndex);
        Assert.Equal(2, back.OpenDocuments.Length);
        Assert.True(back.OpenDocuments[0].Dirty);
        Assert.Equal("doc-0.wav", back.OpenDocuments[0].SessionFileName);
        Assert.Equal([8, 16], back.OpenDocuments[0].SelectedMarkerFrames);
        Assert.Equal("hit", back.OpenDocuments[1].MarkerComments[0]);
    }

    [Fact]
    public void RemoveOrphanSessionFiles_KeepsListedWaves()
    {
        var dir = NewTempDir("orphans");
        try
        {
            var keep = Path.Combine(dir, "doc-0.wav");
            var drop = Path.Combine(dir, "doc-1.wav");
            File.WriteAllText(keep, "keep");
            File.WriteAllText(drop, "drop");

            DocumentSessionStore.RemoveOrphanSessionFiles(dir, ["doc-0.wav"]);

            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(drop));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void SaveWaveThenLoad_KeepsUnsavedSamples()
    {
        var root = NewTempDir("audio");
        try
        {
            var sourcePath = Path.Combine(root, "source.wav");
            var original = MakeDocument(frames: 8, sample: 0.25f);
            AudioCodec.SaveWave(original, sourcePath);

            var edited = AudioCodec.Load(sourcePath);
            edited.Interleaved[0] = 0.9f;
            edited.MarkUnsaved(sourcePath);

            var sessionDir = Path.Combine(root, DocumentSessionStore.SessionDirectoryName);
            Directory.CreateDirectory(sessionDir);
            var snap = DocumentSessionStore.Capture(edited, DefaultView(playhead: 3), index: 0);
            AudioCodec.SaveWave(edited, Path.Combine(sessionDir, snap.SessionFileName));

            Assert.True(DocumentSessionStore.TryResolveLoadPath(root, snap, out var path, out var fromSession));
            Assert.True(fromSession);

            var restored = AudioCodec.Load(path);
            restored.MarkUnsaved(string.IsNullOrWhiteSpace(snap.SourcePath) ? null : snap.SourcePath);
            DocumentSessionStore.ApplyMeta(restored, snap);

            Assert.True(restored.IsDirty);
            Assert.Equal(sourcePath, restored.SourcePath);
            Assert.Equal(0.9f, restored.Interleaved[0], 3);
            Assert.Equal(3, restored.CursorFrame);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    private static SessionViewState DefaultView(long playhead = 0) =>
        new(playhead, 1, 1, 0, true, []);

    private static AudioDocument MakeDocument(int frames = 80, float sample = 0)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, sample);
        return new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
    }

    private static string NewTempDir(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mga-session-{suffix}-{Guid.NewGuid():N}");
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
