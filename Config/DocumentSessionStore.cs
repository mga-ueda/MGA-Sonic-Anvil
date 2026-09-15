using System.IO;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.Config;

internal readonly record struct SessionViewState(
    long PlayheadFrame,
    double TimeZoom,
    double AmpZoom,
    double ViewStart,
    bool LoopEnabled,
    IReadOnlyList<long> SelectedMarkerFrames);

/// <summary>終了時に残すタブ 1 本分。未保存なら SessionFileName の作業コピーから戻す。</summary>
internal sealed class OpenDocumentSnapshot
{
    public string SourcePath { get; set; } = string.Empty;

    public bool Dirty { get; set; }

    public string SessionFileName { get; set; } = string.Empty;

    public long CursorFrame { get; set; }

    public long SelectionStart { get; set; }

    public long SelectionEnd { get; set; }

    public double TimeZoom { get; set; } = 1;

    public double AmpZoom { get; set; } = 1;

    public double ViewStart { get; set; }

    public bool LoopEnabled { get; set; } = true;

    public long SampleLoopStart { get; set; }

    public long SampleLoopEnd { get; set; }

    public long[] RegionStarts { get; set; } = [];

    public long[] RegionEnds { get; set; } = [];

    public string[] RegionNames { get; set; } = [];

    public long[] MarkerFrames { get; set; } = [];

    public string[] MarkerComments { get; set; } = [];

    public long[] SelectedMarkerFrames { get; set; } = [];

    /// <summary>終了時に前面だったタブ。起動復元で ActiveDocumentIndex より優先する。</summary>
    public bool IsActive { get; set; }

    public string OriginFileName { get; set; } = string.Empty;

    public string HistoryFileName { get; set; } = string.Empty;

    /// <summary>未保存の録音。保存すると false。</summary>
    public bool CanContinueRecording { get; set; }

    /// <summary>Wave / Aiff / Mp3。空なら復元時に変えない。</summary>
    public string SourceKind { get; set; } = string.Empty;

    /// <summary>以前の版が閉じたタブの挿入位置を残した。今は使わない。</summary>
    public int ClosedIndex { get; set; }
}

internal enum SessionAudioWriteKind
{
    Skip,
    Reuse,
    Rewrite,
}

internal static class DocumentSessionStore
{
    public const string SessionDirectoryName = "session";

    public static string FileNameForIndex(int index) => $"doc-{Math.Max(0, index)}.wav";

    /// <summary>以前の版が書いた閉じたタブの作業コピー名。起動時に孤児として消す。</summary>
    public static string FileNameForClosedIndex(int index) => $"closed-{Math.Max(0, index)}.wav";

    public static string OriginFileNameForIndex(int index) => $"doc-{Math.Max(0, index)}-origin.wav";

    public static string OriginFileNameForClosedIndex(int index) => $"closed-{Math.Max(0, index)}-origin.wav";

    public static string HistoryFileNameForIndex(int index) => $"doc-{Math.Max(0, index)}-history.json";

    public static string HistoryFileNameForClosedIndex(int index) => $"closed-{Math.Max(0, index)}-history.json";

    public static string? SanitizeSidecarName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        if (name.EndsWith("-origin.wav", StringComparison.OrdinalIgnoreCase)
            && IsManagedSessionStem(name))
        {
            return name;
        }

        if (name.EndsWith("-history.json", StringComparison.OrdinalIgnoreCase)
            && IsManagedSessionStem(name))
        {
            return name;
        }

        return null;
    }

    public static bool NeedsSessionAudio(bool dirty, string? sourcePath, bool canContinueRecording = false) =>
        canContinueRecording || dirty || string.IsNullOrWhiteSpace(sourcePath);

    public static bool HasWorkingAudio(AudioDocument document) => document.FrameCount > 0;

    /// <summary>
    /// 作業コピー WAV を書くか。サンプルが前回と同じなら、既にあるコピーか元ファイルを使う。
    /// </summary>
    public static SessionAudioWriteKind DecideSessionAudioWrite(
        bool needsSessionAudio,
        bool hasWorkingAudio,
        bool hasSourcePath,
        int sampleRevision,
        int persistedSampleRevision,
        bool reusableFileExists)
    {
        if (!needsSessionAudio || !hasWorkingAudio)
        {
            return SessionAudioWriteKind.Skip;
        }

        if (sampleRevision == persistedSampleRevision)
        {
            if (reusableFileExists)
            {
                return SessionAudioWriteKind.Reuse;
            }

            if (hasSourcePath)
            {
                return SessionAudioWriteKind.Skip;
            }
        }

        return SessionAudioWriteKind.Rewrite;
    }

    public static bool HasUsableSessionAudio(string directory, string? fileName) =>
        SanitizeSessionFileName(fileName) is { } name && HasUsableFile(directory, name);

    public static bool HasUsableSidecar(string directory, string? fileName) =>
        SanitizeSidecarName(fileName) is { } name && HasUsableFile(directory, name);

    public static bool HistoryMatchesFile(string path, HistorySessionSnapshot snapshot)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var expected = JsonSerializer.Serialize(snapshot, HistorySessionJsonContext.Default.HistorySessionSnapshot);
            return string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasUsableFile(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            var path = Path.Combine(directory, fileName);
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 作業コピーか保存済み元ファイルで現状の PCM が戻せる。
    /// このときは履歴の再実行は波形にも Undo 復元にも使わない。
    /// </summary>
    public static bool HasCurrentAudio(string rootDirectory, OpenDocumentSnapshot snap)
    {
        var sessionDir = Path.Combine(rootDirectory, SessionDirectoryName);
        if (HasUsableSessionAudio(sessionDir, snap.SessionFileName))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(snap.SourcePath) || !File.Exists(snap.SourcePath))
        {
            return false;
        }

        if (!snap.Dirty)
        {
            return true;
        }

        if (!TryGetRestorableHistory(sessionDir, snap, out var history))
        {
            return true;
        }

        return !HistoryRecipes.AffectsSamples(history);
    }

    public static bool TryGetRestorableHistory(
        string sessionDirectory,
        OpenDocumentSnapshot snap,
        out HistorySessionSnapshot history)
    {
        history = null!;
        var originName = SanitizeSidecarName(snap.OriginFileName);
        var historyName = SanitizeSidecarName(snap.HistoryFileName);
        if (originName is null || historyName is null)
        {
            return false;
        }

        if (!HasUsableSidecar(sessionDirectory, originName)
            || !TryReadHistory(Path.Combine(sessionDirectory, historyName), out var loaded)
            || !HistoryRecipes.CanImport(loaded))
        {
            return false;
        }

        history = loaded;
        return true;
    }

    public static bool HasRestorableHistory(string sessionDirectory, OpenDocumentSnapshot snap) =>
        TryGetRestorableHistory(sessionDirectory, snap, out _);

    /// <summary>現状の PCM が無いときだけ、原点 WAV から履歴を再実行する。</summary>
    public static bool NeedsHistoryReplay(string rootDirectory, OpenDocumentSnapshot snap) =>
        !HasCurrentAudio(rootDirectory, snap)
        && HasRestorableHistory(Path.Combine(rootDirectory, SessionDirectoryName), snap);

    public static bool CanRestoreDocument(string rootDirectory, OpenDocumentSnapshot snap) =>
        HasCurrentAudio(rootDirectory, snap)
        || HasRestorableHistory(Path.Combine(rootDirectory, SessionDirectoryName), snap)
        || TryResolveLoadPath(rootDirectory, snap, out _, out _);

    /// <summary>次起動で現状の PCM を読めるなら origin / history は残さない（再実行しない）。</summary>
    public static bool ShouldPersistHistorySidecars(
        bool hasSessionAudio,
        bool dirty,
        string? sourcePath) =>
        !hasSessionAudio && (dirty || string.IsNullOrWhiteSpace(sourcePath));

    /// <summary>次に実際に開くファイルだけ残す。履歴再実行しない origin / history は捨てる。</summary>
    public static void CollectReferencedSessionFiles(
        string rootDirectory,
        OpenDocumentSnapshot snap,
        ICollection<string> keep)
    {
        if (NeedsHistoryReplay(rootDirectory, snap))
        {
            if (SanitizeSidecarName(snap.OriginFileName) is { } origin)
            {
                keep.Add(origin);
            }

            if (SanitizeSidecarName(snap.HistoryFileName) is { } history)
            {
                keep.Add(history);
            }

            return;
        }

        if (TryResolveLoadPath(rootDirectory, snap, out _, out var fromSession)
            && fromSession
            && SanitizeSessionFileName(snap.SessionFileName) is { } audio)
        {
            keep.Add(audio);
        }
    }

    public static void CollectReferencedSessionFiles(
        string rootDirectory,
        IEnumerable<OpenDocumentSnapshot> open,
        ICollection<string> keep)
    {
        foreach (var snap in open)
        {
            if (CanRestoreDocument(rootDirectory, snap))
            {
                CollectReferencedSessionFiles(rootDirectory, snap, keep);
            }
        }
    }

    /// <summary>次起動で読まないサイドカー名をスナップから外す。消したら true。</summary>
    public static bool DropUnreferencedSidecarNames(string rootDirectory, OpenDocumentSnapshot snap)
    {
        var changed = false;
        if (NeedsHistoryReplay(rootDirectory, snap))
        {
            if (snap.SessionFileName.Length > 0)
            {
                snap.SessionFileName = string.Empty;
                changed = true;
            }

            return changed;
        }

        if (snap.OriginFileName.Length > 0 || snap.HistoryFileName.Length > 0)
        {
            snap.OriginFileName = string.Empty;
            snap.HistoryFileName = string.Empty;
            changed = true;
        }

        var keepSession = TryResolveLoadPath(rootDirectory, snap, out _, out var fromSession) && fromSession;
        if (!keepSession && snap.SessionFileName.Length > 0)
        {
            snap.SessionFileName = string.Empty;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 設定が指していても、次起動で開かないセッションファイルを消す。
    /// 閉じたタブは世代を越えないので、ClosedDocuments とそれらの作業コピーも捨てる。
    /// </summary>
    public static bool PruneUnreferencedSessionState(
        string rootDirectory,
        AppSettings settings,
        string? leftoverSessionDocumentPath = null)
    {
        var open = settings.OpenDocuments ?? [];
        var keep = new List<string>();
        CollectReferencedSessionFiles(rootDirectory, open, keep);

        var sessionDir = Path.Combine(rootDirectory, SessionDirectoryName);
        RemoveOrphanSessionFiles(sessionDir, keep);
        TryDeleteEmptyDirectory(sessionDir);
        TryDeleteFile(leftoverSessionDocumentPath);

        var changed = false;
        foreach (var snap in open)
        {
            if (DropUnreferencedSidecarNames(rootDirectory, snap))
            {
                changed = true;
            }
        }

        if ((settings.ClosedDocuments ?? []).Length > 0)
        {
            settings.ClosedDocuments = [];
            changed = true;
        }

        return changed;
    }

    public static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
        catch
        {
            // 空フォルダの削除失敗は残ファイルより軽い。
        }
    }

    public static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 参照されない作業コピーの削除失敗は致命的ではない。
        }
    }

    private static bool IsManagedSessionStem(string name) =>
        name.StartsWith("doc-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("closed-", StringComparison.OrdinalIgnoreCase);

    public static bool TryWriteHistory(string path, HistorySessionSnapshot snapshot)
    {
        try
        {
            var json = JsonSerializer.Serialize(snapshot, HistorySessionJsonContext.Default.HistorySessionSnapshot);
            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadHistory(string path, out HistorySessionSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var loaded = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                HistorySessionJsonContext.Default.HistorySessionSnapshot);
            if (loaded?.Recipes is not { Length: > 0 })
            {
                return false;
            }

            snapshot = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? SanitizeSessionFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return name;
    }

    public static string ResolveSessionAudioPath(string rootDirectory, OpenDocumentSnapshot snap)
    {
        var name = SanitizeSessionFileName(snap.SessionFileName);
        if (name is null)
        {
            return string.Empty;
        }

        return Path.Combine(rootDirectory, SessionDirectoryName, name);
    }

    public static bool TryResolveLoadPath(
        string rootDirectory,
        OpenDocumentSnapshot snap,
        out string path,
        out bool fromSession)
    {
        var sessionPath = ResolveSessionAudioPath(rootDirectory, snap);
        if (snap.Dirty && File.Exists(sessionPath))
        {
            path = sessionPath;
            fromSession = true;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(snap.SourcePath) && File.Exists(snap.SourcePath))
        {
            path = snap.SourcePath;
            fromSession = false;
            return true;
        }

        if (File.Exists(sessionPath))
        {
            path = sessionPath;
            fromSession = true;
            return true;
        }

        path = string.Empty;
        fromSession = false;
        return false;
    }

    public static OpenDocumentSnapshot Capture(AudioDocument document, in SessionViewState view, int index)
    {
        var needsAudio = NeedsSessionAudio(document.IsDirty, document.SourcePath, document.CanContinueRecording)
            && HasWorkingAudio(document);
        var snap = new OpenDocumentSnapshot
        {
            SourcePath = document.SourcePath ?? string.Empty,
            Dirty = document.IsDirty,
            SessionFileName = needsAudio ? FileNameForIndex(index) : string.Empty,
            CursorFrame = view.PlayheadFrame,
            SelectionStart = document.Selection.StartFrame,
            SelectionEnd = document.Selection.EndFrame,
            TimeZoom = view.TimeZoom,
            AmpZoom = view.AmpZoom,
            ViewStart = view.ViewStart,
            LoopEnabled = view.LoopEnabled,
            SampleLoopStart = document.SampleLoop.StartFrame,
            SampleLoopEnd = document.SampleLoop.EndFrame,
            SelectedMarkerFrames = view.SelectedMarkerFrames.Count == 0
                ? []
                : [.. view.SelectedMarkerFrames],
            CanContinueRecording = document.CanContinueRecording,
            SourceKind = document.SourceKind == AudioFileKind.Wave
                ? string.Empty
                : document.SourceKind.ToString(),
        };
        StoreMarkers(snap, document.SnapshotMarkers());
        StoreRegions(snap, document.SnapshotRegions());
        return snap;
    }

    public static bool TryParseSourceKind(string? text, out AudioFileKind kind)
    {
        if (string.Equals(text, nameof(AudioFileKind.Mp3), StringComparison.OrdinalIgnoreCase))
        {
            kind = AudioFileKind.Mp3;
            return true;
        }

        if (string.Equals(text, nameof(AudioFileKind.Aiff), StringComparison.OrdinalIgnoreCase))
        {
            kind = AudioFileKind.Aiff;
            return true;
        }

        if (string.Equals(text, nameof(AudioFileKind.Wave), StringComparison.OrdinalIgnoreCase))
        {
            kind = AudioFileKind.Wave;
            return true;
        }

        kind = AudioFileKind.Wave;
        return false;
    }

    public static void ApplyMeta(AudioDocument document, OpenDocumentSnapshot snap)
    {
        if (TryParseSourceKind(snap.SourceKind, out var kind))
        {
            document.RestoreSourceKind(kind);
        }

        document.ReplaceMarkers(LoadMarkers(snap), markDirty: false);
        document.SetSampleLoop(new WaveSelection(snap.SampleLoopStart, snap.SampleLoopEnd), markDirty: false);
        document.SetRegions(LoadRegions(snap), markDirty: false);
        // 起動復元ではズーム・選択・再生ヘッドは初期化する（タブ切り替え中の表示はセッション側で持つ）。
        document.Selection = WaveSelection.Empty;
        document.CursorFrame = 0;
        document.CanContinueRecording = snap.CanContinueRecording
            || (snap.Dirty && string.IsNullOrWhiteSpace(snap.SourcePath));
    }

    public static int ResolveActiveIndex(IReadOnlyList<OpenDocumentSnapshot> docs, int savedIndex)
    {
        if (docs.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < docs.Count; i++)
        {
            if (docs[i].IsActive)
            {
                return i;
            }
        }

        return Math.Clamp(savedIndex, 0, docs.Count - 1);
    }

    public static T? PickRestoredActive<T>(
        IReadOnlyList<(int SourceIndex, T Item)> restored,
        int activeSourceIndex)
        where T : class
    {
        foreach (var (sourceIndex, item) in restored)
        {
            if (sourceIndex == activeSourceIndex)
            {
                return item;
            }
        }

        return restored.Count > 0 ? restored[0].Item : null;
    }

    /// <summary>復元タブを元の並び（SourceIndex）へ差し込む。</summary>
    public static int InsertRestoredBySourceIndex<T>(
        IList<(int SourceIndex, T Item)> restored,
        IList<T> sessions,
        int sourceIndex,
        T item)
    {
        var insertAt = 0;
        while (insertAt < restored.Count && restored[insertAt].SourceIndex < sourceIndex)
        {
            insertAt++;
        }

        restored.Insert(insertAt, (sourceIndex, item));
        sessions.Insert(insertAt, item);
        return insertAt;
    }

    public static OpenDocumentSnapshot[] ResolveOpenDocuments(AppSettings settings) =>
        settings.OpenDocuments ?? [];

    public static void ClearOpenDocuments(AppSettings settings)
    {
        settings.OpenDocuments = [];
        settings.ClosedDocuments = [];
        settings.ActiveDocumentIndex = 0;
        settings.WaveformTileArrange = string.Empty;
    }

    public static void RemoveOrphanSessionFiles(string sessionDirectory, IReadOnlyCollection<string> keepFileNames)
    {
        if (!Directory.Exists(sessionDirectory))
        {
            return;
        }

        var keep = new HashSet<string>(keepFileNames, StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(sessionDirectory))
        {
            if (keep.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch
            {
                // 作業コピーの削除失敗は致命的ではない。
            }
        }

        TryDeleteEmptyDirectory(sessionDirectory);
    }

    private static void StoreMarkers(OpenDocumentSnapshot snap, IReadOnlyList<MarkerSnapshot> markers)
    {
        if (markers.Count == 0)
        {
            snap.MarkerFrames = [];
            snap.MarkerComments = [];
            return;
        }

        var frames = new long[markers.Count];
        var comments = new string[markers.Count];
        for (var i = 0; i < markers.Count; i++)
        {
            frames[i] = markers[i].Frame;
            comments[i] = markers[i].Comment ?? string.Empty;
        }

        snap.MarkerFrames = frames;
        snap.MarkerComments = comments;
    }

    private static MarkerSnapshot[] LoadMarkers(OpenDocumentSnapshot snap)
    {
        var frames = snap.MarkerFrames ?? [];
        var comments = snap.MarkerComments ?? [];
        var markers = new MarkerSnapshot[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            markers[i] = new MarkerSnapshot(frames[i], i < comments.Length ? comments[i] : string.Empty);
        }

        return markers;
    }

    private static void StoreRegions(OpenDocumentSnapshot snap, IReadOnlyList<WaveRegion> regions)
    {
        if (regions.Count == 0)
        {
            snap.RegionStarts = [];
            snap.RegionEnds = [];
            snap.RegionNames = [];
            return;
        }

        var starts = new long[regions.Count];
        var ends = new long[regions.Count];
        var names = new string[regions.Count];
        for (var i = 0; i < regions.Count; i++)
        {
            starts[i] = regions[i].StartFrame;
            ends[i] = regions[i].EndFrame;
            names[i] = regions[i].Name;
        }

        snap.RegionStarts = starts;
        snap.RegionEnds = ends;
        snap.RegionNames = names;
    }

    private static WaveRegion[] LoadRegions(OpenDocumentSnapshot snap)
    {
        var starts = snap.RegionStarts ?? [];
        var ends = snap.RegionEnds ?? [];
        var names = snap.RegionNames ?? [];
        if (starts.Length == 0 || starts.Length != ends.Length)
        {
            return [];
        }

        var regions = new WaveRegion[starts.Length];
        for (var i = 0; i < starts.Length; i++)
        {
            var name = i < names.Length ? names[i] : string.Empty;
            regions[i] = new WaveRegion(new WaveSelection(starts[i], ends[i]), name);
        }

        return regions;
    }
}
