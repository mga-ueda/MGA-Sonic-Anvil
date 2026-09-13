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

    /// <summary>閉じたタブを再開するときの挿入位置。</summary>
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

    /// <summary>終了時に作業コピーを残す未保存の録音（閉じたタブ含む）。</summary>
    public static bool ShouldPersistClosedTab(AudioDocument document) =>
        HasWorkingAudio(document)
        && (document.CanContinueRecording || string.IsNullOrWhiteSpace(document.SourcePath));

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

    public static OpenDocumentSnapshot[] ResolveOpenDocuments(AppSettings settings) =>
        settings.OpenDocuments ?? [];

    public static OpenDocumentSnapshot[] ResolveClosedDocuments(AppSettings settings) =>
        settings.ClosedDocuments ?? [];

    public static void ClearOpenDocuments(AppSettings settings)
    {
        settings.OpenDocuments = [];
        settings.ClosedDocuments = [];
        settings.ActiveDocumentIndex = 0;
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
