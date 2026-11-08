namespace MgaSonicAnvil.UI;

/// <summary>
/// 右クリックメニューの InputGestureText。
/// 同じ操作に複数キーがあるときは " / " で並べる（例: Esc / Shift+A / Shift+V）。
/// </summary>
internal static class MenuGestures
{
    public const string Redo = "Ctrl+Y / Ctrl+Shift+Z";
    public const string Delete = "Delete";
    public const string Trim = "Ctrl+T";
    public const string Volume = "V / Ctrl+Shift+V";
    public const string VolumePlayer = "Ctrl+Shift+V";
    public const string AddMarker = "M / Ins";
    public const string PitchUp = "Ctrl++ / Ctrl+Num+";
    public const string PitchDown = "Ctrl+- / Ctrl+Num-";
    public const string PitchReset = "Ctrl+0 / Ctrl+Num0";
    public const string PreviousTrack = "P / Num4";
    public const string NextTrack = "N / Num6";
    public const string RestartTrack = "R / Num5";
    public const string SelectByGroupUp = "Ctrl+Shift+Up";
    public const string SelectByGroupDown = "Ctrl+Shift+Down";
    /// <summary>センターロック（再生ヘッド中央寄せではない）。</summary>
    public const string CenterLock = "Z / Num.";

    /// <summary>旧名。表示は <see cref="CenterLock"/> と同じ。</summary>
    public const string CenterPlayhead = CenterLock;
    public const string VideoZoomIn = "+ / Num+";
    public const string VideoZoomOut = "- / Num-";
    public const string ExitRealPlayback = "Q / Esc";
    public const string NextTab = "Ctrl+Tab / Ctrl+PageDown / Num6";
    public const string PrevTab = "Ctrl+Shift+Tab / Ctrl+PageUp / Num4";
    public const string ViewWaveform = "Esc / Shift+A / Shift+V";
    public const string ExplorerExpand = "Shift+8 / *";
    public const string ExplorerCollapse = "/ / Num/";
    public const string CrossfadeEdit = "Shift+X";
}
