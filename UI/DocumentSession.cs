using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal sealed class DocumentSession
{
    public DocumentSession(AudioDocument document)
    {
        Document = document;
        PlayheadFrame = document.CursorFrame;
    }

    public AudioDocument Document { get; private set; }

    public void ReplaceDocument(AudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        History = new();
        TimeZoom = 1;
        AmpZoom = 1;
        ViewStart = 0;
        PlayheadFrame = document.CursorFrame;
        SelectedMarkerFrames.Clear();
        AnalysisView = null;
        SoloMask = 0;
        LoopEnabled = true;
        PersistedSessionAudioName = null;
        PersistedSampleRevision = 0;
        PersistedOriginName = null;
        PersistedHistoryName = null;
    }

    public EditHistory History { get; set; } = new();

    public double TimeZoom { get; set; } = 1;

    public double AmpZoom { get; set; } = 1;

    public double ViewStart { get; set; }

    public long PlayheadFrame { get; set; }

    public List<long> SelectedMarkerFrames { get; } = [];

    public bool LoopEnabled { get; set; } = true;

    /// <summary>
    /// 波形 / スペクトログラム / 重ね / ラウドネス。未設定ならタイル開始時に今の表示を引き継ぐ。
    /// </summary>
    public WaveformAnalysisView? AnalysisView { get; set; }

    /// <summary>波形レーンのソロ。0 は解除。bit i がそのレーン。</summary>
    public int SoloMask { get; set; }

    /// <summary>前回書いた／読み込んだ作業コピー。サンプルが同じなら書き直さない。</summary>
    public string? PersistedSessionAudioName { get; set; }

    public int PersistedSampleRevision { get; set; }

    public string? PersistedOriginName { get; set; }

    public string? PersistedHistoryName { get; set; }

    public string DisplayName =>
        Document.SourcePath is { } path
            ? System.IO.Path.GetFileName(path)
            : UiStrings.UntitledDocument;

    public string TabTitle => Document.IsDirty ? "* " + DisplayName : DisplayName;
}
