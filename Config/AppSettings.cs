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

    /// <summary>保存／書き出しで最後に選んだフォルダ。開くとは別。</summary>
    public string LastExportFolder { get; set; } = string.Empty;

    /// <summary>ラウドネスメーターのターゲット（LKFS）。既定 -24。</summary>
    public double LoudnessTargetLufs { get; set; } = LoudnessMeterEngine.DefaultTargetLufs;

    public bool AlwaysOnTop { get; set; }

    /// <summary>再生で MP3／M4A の encoder delay／padding を飛ばす。既定オン。</summary>
    public bool GaplessPlayback { get; set; } = true;

    /// <summary>プレイリストに PDF を載せる。既定オン。プレイヤー専用。</summary>
    public bool LibraryShowPlaylistPdf { get; set; } = true;

    /// <summary>プレイリストに MOV を載せる。既定オフ。プレイヤー専用。</summary>
    public bool LibraryShowPlaylistMov { get; set; }

    /// <summary>プレイリストに MP4 を載せる。既定オフ。プレイヤー専用。</summary>
    public bool LibraryShowPlaylistMp4 { get; set; }

    /// <summary>プレイリストに AVI を載せる。既定オフ。プレイヤー専用。</summary>
    public bool LibraryShowPlaylistAvi { get; set; }

    /// <summary>プレイリストに MKV を載せる。既定オフ。プレイヤー専用。</summary>
    public bool LibraryShowPlaylistMkv { get; set; }

    /// <summary>プレイリストに WebM を載せる。既定オフ。プレイヤー専用。</summary>
    public bool LibraryShowPlaylistWebm { get; set; }

    /// <summary>プレイリストに MPG / MPEG を載せる。既定オフ。プレイヤー専用。</summary>
    public bool LibraryShowPlaylistMpg { get; set; }

    /// <summary>動画引数起動ウィンドウの中心 X（DIP）。HasPosition のときだけ使う。</summary>
    public int VideoLaunchWindowX { get; set; }

    /// <summary>動画引数起動ウィンドウの中心 Y（DIP）。HasPosition のときだけ使う。</summary>
    public int VideoLaunchWindowY { get; set; }

    /// <summary>動画引数起動の中心位置を覚えたか。サイズは都度動画の原寸。</summary>
    public bool VideoLaunchWindowHasPosition { get; set; }

    /// <summary>動画ミニ（F8／引数起動）の A（波形・アナライザ・ファイル名）。既定オフ。</summary>
    public bool VideoLaunchHud { get; set; }

    /// <summary>動画ミニ（F8／引数起動）の T（タイムコード）。既定オフ。</summary>
    public bool VideoLaunchTimecode { get; set; }

    /// <summary>ユーザー用意の ffmpeg.exe。空または無効ならプロキシは作らない。</summary>
    public string FfmpegExePath { get; set; } = string.Empty;

    /// <summary>オンなら本再生でもプロキシを自動生成しない。既定オフ。</summary>
    public bool VideoProxyDisableAutoEncode { get; set; }

    /// <summary>動画プロキシを残す日数。1 / 7 / 14 / 30。既定 7。</summary>
    public int VideoProxyRetentionDays { get; set; } = VideoProxy.DefaultRetentionDays;

    /// <summary>再生で無音区間を飛ばす。既定オフ。</summary>
    public bool SilentSkip { get; set; }

    /// <summary>Silent Skip と無音削除の無音しきい値（dBFS）。既定 -60。</summary>
    public double SilentSkipThresholdDb { get; set; } = global::MgaSonicAnvil.Audio.SilentSkip.DefaultThresholdDb;

    /// <summary>録音 Silent Skip で、しきい値を下回った時点から挿入する無音の時間（ms）。再生には使わない。既定 500。</summary>
    public int SilentSkipRecordPadMs { get; set; } = global::MgaSonicAnvil.Audio.SilentSkip.DefaultRecordPadMs;

    /// <summary>Silent Skip 録音の停止時、pad で区切られた可聴だけにリージョンを付ける。既定オフ。</summary>
    public bool SilentSkipRecordAddRegion { get; set; }

    /// <summary>継ぎ目のプチノイズ防止フェード（ms）。既定 20。</summary>
    public int ClickGuardFadeMs { get; set; } = global::MgaSonicAnvil.Audio.ClickGuard.DefaultFadeMilliseconds;

    public int WindowX { get; set; }

    public int WindowY { get; set; }

    public int WindowWidth { get; set; }

    public int WindowHeight { get; set; }

    /// <summary>Normal / Maximized。空または不明なら通常表示。</summary>
    public string WindowState { get; set; } = string.Empty;

    /// <summary>F10 プレイヤーのウィンドウ。未保存なら幅 0。</summary>
    public int PlayerWindowX { get; set; }

    public int PlayerWindowY { get; set; }

    public int PlayerWindowWidth { get; set; }

    public int PlayerWindowHeight { get; set; }

    /// <summary>Normal / Maximized。空または不明なら通常表示。</summary>
    public string PlayerWindowState { get; set; } = string.Empty;

    /// <summary>F9 ミニマムプレイヤーのウィンドウ。未保存なら幅 0。</summary>
    public int MinimalPlayerWindowX { get; set; }

    public int MinimalPlayerWindowY { get; set; }

    public int MinimalPlayerWindowWidth { get; set; }

    public int MinimalPlayerWindowHeight { get; set; }

    /// <summary>Normal / Maximized。空または不明なら通常表示。</summary>
    public string MinimalPlayerWindowState { get; set; } = string.Empty;

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

    /// <summary>旧単一列設定。読み取り互換のみ。新フィールドへ移したら空にする。</summary>
    public string[] LibraryListColumns { get; set; } = [];

    /// <summary>WAVE / AIFF 向け列。空は既定（ファイル／レート／ビット／Ch／時間／日付／親フォルダ／波形表示）。</summary>
    public string[] LibraryListColumnsWave { get; set; } = [];

    /// <summary>MP3 / M4A 向け列。空は既定（ファイル／タイトル／アルバム／…／親フォルダ。波形表示はオフ）。</summary>
    public string[] LibraryListColumnsMp3 { get; set; } = [];

    /// <summary>
    /// 混在プレイリスト向け列。空は Wave / MP3 設定の和集合。
    /// Wave / MP3 を変えるとクリアする。設定 UI には出さない。
    /// </summary>
    public string[] LibraryListColumnsMixed { get; set; } = [];

    /// <summary>WAVE / AIFF 向けプレイリスト波形列の幅。S / M / L。空または不正は L。</summary>
    public string LibraryPlaylistWaveformSizeWave { get; set; } = "L";

    /// <summary>MP3 / M4A 向けプレイリスト波形列の幅。S / M / L。空または不正は L。</summary>
    public string LibraryPlaylistWaveformSizeMp3 { get; set; } = "L";

    /// <summary>F10 リストのグループ。空は親フォルダ。</summary>
    public string LibraryListGroup { get; set; } = string.Empty;

    /// <summary>F10 左のフォルダツリー。空はマイミュージック（ルート解決後）。</summary>
    public string LibraryExplorerPath { get; set; } = string.Empty;

    /// <summary>F10 ツリーで展開していたフォルダ。無いものは起動時に落とす。</summary>
    public string[] LibraryExplorerExpanded { get; set; } = [];

    /// <summary>F10 ツリーのルート。空はマイミュージックのみ。</summary>
    public string[] LibraryExplorerRoots { get; set; } = [];

    /// <summary>F10 お気に入り（ファイル／フォルダのパス）。存在しないものは起動時に落とす。</summary>
    public string[] LibraryFavorites { get; set; } = [];

    /// <summary>F10 左のフォルダツリー幅。0 以下は既定。</summary>
    public double LibraryExplorerWidth { get; set; }

    /// <summary>F10 お気に入りの高さ比（ツリーとの合計に対するお気に入り側）。0 は半分。</summary>
    public double LibraryFavoritesSplit { get; set; }

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

    public int ResolvedVideoProxyRetentionDays() =>
        VideoProxy.ClampRetentionDays(VideoProxyRetentionDays);

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

    public LibraryFileColumn[] ResolvedLibraryListColumnsWave()
    {
        MigrateLegacyLibraryListColumns();
        return LibraryColumnFilter.ResolveWave(LibraryListColumnsWave);
    }

    public LibraryFileColumn[] ResolvedLibraryListColumnsMp3()
    {
        MigrateLegacyLibraryListColumns();
        return LibraryColumnFilter.ResolveMp3(LibraryListColumnsMp3);
    }

    public LibraryFileColumn[] ResolvedLibraryListColumnsMixed()
    {
        MigrateLegacyLibraryListColumns();
        return LibraryColumnFilter.ResolveMixed(
            LibraryListColumnsMixed,
            ResolvedLibraryListColumnsWave(),
            ResolvedLibraryListColumnsMp3());
    }

    public void ApplyLibraryListColumnsWave(IEnumerable<LibraryFileColumn> columns)
    {
        MigrateLegacyLibraryListColumns();
        LibraryListColumnsWave = LibraryColumnFilter.Serialize(columns, LibraryColumnFilter.WaveDefaults);
        LibraryListColumnsMixed = [];
        LibraryListColumns = [];
    }

    public void ApplyLibraryListColumnsMp3(IEnumerable<LibraryFileColumn> columns)
    {
        MigrateLegacyLibraryListColumns();
        LibraryListColumnsMp3 = LibraryColumnFilter.Serialize(columns, LibraryColumnFilter.Mp3Defaults);
        LibraryListColumnsMixed = [];
        LibraryListColumns = [];
    }

    public void ApplyLibraryListColumnsMixed(IEnumerable<LibraryFileColumn> columns)
    {
        MigrateLegacyLibraryListColumns();
        LibraryListColumnsMixed = LibraryColumnFilter.Serialize(
            columns,
            LibraryColumnFilter.MixedDefaults);
        LibraryListColumns = [];
    }

    /// <summary>Wave / MP3 を更新し、混在のユーザー記憶を捨てて和集合へ戻す。</summary>
    public void ApplyLibraryListColumnPresets(
        IEnumerable<LibraryFileColumn> waveColumns,
        IEnumerable<LibraryFileColumn> mp3Columns)
    {
        LibraryListColumnsWave = LibraryColumnFilter.Serialize(waveColumns, LibraryColumnFilter.WaveDefaults);
        LibraryListColumnsMp3 = LibraryColumnFilter.Serialize(mp3Columns, LibraryColumnFilter.Mp3Defaults);
        LibraryListColumnsMixed = [];
        LibraryListColumns = [];
    }

    public void ApplyLibraryListColumnPresets(
        IEnumerable<LibraryFileColumn> waveColumns,
        IEnumerable<LibraryFileColumn> mp3Columns,
        IEnumerable<LibraryFileColumn> mixedColumns)
    {
        LibraryListColumnsWave = LibraryColumnFilter.Serialize(waveColumns, LibraryColumnFilter.WaveDefaults);
        LibraryListColumnsMp3 = LibraryColumnFilter.Serialize(mp3Columns, LibraryColumnFilter.Mp3Defaults);
        LibraryListColumnsMixed = LibraryColumnFilter.Serialize(
            mixedColumns,
            LibraryColumnFilter.MixedDefaults);
        LibraryListColumns = [];
    }

    private void MigrateLegacyLibraryListColumns()
    {
        if (LibraryListColumnsWave.Length > 0 || LibraryListColumnsMp3.Length > 0)
        {
            return;
        }

        if (LibraryListColumns.Length == 0)
        {
            return;
        }

        if (LibraryColumnFilter.IsLegacyDefaultStored(LibraryListColumns))
        {
            LibraryListColumns = [];
            return;
        }

        LibraryListColumnsWave = LibraryListColumns;
        LibraryListColumnsMp3 = LibraryListColumns;
        LibraryListColumns = [];
    }

    public LibraryPlaylistWaveformSize ResolvedLibraryPlaylistWaveformSizeWave() =>
        LibraryPlaylistWaveformSizes.Parse(LibraryPlaylistWaveformSizeWave, LibraryPlaylistWaveformSize.L);

    public LibraryPlaylistWaveformSize ResolvedLibraryPlaylistWaveformSizeMp3() =>
        LibraryPlaylistWaveformSizes.Parse(LibraryPlaylistWaveformSizeMp3, LibraryPlaylistWaveformSize.L);

    public void ApplyLibraryPlaylistWaveformSizeWave(LibraryPlaylistWaveformSize size) =>
        LibraryPlaylistWaveformSizeWave = LibraryPlaylistWaveformSizes.Format(size);

    public void ApplyLibraryPlaylistWaveformSizeMp3(LibraryPlaylistWaveformSize size) =>
        LibraryPlaylistWaveformSizeMp3 = LibraryPlaylistWaveformSizes.Format(size);

    public LibraryFileGroup ResolvedLibraryListGroup() =>
        LibraryFileList.ParseGroup(LibraryListGroup);

    public void ApplyLibraryListGroup(LibraryFileGroup group) =>
        LibraryListGroup = LibraryFileList.SerializeGroup(group);

    public string ResolvedLibraryExplorerPath() =>
        LibraryExplorerPaths.Resolve(LibraryExplorerPath);

    public void ApplyLibraryExplorerPath(string path) =>
        LibraryExplorerPath = LibraryExplorerPaths.Resolve(path);

    public string[] ResolvedLibraryExplorerRoots() =>
        LibraryExplorerPaths.ResolveRoots(LibraryExplorerRoots);

    public void ApplyLibraryExplorerRoots(IEnumerable<string> roots) =>
        LibraryExplorerRoots = LibraryExplorerPaths.SerializeRoots(roots);

    public string[] ResolvedLibraryExplorerExpanded() =>
        LibraryExplorerPaths.ResolveExpanded(LibraryExplorerExpanded);

    public void ApplyLibraryExplorerExpanded(IEnumerable<string> paths) =>
        LibraryExplorerExpanded = LibraryExplorerPaths.SerializeExpanded(paths);

    public string[] ResolvedLibraryFavoritePaths() =>
        LibraryFavoritePaths.Resolve(LibraryFavorites);

    public void ApplyLibraryFavoritePaths(IEnumerable<string> paths) =>
        LibraryFavorites = LibraryFavoritePaths.Serialize(paths);
}

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SpeakerPreset))]
[JsonSerializable(typeof(SpeakerPreset[]))]
[JsonSerializable(typeof(OpenDocumentSnapshot))]
[JsonSerializable(typeof(OpenDocumentSnapshot[]))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
