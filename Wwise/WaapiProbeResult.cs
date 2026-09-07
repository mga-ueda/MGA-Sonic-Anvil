namespace MgaSonicAnvil.Wwise;

/// <summary>WAAPI 接続確認と、Wwise 上の現在選択（作成先）の結果。</summary>
internal sealed class WaapiProbeResult
{
    public bool Ok { get; init; }
    public string WwiseVersion { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectFilePath { get; init; } = string.Empty;
    public string SelectedPath { get; init; } = string.Empty;
    public string SelectedType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public bool HasSelection => SelectedPath.Length > 0;
}
