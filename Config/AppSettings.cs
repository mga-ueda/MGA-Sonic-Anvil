using System.Text.Json.Serialization;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using MgaSonicAnvil.Wwise;

namespace MgaSonicAnvil.Config;

internal sealed class AppSettings
{
    /// <summary>現行の設定ファイル世代。一致しないファイルは破棄して作り直す。</summary>
    public const int CurrentGeneration = 1;

    /// <summary>このファイルの世代。欠けている／古い値はレガシーとみなす。</summary>
    public int SettingsGeneration { get; set; }

    public string AudioApi { get; set; } = "WaveOut";

    public string AudioDeviceId { get; set; } = string.Empty;

    /// <summary>アクティブなスピーカー定義の写し。</summary>
    public string RecordLayout { get; set; } = "Stereo";

    /// <summary>アクティブなスピーカー定義の写し。</summary>
    public string PlaybackLayout { get; set; } = string.Empty;

    public string RecordDeviceId { get; set; } = string.Empty;

    public int[] RecordInputMap { get; set; } = [];

    public int[] PlaybackOutputMap { get; set; } = [];

    public int[] FileChannelMap { get; set; } = [];

    public SpeakerPreset[] SpeakerPresets { get; set; } = [];

    public string ActiveSpeakerPresetId { get; set; } = string.Empty;

    /// <summary>有効にするスピーカー定義。空は Stereo のみ。</summary>
    public string[] VisibleSpeakerPresetIds { get; set; } = [];

    /// <summary>アクティブファイルの本数に合わせてスピーカー定義を選ぶ。既定オフ。</summary>
    public bool AutoSpeakerSelect { get; set; }

    /// <summary>新規録音などの既定サンプリングレート。既定 48000。</summary>
    public int DefaultSampleRate { get; set; } = DefaultAudioFormat.SampleRate;

    /// <summary>新規録音などの既定ビット深度。既定 24。</summary>
    public int DefaultBitsPerSample { get; set; } = DefaultAudioFormat.BitsPerSample;

    /// <summary>新規ファイルなどの既定チャンネル配置。既定 Stereo。</summary>
    public string DefaultChannelLayout { get; set; } = DefaultAudioFormat.ChannelLayoutId;

    /// <summary>新規ダイアログで前回選んだレート。0 は既定フォーマット。</summary>
    public int LastNewSampleRate { get; set; }

    /// <summary>新規ダイアログで前回選んだビット深度。0 は既定フォーマット。</summary>
    public int LastNewBitsPerSample { get; set; }

    /// <summary>新規ダイアログで前回選んだ配置。空は既定フォーマット。</summary>
    public string LastNewChannelLayout { get; set; } = string.Empty;

    public string DefaultFadeInCurve { get; set; } = nameof(FadeShape.SCurve);

    public string DefaultFadeOutCurve { get; set; } = nameof(FadeShape.SCurve);

    public int WaveformHeightScale { get; set; } = 1;

    /// <summary>レベルメーター／ゴニオ列の幅。0 以下は既定（最小幅）。</summary>
    public double MeterColumnWidth { get; set; }

    public int Mp3BitRate { get; set; } = Mp3Encode.DefaultWindowsBitRateKbps;

    /// <summary>ユーザー用意の lame.exe。空または無効なら Windows で MP3 出力。</summary>
    public string LameExePath { get; set; } = string.Empty;

    /// <summary>lame に渡すオプション。入出力パスは含めない。空欄は既定（-V2）。</summary>
    public string LameOptions { get; set; } = Mp3Encode.DefaultLameOptions;

    /// <summary>全タブ同時書き出し数。0 は Auto（コア数の 1/4）。上限はコア数の 1/2。</summary>
    public int ExportParallelism { get; set; }

    /// <summary>「開く」で最後に選んだフォルダ。書き出し先とは別。</summary>
    public string LastOpenFolder { get; set; } = string.Empty;

    /// <summary>書き出し（タブ / MP3 として保存）で最後に選んだフォルダ。開くとは別。</summary>
    public string LastExportFolder { get; set; } = string.Empty;

    /// <summary>ラウドネスメーターのターゲット（LKFS）。既定 -24。</summary>
    public double LoudnessTargetLufs { get; set; } = LoudnessMeterEngine.DefaultTargetLufs;

    public bool AlwaysOnTop { get; set; }

    /// <summary>再生で無音区間を飛ばす。既定オフ。</summary>
    public bool SilentSkip { get; set; }

    /// <summary>Silent Skip と無音削除の無音しきい値（dBFS）。既定 -60。</summary>
    public double SilentSkipThresholdDb { get; set; } = global::MgaSonicAnvil.Audio.SilentSkip.DefaultThresholdDb;

    /// <summary>録音 Silent Skip で、しきい値を下回った時点から挿入する無音の時間（ms）。再生には使わない。既定 500。</summary>
    public int SilentSkipRecordPadMs { get; set; } = global::MgaSonicAnvil.Audio.SilentSkip.DefaultRecordPadMs;

    /// <summary>Silent Skip 録音の停止時、pad を挟んだ可聴／無音にリージョンを付ける。既定オフ。</summary>
    public bool SilentSkipRecordAddRegion { get; set; }

    /// <summary>継ぎ目のプチノイズ防止フェード（ms）。既定 20。</summary>
    public int ClickGuardFadeMs { get; set; } = global::MgaSonicAnvil.Audio.ClickGuard.DefaultFadeMilliseconds;

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

    /// <summary>色設定ウィンドウの位置。未保存なら false（初回はアプリ中央）。</summary>
    public bool ColorPanelHasPosition { get; set; }

    public int ColorPanelX { get; set; }

    public int ColorPanelY { get; set; }

    public int ColorPanelWidth { get; set; }

    public int ColorPanelHeight { get; set; }

    public string UiLanguage { get; set; } = "auto";

    /// <summary>配色。auto / dark / light。既定は OS に従う。</summary>
    public string UiTheme { get; set; } = "auto";

    /// <summary>アプリ表示の追加拡大（％）。100 未満は 100。上限 200。</summary>
    public int UiScalePercent { get; set; } = UiScale.DefaultPercent;

    /// <summary>Tips 枠の表示。既定オン。</summary>
    public bool ShowTips { get; set; } = true;

    /// <summary>ステータスバーの時間をサンプル数で表示。</summary>
    public bool StatusShowSamples { get; set; }

    /// <summary>「今は開かない」にしたリモート版。同じ版では再通知しない。</summary>
    public string SkippedUpdateVersion { get; set; } = string.Empty;

    /// <summary>最後に開いた／保存したファイル。開くダイアログの予備フォルダに使う。</summary>
    public string LastDocumentPath { get; set; } = string.Empty;

    /// <summary>終了時に開いていたタブ。</summary>
    public OpenDocumentSnapshot[] OpenDocuments { get; set; } = [];

    /// <summary>以前の版が残した閉じたタブ。起動時に空にする。Ctrl+Shift+T は今の起動のメモリだけ。</summary>
    public OpenDocumentSnapshot[] ClosedDocuments { get; set; } = [];

    public int ActiveDocumentIndex { get; set; }

    /// <summary>終了時のタイル表示。off / vertical / horizontal / grid。タブが 2 未満なら起動時は無視。</summary>
    public string WaveformTileArrange { get; set; } = string.Empty;

    /// <summary>
    /// 複数ファイルを開いたあとの並べ方。空／off はタブのまま（手動タイル）。
    /// vertical / horizontal / grid で読み込み後にタイルする。
    /// </summary>
    public string MultiFileArrange { get; set; } = string.Empty;

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

    /// <summary>Wwise EXPORT の Prefetch Length（ms）。先頭 Segment（Zero Latency）に書く。既定 500。</summary>
    public int WwisePrefetchLengthMs { get; set; } = WwiseTrackTiming.DefaultPrefetchLengthMs;

    /// <summary>Wwise EXPORT の Look-ahead Time（ms）。2 本目以降に書く。既定 500。</summary>
    public int WwiseLookAheadTimeMs { get; set; } = WwiseTrackTiming.DefaultLookAheadTimeMs;

    /// <summary>旧版の一括色。テーマ別へ移したあと使わない。</summary>
    public Dictionary<string, string>? Colors { get; set; }

    /// <summary>ライト配色の上書き。#RRGGBB。無いキーは既定。</summary>
    public Dictionary<string, string>? ColorsLight { get; set; }

    /// <summary>ダーク配色の上書き。#RRGGBB。無いキーは既定。</summary>
    public Dictionary<string, string>? ColorsDark { get; set; }

    public AudioOutputSettings ToAudioOutputSettings() =>
        ResolvedSpeaker().ToAudioOutputSettings();

    public void ApplyAudioOutput(AudioOutputSettings settings)
    {
        var speaker = ResolvedSpeaker();
        speaker.ApplyAudioOutput(settings);
        MirrorActiveSpeaker();
    }

    public double ResolvedLoudnessTargetLufs() =>
        LoudnessMeterEngine.ClampTargetLufs(LoudnessTargetLufs);

    public double ResolvedSilentSkipThresholdDb() =>
        global::MgaSonicAnvil.Audio.SilentSkip.ClampThresholdDb(SilentSkipThresholdDb);

    public int ResolvedSilentSkipRecordPadMs() =>
        global::MgaSonicAnvil.Audio.SilentSkip.ClampRecordPadMs(SilentSkipRecordPadMs);

    public int ResolvedClickGuardFadeMs() =>
        global::MgaSonicAnvil.Audio.ClickGuard.ClampFadeMs(ClickGuardFadeMs);

    public int ResolvedWwisePrefetchLengthMs() =>
        WwiseTrackTiming.ClampPrefetchLengthMs(WwisePrefetchLengthMs);

    public int ResolvedWwiseLookAheadTimeMs() =>
        WwiseTrackTiming.ClampLookAheadTimeMs(WwiseLookAheadTimeMs);

    public SpeakerPreset ResolvedSpeaker()
    {
        EnsureSpeakerPresets();
        return FindSpeaker(ActiveSpeakerPresetId) ?? SpeakerPresets[0];
    }

    public string[] ResolvedVisibleSpeakerIds() =>
        SpeakerPreset.NormalizeVisibleIds(VisibleSpeakerPresetIds);

    public void ApplyVisibleSpeakerIds(IEnumerable<string> ids) =>
        VisibleSpeakerPresetIds = SpeakerPreset.NormalizeVisibleIds(ids);

    public SpeakerPreset[] MenuSpeakers()
    {
        EnsureSpeakerPresets();
        return SpeakerPreset.FilterMenu(SpeakerPresets, ResolvedVisibleSpeakerIds(), ActiveSpeakerPresetId);
    }

    public ChannelLayout ResolvedRecordLayout() =>
        ChannelLayout.Parse(ResolvedSpeaker().Id);

    public ChannelLayout ResolvedPlaybackLayout() =>
        ChannelLayout.Parse(ResolvedSpeaker().Id);

    public int[] ResolvedRecordInputMap() => ResolvedSpeaker().RecordInputMap ?? [];

    public int[] ResolvedPlaybackOutputMap() => ResolvedSpeaker().PlaybackOutputMap ?? [];

    public int[] ResolvedFileChannelMap() => ResolvedSpeaker().FileChannelMap ?? [];

    public string ResolvedRecordDeviceId()
    {
        var output = ToAudioOutputSettings();
        return AudioCaptureFactory.ResolveRecordDeviceId(output.Api, output.DeviceId);
    }

    public SpeakerPreset? FindSpeaker(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || SpeakerPresets is not { Length: > 0 })
        {
            return null;
        }

        foreach (var preset in SpeakerPresets)
        {
            if (preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }

    public void ReplaceSpeakerPresets(IEnumerable<SpeakerPreset> presets, string? activeId)
    {
        var saved = presets as SpeakerPreset[] ?? presets.ToArray();
        ActiveSpeakerPresetId = activeId ?? string.Empty;
        SpeakerPresets = SpeakerPreset.MergeCatalog(saved);
        ActiveSpeakerPresetId = SpeakerPreset.ResolveActiveId(SpeakerPresets, ActiveSpeakerPresetId, saved);
        MirrorActiveSpeaker();
    }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings { SettingsGeneration = CurrentGeneration };
        settings.EnsureSpeakerPresets();
        return settings;
    }

    /// <summary>カタログを正本にし、保存済みのデバイスとマップを載せる。</summary>
    public void EnsureSpeakerPresets()
    {
        var saved = SpeakerPresets is { Length: > 0 } ? SpeakerPresets : SpeakerPreset.CreateCatalog();
        SpeakerPresets = SpeakerPreset.MergeCatalog(saved);
        if (FindSpeaker(ActiveSpeakerPresetId) is null)
        {
            ActiveSpeakerPresetId = SpeakerPreset.ResolveActiveId(
                SpeakerPresets,
                ActiveSpeakerPresetId,
                saved);
        }

        if (FindSpeaker(ActiveSpeakerPresetId) is null)
        {
            ActiveSpeakerPresetId = SpeakerPreset.DefaultId;
        }

        MirrorActiveSpeaker();
    }

    private void MirrorActiveSpeaker()
    {
        var speaker = FindSpeaker(ActiveSpeakerPresetId) ?? (SpeakerPresets is { Length: > 0 } ? SpeakerPresets[0] : null);
        if (speaker is null)
        {
            return;
        }

        AudioApi = speaker.AudioApi ?? "WaveOut";
        AudioDeviceId = speaker.AudioDeviceId ?? string.Empty;
        RecordInputMap = [.. speaker.RecordInputMap ?? []];
        PlaybackOutputMap = [.. speaker.PlaybackOutputMap ?? []];
        FileChannelMap = [.. speaker.FileChannelMap ?? []];
        RecordLayout = speaker.Id;
        PlaybackLayout = speaker.Id;
    }

    public int ResolvedDefaultSampleRate() => DefaultAudioFormat.ClampSampleRate(DefaultSampleRate);

    public int ResolvedDefaultBitsPerSample() => DefaultAudioFormat.ClampBitDepth(DefaultBitsPerSample);

    public ChannelLayout ResolvedDefaultChannelLayout() =>
        DefaultAudioFormat.ClampPickerLayout(DefaultChannelLayout, ResolvedVisibleSpeakerIds());

    public DefaultAudioFormat.Spec ResolvedDefaultAudioFormat() =>
        DefaultAudioFormat.Resolve(
            DefaultSampleRate,
            DefaultBitsPerSample,
            DefaultChannelLayout,
            ResolvedVisibleSpeakerIds());

    public DefaultAudioFormat.Spec ResolvedLastNewAudioFormat() =>
        DefaultAudioFormat.Resolve(
            LastNewSampleRate == 0 ? DefaultSampleRate : LastNewSampleRate,
            LastNewBitsPerSample == 0 ? DefaultBitsPerSample : LastNewBitsPerSample,
            string.IsNullOrWhiteSpace(LastNewChannelLayout) ? DefaultChannelLayout : LastNewChannelLayout,
            ResolvedVisibleSpeakerIds());

    public void RememberNewAudioFormat(DefaultAudioFormat.Spec format)
    {
        LastNewSampleRate = format.SampleRate;
        LastNewBitsPerSample = format.BitsPerSample;
        LastNewChannelLayout = format.Layout.Id;
    }

    public int ResolvedUiScalePercent() => UiScale.ClampPercent(UiScalePercent);

    public double ResolvedUiScale() => UiScale.FactorFrom(UiScalePercent);

    public FadeShape ResolvedFadeInCurve() => FadeCurves.ParseStored(DefaultFadeInCurve);

    public FadeShape ResolvedFadeOutCurve() => FadeCurves.ParseStored(DefaultFadeOutCurve);

    public void ApplyDefaultFades(FadeShape fadeIn, FadeShape fadeOut)
    {
        DefaultFadeInCurve = fadeIn.ToString();
        DefaultFadeOutCurve = fadeOut.ToString();
    }

    public Mp3EncodeOptions ToMp3EncodeOptions() =>
        new(Mp3BitRate, LameExePath ?? string.Empty, Mp3Encode.ResolveLameOptions(LameOptions));

    public Mp3SpeakerMix ToMp3SpeakerMix() =>
        new(ResolvedSpeaker().Channels, ResolvedFileChannelMap());
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SpeakerPreset))]
[JsonSerializable(typeof(SpeakerPreset[]))]
[JsonSerializable(typeof(OpenDocumentSnapshot))]
[JsonSerializable(typeof(OpenDocumentSnapshot[]))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
