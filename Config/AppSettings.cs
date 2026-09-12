using System.Text.Json.Serialization;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.Config;

internal sealed class AppSettings
{
    public string AudioApi { get; set; } = "WaveOut";

    public string AudioDeviceId { get; set; } = string.Empty;

    /// <summary>旧設定。読み込み時に SpeakerPresets へ移す。</summary>
    public string RecordLayout { get; set; } = "Stereo";

    /// <summary>旧設定。空なら RecordLayout を使っていた。</summary>
    public string PlaybackLayout { get; set; } = string.Empty;

    public string RecordDeviceId { get; set; } = string.Empty;

    public int[] RecordInputMap { get; set; } = [];

    public int[] PlaybackOutputMap { get; set; } = [];

    public int[] FileChannelMap { get; set; } = [];

    public SpeakerPreset[] SpeakerPresets { get; set; } = [];

    public string ActiveSpeakerPresetId { get; set; } = string.Empty;

    /// <summary>有効にするスピーカー定義。空は Stereo のみ。</summary>
    public string[] VisibleSpeakerPresetIds { get; set; } = [];

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

    /// <summary>配色。auto / dark / light。既定は OS に従う。</summary>
    public string UiTheme { get; set; } = "auto";

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
        ResolvedSpeaker().ToAudioOutputSettings();

    public void ApplyAudioOutput(AudioOutputSettings settings)
    {
        var speaker = ResolvedSpeaker();
        speaker.ApplyAudioOutput(settings);
        MirrorActiveSpeaker();
    }

    public double ResolvedLoudnessTargetLufs() =>
        LoudnessMeterEngine.ClampTargetLufs(LoudnessTargetLufs);

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

    /// <summary>カタログを正本にし、保存済みのデバイスとマップを載せる。</summary>
    public void EnsureSpeakerPresets()
    {
        var saved = SpeakerPresets;
        if (saved is not { Length: > 0 })
        {
            saved = SeedFromLegacyFields();
        }

        SpeakerPresets = SpeakerPreset.MergeCatalog(saved);
        if (FindSpeaker(ActiveSpeakerPresetId) is null)
        {
            if (ChannelLayout.TryGet(RecordLayout, out var fromRecord))
            {
                ActiveSpeakerPresetId = fromRecord.Id;
            }
            else
            {
                ActiveSpeakerPresetId = SpeakerPreset.ResolveActiveId(
                    SpeakerPresets,
                    ActiveSpeakerPresetId,
                    saved);
            }
        }

        if (FindSpeaker(ActiveSpeakerPresetId) is null)
        {
            ActiveSpeakerPresetId = SpeakerPreset.DefaultId;
        }

        MirrorActiveSpeaker();
    }

    private SpeakerPreset[] SeedFromLegacyFields()
    {
        var output = new AudioOutputSettings(AudioOutputSettings.ParseApi(AudioApi), AudioDeviceId ?? string.Empty);
        var recordChannels = ChannelLayout.Parse(RecordLayout).Channels;
        var playChannels = ChannelLayout.Parse(
            string.IsNullOrWhiteSpace(PlaybackLayout) ? RecordLayout : PlaybackLayout).Channels;
        var channels = Math.Clamp(Math.Max(recordChannels, playChannels), 1, ChannelLayout.MaxChannels);
        var target = ChannelLayout.TryGet(RecordLayout, out var named)
            ? named
            : ChannelLayout.PreferredForChannels(channels);
        var catalog = SpeakerPreset.CreateCatalog();
        foreach (var item in catalog)
        {
            item.ApplyAudioOutput(output);
            if (!item.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            item.RecordInputMap = [.. RecordInputMap ?? []];
            item.PlaybackOutputMap = [.. PlaybackOutputMap ?? []];
            item.FileChannelMap = [.. FileChannelMap ?? []];
        }

        ActiveSpeakerPresetId = target.Id;
        return catalog;
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
[JsonSerializable(typeof(SpeakerPreset))]
[JsonSerializable(typeof(SpeakerPreset[]))]
[JsonSerializable(typeof(OpenDocumentSnapshot))]
[JsonSerializable(typeof(OpenDocumentSnapshot[]))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
