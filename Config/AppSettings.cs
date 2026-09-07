using System.Text.Json.Serialization;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Config;

internal sealed class AppSettings
{
    public string AudioApi { get; set; } = "WaveOut";

    public string AudioDeviceId { get; set; } = string.Empty;

    public int WaveformHeightScale { get; set; } = 1;

    public int Mp3BitRate { get; set; } = 192;

    public bool AlwaysOnTop { get; set; }

    public string LastDocumentPath { get; set; } = string.Empty;

    public bool LastDocumentDirty { get; set; }

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
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
