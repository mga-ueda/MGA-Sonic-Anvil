namespace MgaSonicAnvil.UI;

/// <summary>
/// 右クリックメニューの InputGestureText。
/// 同じ操作に複数キーがあるときは " / " で並べる（例: Esc / Shift+A / Shift+V）。
/// </summary>
internal static class MenuGestures
{
    public const string Redo = "Ctrl+Y / Ctrl+Shift+Z";
    public const string Delete = "Delete";
    public const string AddMarker = "M / Ins";
    public const string PitchUp = "Ctrl++ / Ctrl+Num+";
    public const string PitchDown = "Ctrl+- / Ctrl+Num-";
    public const string PitchReset = "Ctrl+0 / Ctrl+Num0";
    public const string PreviousTrack = "P / Num4";
    public const string NextTrack = "N / Num6";
    public const string RestartTrack = "R / Num5";
    public const string CenterPlayhead = "Z / Num.";
    public const string VideoZoomIn = "+ / Num+";
    public const string VideoZoomOut = "- / Num-";
    public const string ExitRealPlayback = "Q / Esc";
    public const string NextTab = "Ctrl+Tab / Ctrl+PageDown / Num6";
    public const string PrevTab = "Ctrl+Shift+Tab / Ctrl+PageUp / Num4";
    public const string ViewWaveform = "Esc / Shift+A / Shift+V";
    public const string ExplorerExpand = "Shift+8 / *";
    public const string ExplorerCollapse = "/ / Num/";
}
