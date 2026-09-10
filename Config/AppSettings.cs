using System.Text.Json.Serialization;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.Config;

internal sealed class AppSettings
{
    public string AudioApi { get; set; } = "WaveOut";

    public string AudioDeviceId { get; set; } = string.Empty;

    public string RecordLayout { get; set; } = "Stereo";

    /// <summary>空なら RecordLayout を使う（以前は入出力で兼用していた）。</summary>
    public string PlaybackLayout { get; set; } = string.Empty;

    public string RecordDeviceId { get; set; } = string.Empty;

    public int[] RecordInputMap { get; set; } = [];

    public int[] PlaybackOutputMap { get; set; } = [];

    public string DefaultFadeInCurve { get; set; } = nameof(FadeShape.SCurve);

    public string DefaultFadeOutCurve { get; set; } = nameof(FadeShape.SCurve);

    public int WaveformHeightScale { get; set; } = 1;

    public int Mp3BitRate { get; set; } = Mp3Encode.DefaultWindowsBitRateKbps;

    /// <summary>ユーザー用意の lame.exe。空または無効なら Windows で MP3 出力。</summary>
    public string LameExePath { get; set; } = string.Empty;

    /// <summary>lame に渡すオプション。入出力パスは含めない。空欄は既定（-V2）。</summary>
    public string LameOptions { get; set; } = Mp3Encode.DefaultLameOptions;

    /// <summary>全タブ同時書き出し数。0 は Auto（コア数の 1/4）。上限はコア数の 1/2。</summary>
    public int ExportParallelism { get; set; }

    /// <summary>タブ書き出しで最後に選んだフォルダ。</summary>
    public string LastExportFolder { get; set; } = string.Empty;

    /// <summary>ラウドネスメーターのターゲット（LKFS）。既定 -24。</summary>
    public double LoudnessTargetLufs { get; set; } = LoudnessMeterEngine.DefaultTargetLufs;

    public bool AlwaysOnTop { get; set; }

    public int WindowX { get; set; }

    public int WindowY { get; set; }

    public int WindowWidth { get; set; }

    public int WindowHeight { get; set; }

    /// <summary>Normal / Maximized。空または不明なら通常表示。</summary>
    public string WindowState { get; set; } = string.Empty;

    /// <summary>設定ウィンドウの位置。未保存なら false。</summary>
    public bool SettingsWindowHasPosition { get; set; }

    public int SettingsWindowX { get; set; }

    public int SettingsWindowY { get; set; }

    /// <summary>閉じたときの幅。開くときは使わず、内容から自動で決める。</summary>
    public int SettingsWindowWidth { get; set; }

    /// <summary>閉じたときの高さ。次回復元する。</summary>
    public int SettingsWindowHeight { get; set; }

    public string UiLanguage { get; set; } = "auto";

    /// <summary>Tips 枠の表示。既定オン。</summary>
    public bool ShowTips { get; set; } = true;

    /// <summary>ステータスバーの時間をサンプル数で表示。</summary>
    public bool StatusShowSamples { get; set; }

    /// <summary>「今は開かない」にしたリモート版。同じ版では再通知しない。</summary>
    public string SkippedUpdateVersion { get; set; } = string.Empty;

    public string LastDocumentPath { get; set; } = string.Empty;

    public bool LastDocumentDirty { get; set; }

    /// <summary>終了時に開いていたタブ。無ければ LastDocument* から 1 本だけ戻す。</summary>
    public OpenDocumentSnapshot[] OpenDocuments { get; set; } = [];

    public int ActiveDocumentIndex { get; set; }

    public long LastCursorFrame { get; set; }

    public long LastSelectionStart { get; set; }

    public long LastSelectionEnd { get; set; }

    public double LastTimeZoom { get; set; } = 1;

    public double LastAmpZoom { get; set; } = 1;

    public double LastViewStart { get; set; }

    public bool LastLoop { get; set; }

    public long LastSampleLoopStart { get; set; }

    public long LastSampleLoopEnd { get; set; }

    public long LastRegionStart { get; set; }

    public long LastRegionEnd { get; set; }

    public long[] LastRegionStarts { get; set; } = [];

    public long[] LastRegionEnds { get; set; } = [];

    public string[] LastRegionNames { get; set; } = [];

    public long[] LastMarkerFrames { get; set; } = [];

    public string[] LastMarkerComments { get; set; } = [];

    public bool WaapiKeepTarget { get; set; }

    public string WaapiKeptTargetPath { get; set; } = string.Empty;

    public string WaapiKeptTargetProjectFilePath { get; set; } = string.Empty;

    public bool WaapiAutoActive { get; set; } = true;

    /// <summary>Play -E（Wwise Play post-exit）。既定オフ。</summary>
    public bool WaapiPlayPostExit { get; set; }

    /// <summary>WAAPI エリアの表示。既定オン。</summary>
    public bool WaapiPanelVisible { get; set; } = true;

    public string WaapiLastProjectName { get; set; } = string.Empty;

    public string WaapiLastProjectFilePath { get; set; } = string.Empty;

    public string WaapiOutputDirectory { get; set; } = string.Empty;

    /// <summary>色調整パネルで保存したアプリ既定色。#RRGGBB。未設定なら XAML 既定。</summary>
    public Dictionary<string, string>? Colors { get; set; }

    public AudioOutputSettings ToAudioOutputSettings() =>
        new(AudioOutputSettings.ParseApi(AudioApi), AudioDeviceId ?? string.Empty);

    public void ApplyAudioOutput(AudioOutputSettings settings)
    {
        AudioApi = AudioOutputSettings.ToStoredValue(settings.Api);
        AudioDeviceId = settings.DeviceId ?? string.Empty;
    }

    public double ResolvedLoudnessTargetLufs() =>
        LoudnessMeterEngine.ClampTargetLufs(LoudnessTargetLufs);

    public ChannelLayout ResolvedPlaybackLayout() =>
        ChannelLayout.Parse(string.IsNullOrWhiteSpace(PlaybackLayout) ? RecordLayout : PlaybackLayout);

    public string ResolvedRecordDeviceId() =>
        AudioCaptureFactory.ResolveRecordDeviceId(ToAudioOutputSettings().Api, AudioDeviceId);

    public FadeShape ResolvedFadeInCurve() => FadeCurves.ParseStored(DefaultFadeInCurve);

    public FadeShape ResolvedFadeOutCurve() => FadeCurves.ParseStored(DefaultFadeOutCurve);

    public void ApplyDefaultFades(FadeShape fadeIn, FadeShape fadeOut)
    {
        DefaultFadeInCurve = fadeIn.ToString();
        DefaultFadeOutCurve = fadeOut.ToString();
    }

    public Mp3EncodeOptions ToMp3EncodeOptions() =>
        new(Mp3BitRate, LameExePath ?? string.Empty, Mp3Encode.ResolveLameOptions(LameOptions));
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(OpenDocumentSnapshot))]
[JsonSerializable(typeof(OpenDocumentSnapshot[]))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
