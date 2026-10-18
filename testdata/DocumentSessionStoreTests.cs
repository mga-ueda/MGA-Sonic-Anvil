using System.IO;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DocumentSessionStoreTests
{
    [Fact]
    public void CaptureAndApply_RestoresMarkersLoopRegions_ButClearsView()
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
        Assert.True(target.Selection.IsEmpty);
        Assert.Equal(0, target.CursorFrame);
        Assert.True(target.IsDirty);
    }

    [Fact]
    public void ApplyMeta_DirtyMarkerOnly_KeepsUnsavedFlagWithoutSessionAudio()
    {
        var source = MakeDocument();
        source.SourcePath = @"D:\src\tone.wav";
        source.TryAddMarker(24);
        source.SetDirty(true);

        var snap = DocumentSessionStore.Capture(source, DefaultView(), index: 0);
        Assert.True(snap.Dirty);
        // Capture 時点では名前が付くが、終了時にサンプル不変なら Skip で空になる。
        // 復元は元ファイル + メタだけ、という経路をここで再現する。
        snap.SessionFileName = string.Empty;

        var target = MakeDocument();
        target.SourcePath = snap.SourcePath;
        DocumentSessionStore.ApplyMeta(target, snap);

        Assert.True(target.IsDirty);
        Assert.Equal(snap.SourcePath, target.SourcePath);
        Assert.True(target.HasMarkerAt(24));
    }

    [Theory]
    [InlineData(false, true, false, 1, 0, false, 0)]
    [InlineData(true, true, true, 0, 0, false, 0)]
    [InlineData(true, true, false, 0, 0, true, 1)]
    [InlineData(true, true, false, 3, 0, true, 2)]
    [InlineData(true, true, false, 2, 0, false, 2)]
    [InlineData(true, false, false, 0, 0, false, 0)]
    public void DecideSessionAudioWrite_SkipsWhenSamplesUnchanged(
        bool needs,
        bool hasWorking,
        bool hasSource,
        int revision,
        int persisted,
        bool reusable,
        int expected)
    {
        Assert.Equal(
            expected,
            (int)DocumentSessionStore.DecideSessionAudioWrite(
                needs,
                hasWorking,
                hasSource,
                revision,
                persisted,
                reusable));
    }

    [Fact]
    public void HasUsableSessionAudio_RequiresNonEmptyWave()
    {
        var dir = NewTempDir("usable");
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "doc-0.wav"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(dir, "empty.wav"), []);
            Assert.True(DocumentSessionStore.HasUsableSessionAudio(dir, "doc-0.wav"));
            Assert.False(DocumentSessionStore.HasUsableSessionAudio(dir, "empty.wav"));
            Assert.False(DocumentSessionStore.HasUsableSessionAudio(dir, "missing.wav"));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void SampleRevision_IgnoresMarkerOnlyEdits()
    {
        var document = MakeDocument();
        Assert.Equal(0, document.SampleRevision);
        document.SetSampleLoop(new WaveSelection(5, 20), markDirty: true);
        document.ReplaceMarkers([new MarkerSnapshot(10, "A")], markDirty: true);
        Assert.Equal(0, document.SampleRevision);
        Assert.True(document.IsDirty);

        document.ReplaceRange(0, [0.1f, 0.2f]);
        Assert.Equal(1, document.SampleRevision);
    }

    [Fact]
    public void HistoryMatchesFile_DetectsUnchangedRecipes()
    {
        var dir = NewTempDir("history-match");
        var path = Path.Combine(dir, "doc-0-history.json");
        try
        {
            var snap = new HistorySessionSnapshot
            {
                CurrentIndex = 1,
                CleanIndex = 0,
                Recipes = [new HistoryRecipe { Kind = HistoryRecipes.AddMarker, Frame = 8 }],
            };
            Assert.True(DocumentSessionStore.TryWriteHistory(path, snap));
            Assert.True(DocumentSessionStore.HistoryMatchesFile(path, snap));
            snap.CurrentIndex = 0;
            Assert.False(DocumentSessionStore.HistoryMatchesFile(path, snap));
        }
        finally
        {
            TryDeleteDir(dir);
        }
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
    public void ShouldPersistHistorySidecars_OnlyWhenCurrentAudioIsMissing()
    {
        Assert.False(DocumentSessionStore.ShouldPersistHistorySidecars(hasSessionAudio: true, dirty: true, @"C:\a.wav"));
        Assert.False(DocumentSessionStore.ShouldPersistHistorySidecars(hasSessionAudio: false, dirty: false, @"C:\a.wav"));
        Assert.True(DocumentSessionStore.ShouldPersistHistorySidecars(hasSessionAudio: false, dirty: true, @"C:\a.wav"));
        Assert.True(DocumentSessionStore.ShouldPersistHistorySidecars(hasSessionAudio: false, dirty: true, null));
    }

    [Fact]
    public void RestorePredicates_SkipUnrestorableAndHistoryWhenCurrentAudioExists()
    {
        var root = NewTempDir("restore-pred");
        try
        {
            var sessionDir = Path.Combine(root, DocumentSessionStore.SessionDirectoryName);
            Directory.CreateDirectory(sessionDir);
            var sourcePath = Path.Combine(root, "source.wav");
            File.WriteAllText(sourcePath, "src");
            File.WriteAllBytes(Path.Combine(sessionDir, "doc-0.wav"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(sessionDir, "doc-0-origin.wav"), [1, 2, 3]);
            var history = new HistorySessionSnapshot
            {
                CurrentIndex = 1,
                Recipes = [new HistoryRecipe { Kind = HistoryRecipes.FadeIn, Start = 0, End = 8 }],
            };
            Assert.True(DocumentSessionStore.TryWriteHistory(
                Path.Combine(sessionDir, "doc-0-history.json"),
                history));

            var dirtyWithSession = new OpenDocumentSnapshot
            {
                SourcePath = sourcePath,
                Dirty = true,
                SessionFileName = "doc-0.wav",
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "doc-0-history.json",
            };
            Assert.True(DocumentSessionStore.HasCurrentAudio(root, dirtyWithSession));
            Assert.False(DocumentSessionStore.NeedsHistoryReplay(root, dirtyWithSession));
            Assert.True(DocumentSessionStore.CanRestoreDocument(root, dirtyWithSession));

            var cleanNamed = new OpenDocumentSnapshot
            {
                SourcePath = sourcePath,
                Dirty = false,
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "doc-0-history.json",
            };
            Assert.True(DocumentSessionStore.HasCurrentAudio(root, cleanNamed));
            Assert.False(DocumentSessionStore.NeedsHistoryReplay(root, cleanNamed));

            var dirtyNoSession = new OpenDocumentSnapshot
            {
                SourcePath = sourcePath,
                Dirty = true,
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "doc-0-history.json",
            };
            Assert.False(DocumentSessionStore.HasCurrentAudio(root, dirtyNoSession));
            Assert.True(DocumentSessionStore.NeedsHistoryReplay(root, dirtyNoSession));

            var markerOnlyDirty = new OpenDocumentSnapshot
            {
                SourcePath = sourcePath,
                Dirty = true,
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "marker-history.json",
            };
            Assert.True(DocumentSessionStore.TryWriteHistory(
                Path.Combine(sessionDir, "marker-history.json"),
                new HistorySessionSnapshot
                {
                    CurrentIndex = 1,
                    Recipes = [new HistoryRecipe { Kind = HistoryRecipes.AddMarker, Frame = 4 }],
                }));
            File.WriteAllBytes(Path.Combine(sessionDir, "doc-0-origin.wav"), [1, 2, 3]);
            Assert.True(DocumentSessionStore.HasCurrentAudio(root, markerOnlyDirty));
            Assert.False(DocumentSessionStore.NeedsHistoryReplay(root, markerOnlyDirty));

            var unknownHistory = new OpenDocumentSnapshot
            {
                Dirty = true,
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "bad-history.json",
            };
            Assert.True(DocumentSessionStore.TryWriteHistory(
                Path.Combine(sessionDir, "bad-history.json"),
                new HistorySessionSnapshot
                {
                    CurrentIndex = 1,
                    Recipes = [new HistoryRecipe { Kind = "NotARealEdit" }],
                }));
            Assert.False(DocumentSessionStore.HasRestorableHistory(sessionDir, unknownHistory));
            Assert.False(DocumentSessionStore.CanRestoreDocument(root, unknownHistory));

            var missing = new OpenDocumentSnapshot
            {
                SourcePath = Path.Combine(root, "gone.wav"),
                Dirty = true,
            };
            Assert.False(DocumentSessionStore.CanRestoreDocument(root, missing));

        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void ClosedSessionFileNames_AreManagedSidecars()
    {
        Assert.Equal("closed-2.wav", DocumentSessionStore.FileNameForClosedIndex(2));
        Assert.Equal("closed-2-origin.wav", DocumentSessionStore.OriginFileNameForClosedIndex(2));
        Assert.Equal("closed-2-history.json", DocumentSessionStore.HistoryFileNameForClosedIndex(2));
        Assert.Equal(
            "closed-2-origin.wav",
            DocumentSessionStore.SanitizeSidecarName(@"..\session\closed-2-origin.wav"));
        Assert.Equal(
            "closed-1-history.json",
            DocumentSessionStore.SanitizeSidecarName("closed-1-history.json"));
    }

    [Fact]
    public void CollectReferencedSessionFiles_KeepsOnlyFilesThatWillBeRead()
    {
        var root = NewTempDir("ref-files");
        try
        {
            var sessionDir = Path.Combine(root, DocumentSessionStore.SessionDirectoryName);
            Directory.CreateDirectory(sessionDir);
            var sourcePath = Path.Combine(root, "source.wav");
            File.WriteAllText(sourcePath, "src");
            File.WriteAllBytes(Path.Combine(sessionDir, "doc-0.wav"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(sessionDir, "doc-0-origin.wav"), [1, 2, 3]);
            Assert.True(DocumentSessionStore.TryWriteHistory(
                Path.Combine(sessionDir, "doc-0-history.json"),
                new HistorySessionSnapshot
                {
                    CurrentIndex = 1,
                    Recipes = [new HistoryRecipe { Kind = HistoryRecipes.FadeIn, Start = 0, End = 8 }],
                }));

            var dirtyWithSession = new OpenDocumentSnapshot
            {
                SourcePath = sourcePath,
                Dirty = true,
                SessionFileName = "doc-0.wav",
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "doc-0-history.json",
            };
            var keep = new List<string>();
            DocumentSessionStore.CollectReferencedSessionFiles(root, dirtyWithSession, keep);
            Assert.Equal(["doc-0.wav"], keep);

            var cleanNamed = new OpenDocumentSnapshot
            {
                SourcePath = sourcePath,
                Dirty = false,
                SessionFileName = "doc-0.wav",
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "doc-0-history.json",
            };
            keep.Clear();
            DocumentSessionStore.CollectReferencedSessionFiles(root, cleanNamed, keep);
            Assert.Empty(keep);

            var historyOnly = new OpenDocumentSnapshot
            {
                Dirty = true,
                OriginFileName = "doc-0-origin.wav",
                HistoryFileName = "doc-0-history.json",
            };
            keep.Clear();
            DocumentSessionStore.CollectReferencedSessionFiles(root, historyOnly, keep);
            Assert.Contains("doc-0-origin.wav", keep);
            Assert.Contains("doc-0-history.json", keep);
            Assert.DoesNotContain("doc-0.wav", keep);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void PruneUnreferencedSessionState_DeletesUnusedSidecarsAndAllClosedDocuments()
    {
        var root = NewTempDir("prune");
        try
        {
            var sessionDir = Path.Combine(root, DocumentSessionStore.SessionDirectoryName);
            Directory.CreateDirectory(sessionDir);
            var sourcePath = Path.Combine(root, "source.wav");
            var leftover = Path.Combine(root, "last-document.wav");
            File.WriteAllText(sourcePath, "src");
            File.WriteAllText(leftover, "old");
            File.WriteAllBytes(Path.Combine(sessionDir, "doc-0.wav"), [1]);
            File.WriteAllBytes(Path.Combine(sessionDir, "doc-0-origin.wav"), [2]);
            File.WriteAllText(Path.Combine(sessionDir, "doc-0-history.json"), "{}");
            File.WriteAllBytes(Path.Combine(sessionDir, "closed-0.wav"), [3]);

            var settings = new AppSettings
            {
                OpenDocuments =
                [
                    new OpenDocumentSnapshot
                    {
                        SourcePath = sourcePath,
                        Dirty = false,
                        SessionFileName = "doc-0.wav",
                        OriginFileName = "doc-0-origin.wav",
                        HistoryFileName = "doc-0-history.json",
                    },
                ],
                ClosedDocuments =
                [
                    new OpenDocumentSnapshot
                    {
                        CanContinueRecording = true,
                        Dirty = true,
                        SessionFileName = "closed-0.wav",
                    },
                ],
            };

            Assert.True(DocumentSessionStore.CanRestoreDocument(root, settings.ClosedDocuments[0]));
            Assert.True(DocumentSessionStore.PruneUnreferencedSessionState(root, settings, leftover));
            Assert.False(File.Exists(leftover));
            Assert.False(File.Exists(Path.Combine(sessionDir, "doc-0.wav")));
            Assert.False(File.Exists(Path.Combine(sessionDir, "doc-0-origin.wav")));
            Assert.False(File.Exists(Path.Combine(sessionDir, "doc-0-history.json")));
            Assert.False(File.Exists(Path.Combine(sessionDir, "closed-0.wav")));
            Assert.False(Directory.Exists(sessionDir));
            Assert.Equal(string.Empty, settings.OpenDocuments[0].SessionFileName);
            Assert.Equal(string.Empty, settings.OpenDocuments[0].OriginFileName);
            Assert.Empty(settings.ClosedDocuments);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void PruneUnreferencedSessionState_DropsLeftoverClosedRecordingsWithNoOpenTabs()
    {
        var root = NewTempDir("prune-closed-only");
        try
        {
            var sessionDir = Path.Combine(root, DocumentSessionStore.SessionDirectoryName);
            Directory.CreateDirectory(sessionDir);
            File.WriteAllBytes(Path.Combine(sessionDir, "closed-0.wav"), [4]);

            var settings = new AppSettings
            {
                ClosedDocuments =
                [
                    new OpenDocumentSnapshot
                    {
                        CanContinueRecording = true,
                        Dirty = true,
                        SessionFileName = "closed-0.wav",
                    },
                ],
            };

            Assert.True(DocumentSessionStore.CanRestoreDocument(root, settings.ClosedDocuments[0]));
            Assert.True(DocumentSessionStore.PruneUnreferencedSessionState(root, settings));
            Assert.False(File.Exists(Path.Combine(sessionDir, "closed-0.wav")));
            Assert.False(Directory.Exists(sessionDir));
            Assert.Empty(settings.ClosedDocuments);
            Assert.Empty(DocumentSessionStore.ResolveOpenDocuments(settings));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void ShouldPersistOpenDocuments_FalseInLibraryPlayer()
    {
        Assert.False(DocumentSessionStore.ShouldPersistOpenDocuments(libraryPlayer: true, sessionCount: 3));
        Assert.False(DocumentSessionStore.ShouldPersistOpenDocuments(libraryPlayer: true, sessionCount: 0));
        Assert.False(DocumentSessionStore.ShouldPersistOpenDocuments(libraryPlayer: false, sessionCount: 0));
        Assert.True(DocumentSessionStore.ShouldPersistOpenDocuments(libraryPlayer: false, sessionCount: 1));
    }

    [Fact]
    public void SanitizeSessionFileName_KeepsFileNameOnly()
    {
        Assert.Equal("doc-0.wav", DocumentSessionStore.SanitizeSessionFileName(@"..\session\doc-0.wav"));
        Assert.Null(DocumentSessionStore.SanitizeSessionFileName("doc-0.txt"));
        Assert.Null(DocumentSessionStore.SanitizeSessionFileName(""));
    }

    [Fact]
    public void ResolveSessionAudioPath_UsesSessionDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-session-path");
        var current = new OpenDocumentSnapshot { SessionFileName = "doc-2.wav" };

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
    public void ResolveOpenDocuments_UsesOpenDocumentsOnly()
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
    public void ResolveOpenDocuments_EmptyWhenNoSnapshots()
    {
        var settings = new AppSettings { LastDocumentPath = @"C:\legacy.wav" };
        Assert.Empty(DocumentSessionStore.ResolveOpenDocuments(settings));
    }

    [Fact]
    public void ResolveActiveIndex_PrefersIsActiveOverSavedIndex()
    {
        OpenDocumentSnapshot[] docs =
        [
            new() { SourcePath = @"C:\a.wav" },
            new() { SourcePath = @"C:\b.wav", IsActive = true },
            new() { SourcePath = @"C:\c.wav" },
        ];

        Assert.Equal(1, DocumentSessionStore.ResolveActiveIndex(docs, savedIndex: 0));
    }

    [Fact]
    public void ResolveActiveIndex_FallsBackToSavedIndex()
    {
        OpenDocumentSnapshot[] docs =
        [
            new() { SourcePath = @"C:\a.wav" },
            new() { SourcePath = @"C:\b.wav" },
        ];

        Assert.Equal(1, DocumentSessionStore.ResolveActiveIndex(docs, savedIndex: 1));
        Assert.Equal(1, DocumentSessionStore.ResolveActiveIndex(docs, savedIndex: 9));
    }

    [Fact]
    public void InsertRestoredBySourceIndex_KeepsOriginalOrder()
    {
        var sessions = new List<string>();
        var restored = new List<(int SourceIndex, string Item)>();
        var second = "b";
        var first = "a";

        DocumentSessionStore.InsertRestoredBySourceIndex(restored, sessions, 1, second);
        DocumentSessionStore.InsertRestoredBySourceIndex(restored, sessions, 0, first);

        Assert.Equal(["a", "b"], sessions);
        Assert.Equal([0, 1], restored.Select(item => item.SourceIndex).ToArray());
        Assert.Same(second, DocumentSessionStore.PickRestoredActive(restored, activeSourceIndex: 1));
    }

    [Fact]
    public void PickRestoredActive_SkipsMissingSource()
    {
        var kept = new object();
        var restored = new List<(int SourceIndex, object Item)>
        {
            (0, new object()),
            (2, kept),
        };

        Assert.Same(kept, DocumentSessionStore.PickRestoredActive(restored, activeSourceIndex: 2));
        Assert.Same(restored[0].Item, DocumentSessionStore.PickRestoredActive(restored, activeSourceIndex: 1));
    }

    [Fact]
    public void SettingsJson_RoundTripsOpenDocuments()
    {
        var settings = new AppSettings
        {
            ActiveDocumentIndex = 1,
            WaveformTileArrange = "grid",
            MultiFileArrange = "horizontal",
            OpenDocuments =
            [
                new OpenDocumentSnapshot
                {
                    SourcePath = @"C:\a.wav",
                    Dirty = true,
                    SessionFileName = "doc-0.wav",
                    TimeZoom = 2,
                    SelectedMarkerFrames = [8, 16],
                    IsActive = true,
                    CanContinueRecording = true,
                },
                new OpenDocumentSnapshot
                {
                    SourcePath = @"C:\b.wav",
                    MarkerFrames = [4],
                    MarkerComments = ["hit"],
                },
            ],
            ClosedDocuments =
            [
                new OpenDocumentSnapshot
                {
                    SessionFileName = "closed-0.wav",
                    CanContinueRecording = true,
                    ClosedIndex = 1,
                },
            ],
        };

        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(back);
        Assert.Equal(1, back.ActiveDocumentIndex);
        Assert.Equal("grid", back.WaveformTileArrange);
        Assert.Equal("horizontal", back.MultiFileArrange);
        Assert.Equal(2, back.OpenDocuments.Length);
        Assert.True(back.OpenDocuments[0].Dirty);
        Assert.Equal("doc-0.wav", back.OpenDocuments[0].SessionFileName);
        Assert.Equal([8, 16], back.OpenDocuments[0].SelectedMarkerFrames);
        Assert.True(back.OpenDocuments[0].IsActive);
        Assert.True(back.OpenDocuments[0].CanContinueRecording);
        Assert.Equal("hit", back.OpenDocuments[1].MarkerComments[0]);
        Assert.Single(back.ClosedDocuments);
        Assert.Equal("closed-0.wav", back.ClosedDocuments[0].SessionFileName);
        Assert.True(back.ClosedDocuments[0].CanContinueRecording);
        Assert.Equal(1, back.ClosedDocuments[0].ClosedIndex);
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
            Assert.Equal(0, restored.CursorFrame);
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
