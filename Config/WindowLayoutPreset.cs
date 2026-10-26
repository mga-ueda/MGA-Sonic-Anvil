namespace MgaSonicAnvil.Config;

/// <summary>F9–F12 のレイアウトプリセット枠。</summary>
internal enum WindowLayoutSlot
{
    F9,
    F10,
    F11,
    F12,
}

/// <summary>プリセットの全画面指定。</summary>
internal enum LayoutFullscreenMode
{
    Off,
    On,
    Toggle,
}

/// <summary>アナライザーエリア（TransportChromeHost）の上下。</summary>
internal enum LayoutAnalyzerDock
{
    Bottom,
    Top,
}

/// <summary>F9–F12 各スロットの表示内容。ワークスペース項目は持たない。</summary>
internal sealed class WindowLayoutPreset
{
    /// <summary>Off / On / Toggle。</summary>
    public string FullscreenMode { get; set; } = nameof(LayoutFullscreenMode.Off);

    /// <summary>
    /// Toggle のとき、起動後そのスロットを初めて開いたときだけ使う初期オン／オフ。
    /// 以後の往復はセッション内の記憶が優先（トグル操作自体は何度でも可）。
    /// </summary>
    public bool FullscreenToggleStartsOn { get; set; }

    /// <summary>
    /// ライブラリ列（ツリー／お気に入り）。プレイヤーではサイド列、エディタでは帯全体。
    /// 設定 UI には出さずスロット既定で固定。表示名は「ライブラリ」。
    /// </summary>
    public bool ShowLibraryAndFavorites { get; set; }

    /// <summary>メイン波形ホスト。エディタ枠では常にオン（設定 UI なし）。</summary>
    public bool ShowWaveform { get; set; } = true;

    /// <summary>エディタ（F11／F12）は常に波形表示。</summary>
    public bool ResolvedShowWaveform(WindowLayoutSlot slot) =>
        IsPlayerLayoutSlot(slot) ? ShowWaveform : true;

    /// <summary>TransportChromeHost 全体とゴニオ（表示オン）。レベルメーターは含まない。</summary>
    public bool ShowAnalyzerArea { get; set; } = true;

    /// <summary>
    /// Bottom / Top。ShowAnalyzerArea がオフのときは無視。
    /// ゴニオは右列で、Bottom ならピーク下・Top ならピーク上。
    /// </summary>
    public string AnalyzerDock { get; set; } = nameof(LayoutAnalyzerDock.Bottom);

    /// <summary>右列ピークレベルメーター。ゴニオの表示は ShowAnalyzerArea。</summary>
    public bool ShowLevelMeter { get; set; } = true;

    /// <summary>
    /// ウィンドウ上部のステータスバー。null はスロット既定（旧設定ファイル互換）。
    /// </summary>
    public bool? ShowStatusBar { get; set; }

    public LayoutFullscreenMode ResolvedFullscreenMode() =>
        FullscreenMode?.Trim().ToLowerInvariant() switch
        {
            "on" => LayoutFullscreenMode.On,
            "toggle" => LayoutFullscreenMode.Toggle,
            _ => LayoutFullscreenMode.Off,
        };

    public void ApplyFullscreenMode(LayoutFullscreenMode mode) =>
        FullscreenMode = mode switch
        {
            LayoutFullscreenMode.On => "On",
            LayoutFullscreenMode.Toggle => "Toggle",
            _ => "Off",
        };

    public LayoutAnalyzerDock ResolvedAnalyzerDock() =>
        string.Equals(AnalyzerDock?.Trim(), "Top", StringComparison.OrdinalIgnoreCase)
            ? LayoutAnalyzerDock.Top
            : LayoutAnalyzerDock.Bottom;

    public void ApplyAnalyzerDock(LayoutAnalyzerDock dock) =>
        AnalyzerDock = dock == LayoutAnalyzerDock.Top ? "Top" : "Bottom";

    /// <summary>
    /// 別スロットへ切替えたときの全画面初期値。
    /// Toggle はセッション初回のみ（呼び出し側が記憶を持たないとき）。
    /// </summary>
    public bool InitialFullscreenOn() =>
        ResolvedFullscreenMode() switch
        {
            LayoutFullscreenMode.On => true,
            LayoutFullscreenMode.Toggle => FullscreenToggleStartsOn,
            _ => false,
        };

    public WindowLayoutPreset Clone() =>
        new()
        {
            FullscreenMode = FullscreenMode,
            FullscreenToggleStartsOn = FullscreenToggleStartsOn,
            ShowLibraryAndFavorites = ShowLibraryAndFavorites,
            ShowWaveform = ShowWaveform,
            ShowAnalyzerArea = ShowAnalyzerArea,
            AnalyzerDock = AnalyzerDock,
            ShowLevelMeter = ShowLevelMeter,
            ShowStatusBar = ShowStatusBar,
        };

    public bool ResolvedShowStatusBar(WindowLayoutSlot slot) =>
        ShowStatusBar ?? CreateDefault(slot).ShowStatusBar ?? true;

    /// <summary>F9／F10。プレイリスト付きプレイヤー。</summary>
    public static bool IsPlayerLayoutSlot(WindowLayoutSlot slot) =>
        slot is WindowLayoutSlot.F9 or WindowLayoutSlot.F10;

    /// <summary>
    /// ライブラリ列（ツリー／お気に入り）。全スロットとも既定固定（設定 UI なし）。
    /// F9=オフ／F10=オン／F11・F12=オフ。プレイリストはプレイヤーでは常に出す。
    /// </summary>
    public bool ResolvedShowLibraryAndFavorites(WindowLayoutSlot slot) =>
        CreateDefault(slot).ShowLibraryAndFavorites;

    public static WindowLayoutPreset CreateDefault(WindowLayoutSlot slot) =>
        slot switch
        {
            WindowLayoutSlot.F9 => new WindowLayoutPreset
            {
                FullscreenMode = "Off",
                ShowLibraryAndFavorites = false,
                ShowWaveform = true,
                ShowAnalyzerArea = false,
                AnalyzerDock = "Bottom",
                ShowLevelMeter = false,
                ShowStatusBar = false,
            },
            WindowLayoutSlot.F10 => new WindowLayoutPreset
            {
                FullscreenMode = "Off",
                ShowLibraryAndFavorites = true,
                ShowWaveform = true,
                ShowAnalyzerArea = true,
                AnalyzerDock = "Bottom",
                ShowLevelMeter = true,
                ShowStatusBar = true,
            },
            WindowLayoutSlot.F11 => new WindowLayoutPreset
            {
                FullscreenMode = "Toggle",
                FullscreenToggleStartsOn = true,
                ShowLibraryAndFavorites = false,
                ShowWaveform = true,
                ShowAnalyzerArea = false,
                AnalyzerDock = "Bottom",
                ShowLevelMeter = true,
                ShowStatusBar = true,
            },
            _ => new WindowLayoutPreset
            {
                FullscreenMode = "Toggle",
                FullscreenToggleStartsOn = false,
                ShowLibraryAndFavorites = false,
                ShowWaveform = true,
                ShowAnalyzerArea = true,
                AnalyzerDock = "Bottom",
                ShowLevelMeter = true,
                ShowStatusBar = true,
            },
        };
}

/// <summary>プリセット適用の純ロジック（UI 非依存）。</summary>
internal static class WindowLayoutPresetLogic
{
    /// <summary>
    /// 同一スロット再押下は Toggle のときだけ全画面を反転。固定 On/Off は現状維持。
    /// 別スロットの Toggle は記憶があればそれを使う。
    /// 記憶が無い初回は、起動など非ショートカットならトグル開始を載せ、
    /// ショートカットなら開始状態からトグル（開始オフの初回押下が無変化にならない）。
    /// 固定 On/Off の別スロットは常に InitialFullscreenOn。
    /// </summary>
    public static bool ResolveFullscreenOn(
        WindowLayoutPreset preset,
        bool sameSlot,
        bool currentlyFullscreenOn,
        bool? rememberedToggleFullscreenOn = null,
        bool userShortcut = false)
    {
        var mode = preset.ResolvedFullscreenMode();
        if (sameSlot)
        {
            return mode == LayoutFullscreenMode.Toggle
                ? !currentlyFullscreenOn
                : currentlyFullscreenOn;
        }

        if (mode == LayoutFullscreenMode.Toggle
            && rememberedToggleFullscreenOn is { } remembered)
        {
            return remembered;
        }

        var initial = preset.InitialFullscreenOn();
        if (mode == LayoutFullscreenMode.Toggle && userShortcut)
        {
            // トグル開始は起動時に載せる。初回キーはそこから反転させる。
            return !initial;
        }

        return initial;
    }

    /// <summary>
    /// スロット＋全画面オン／オフ → 位置記憶キー名。
    /// トグルオフ時は非全画面枠（Waveform / Analyzer 等）を使う。
    /// </summary>
    public static string PlacementKindName(WindowLayoutSlot slot, bool fullscreenOn) =>
        (slot, fullscreenOn) switch
        {
            (WindowLayoutSlot.F9, true) => "WaveformFullscreen",
            (WindowLayoutSlot.F9, false) => "MinimalPlayer",
            (WindowLayoutSlot.F10, true) => "AnalyzerFullscreen",
            (WindowLayoutSlot.F10, false) => "Player",
            (WindowLayoutSlot.F11, true) => "WaveformFullscreen",
            (WindowLayoutSlot.F11, false) => "Waveform",
            (WindowLayoutSlot.F12, true) => "AnalyzerFullscreen",
            _ => "Analyzer",
        };
}
