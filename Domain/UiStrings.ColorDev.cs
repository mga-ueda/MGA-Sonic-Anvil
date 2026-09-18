namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string ColorDevTitleFor(UiTheme theme) =>
        theme == UiTheme.Light
            ? Get("色設定（ライトモード）", "Color settings (Light mode)")
            : Get("色設定（ダークモード）", "Color settings (Dark mode)");
    public static string ColorDevClose => Get("閉じる", "Close");
    public static string ColorDevResetToDefaults => Get("既定に戻す", "Reset to defaults");
    public static string ColorDevResetThis => Get("この色を既定に戻す", "Reset this color");
    public static string ColorDevExport => Get("エクスポート", "Export");
    public static string ColorDevImport => Get("インポート", "Import");
    public static string ColorDevExportTitle => Get("配色を書き出す", "Export color scheme");
    public static string ColorDevImportTitle => Get("配色を読み込む", "Import color scheme");
    public static string FilterColorScheme => Get(
        "配色|*.json|すべて|*.*",
        "Color scheme|*.json|All|*.*");
    public static string ColorDevImportFailed => Get(
        "配色ファイルを読めませんでした。",
        "Could not read the color scheme file.");
    public static string ColorDevExportFailed => Get(
        "配色ファイルを書けませんでした。",
        "Could not write the color scheme file.");
    public static string ColorDevSearch => Get("検索", "Search");
    public static string ColorDevHex => Get("Hex", "Hex");
    public static string ColorDevPickHint => Get(
        "左の一覧から色を選ぶと、ここで調整できます。",
        "Select a color on the left to edit it here.");
    public static string ColorDevNoMatches => Get("一致する色がありません。", "No matching colors.");

    public static string ColorDevGroupShared => Get("共通", "Shared");
    public static string ColorDevGroupOverview => Get("全体波形", "Overview");
    public static string ColorDevGroupWaveform => Get("波形", "Waveform");
    public static string ColorDevGroupGuides => Get("ガイド", "Guides");
    public static string ColorDevGroupSelection => Get("選択", "Selection");
    public static string ColorDevGroupSampleLoop => Get("サンプルループ", "Sample loop");
    public static string ColorDevGroupRegion => Get("リージョン", "Region");
    public static string ColorDevGroupMarker => Get("マーカー", "Marker");
    public static string ColorDevGroupMeter => Get("レベルメーター", "Level meter");
    public static string ColorDevGroupSpectrum => Get("スペアナ", "Spectrum");
    public static string ColorDevGroupVectorScope => Get("ベクタースコープ", "Vector scope");
    public static string ColorDevGroupTransport => Get("トランスポート", "Transport");
    public static string ColorDevGroupStatus => Get("ステータスバー", "Status bar");
    public static string ColorDevGroupDialog => Get("ダイアログ", "Dialog");
    public static string ColorDevGroupOther => Get("その他", "Other");

    public static string ColorLabel(string key)
    {
        var token = key.EndsWith("Brush", StringComparison.Ordinal) ? key[..^5] : key;
        return token switch
        {
            "SurfaceBack" => Get("基本背景", "Base background"),
            "ChromeBack" => Get("クローム背景", "Chrome background"),
            "ChromeBorder" => Get("クローム境界", "Chrome border"),
            "LibraryExplorerSplitter" => Get("プレイヤー・ツリー区切り", "Player · tree splitter"),
            "ChromeMid" => Get("中間グレー", "Mid gray"),
            "ChromeDim" => Get("無効／薄いグレー", "Disabled / dim gray"),
            "PrimaryFore" => Get("標準文字", "Standard text"),
            "MutedFore" => Get("弱い文字", "Muted text"),
            "AccentCyan" => Get("シアンアクセント", "Cyan accent"),
            "DirtyAccent" => Get("未保存アクセント", "Dirty accent"),
            "ScrollThumbBack" => Get("スクロールつまみ", "Scroll thumb"),
            "ScrollThumbHoverBack" => Get("スクロールつまみ・ホバー", "Scroll thumb · hover"),
            "ScrollThumbPressedBack" => Get("スクロールつまみ・ドラッグ", "Scroll thumb · drag"),
            "ScrollThumbGrip" => Get("スクロールつまみ・端の線", "Scroll thumb · end grips"),
            "ControlHoverBorder" => Get("コントロール・ホバー境界", "Control · hover border"),
            "DialogInputBack" => Get("入力欄の背景", "Input background"),
            "MenuSeparator" => Get("メニュー区切り", "Menu separator"),
            "MenuHighlightBack" => Get("メニューハイライト", "Menu highlight"),
            "MenuDisabledFore" => Get("メニュー無効文字", "Menu disabled text"),
            "ProjectBarBack" => Get("背景", "Background"),
            "OverviewOutsideFill" => Get("範囲外", "Outside view"),
            "WaveformBack" => Get("エリア背景", "Area background"),
            "WaveformRecordBack" => Get("録音中の背景", "Recording background"),
            "WaveFill" => Get("波形", "Waveform"),
            "WaveFillOverlay" => Get("スペクトログラム重ね", "Spectrogram overlay"),
            "WaveZeroLine" => Get("0 dB 線", "0 dB line"),
            "TimelineWellBack" => Get("タイムライン背景", "Timeline background"),
            "WaveformTileActiveHeader" => Get("タイル・アクティブ見出し", "Tile · active header"),
            "DbScaleFore" => Get("音量目盛", "Volume scale"),
            "SpectrogramScaleFore" => Get("周波数目盛", "Frequency scale"),
            "Playhead" => Get("再生ヘッド", "Playhead"),
            "SeekExit" => Get("Exit 二重再生ヘッド", "Exit dual playhead"),
            "MouseGuide" => Get("マウスガイド", "Mouse guide"),
            "MouseGuideOnSelection" => Get("マウスガイド（選択上）", "Mouse guide (on selection)"),
            "LoopRangeFill" => Get("塗り", "Fill"),
            "SampleLoopTimeline" => Get("タイムライン", "Timeline"),
            "SampleLoopWaveFill" => Get("波形塗り", "Wave fill"),
            "SampleLoopTimeLabelFore" => Get("時刻文字", "Time text"),
            "RegionTimeline" => Get("タイムライン", "Timeline"),
            "RegionWaveFill" => Get("波形塗り", "Wave fill"),
            "RegionTimeLabelFore" => Get("時刻文字", "Time text"),
            "RegionWaveFillAnacrusis" => Get("波形（-A）", "Wave (-A)"),
            "RegionWaveFillLoop" => Get("波形（-L）", "Wave (-L)"),
            "RegionWaveFillExit" => Get("波形（-E）", "Wave (-E)"),
            "Marker" => Get("通常", "Normal"),
            "MarkerSelected" => Get("選択", "Selected"),
            "MarkerSelectedBorder" => Get("選択枠", "Selected border"),
            "MarkerLabelFore" => Get("番号文字", "Number text"),
            "LevelMeterTrackBack" => Get("トラック", "Track"),
            "LevelMeterTrackBorder" => Get("トラック枠", "Track border"),
            "LevelMeterHoldBorder" => Get("ホールド枠", "Hold border"),
            "LevelMeterTick" => Get("目盛", "Ticks"),
            "LevelMeterClipOff" => Get("クリップ消灯", "Clip off"),
            "LevelMeterClipOffBorder" => Get("クリップ消灯枠", "Clip off border"),
            "SurroundHullStroke" => Get("サラウンド RMS 枠", "Surround RMS hull"),
            "LevelGradFloor" => Get("グラデ・暗い端", "Gradient · dark end"),
            "LevelGradLow" => Get("グラデ・暗い中間", "Gradient · dark mid"),
            "LevelGradMid" => Get("グラデ・中間", "Gradient · mid"),
            "LevelGradHigh" => Get("グラデ・明るい中間", "Gradient · bright mid"),
            "LevelGradCeil" => Get("グラデ・明るい端", "Gradient · bright end"),
            "VectorScopeBack" => Get("背景", "Background"),
            "VectorScopeTrace" => Get("軌跡", "Trace"),
            "VectorScopeGrid" => Get("十字", "Cross"),
            "VectorScopeCorrelation" => Get("相関針", "Correlation"),
            "TransportBack" => Get("背景", "Background"),
            "TransportFore" => Get("文字", "Text"),
            "TransportDisabledFore" => Get("無効文字", "Disabled text"),
            "TransportHoverBack" => Get("ホバー", "Hover"),
            "TransportPressedBack" => Get("押下", "Pressed"),
            "HistoryStripBack" => Get("履歴・背景", "History · background"),
            "HistoryStripHoverBack" => Get("履歴・ホバー", "History · hover"),
            "HistoryStripCurrentFore" => Get("履歴・現在", "History · current"),
            "HistoryStripPastFore" => Get("履歴・過去", "History · past"),
            "HistoryStripFutureFore" => Get("履歴・未来", "History · undone"),
            "ActionCopyrightFore" => Get("著作権文字", "Copyright text"),
            "ActionLinkFore" => Get("リンク", "Link"),
            "ActionLinkHoverFore" => Get("リンクホバー", "Link hover"),
            "WaapiToggleOffBack" => Get("WAAPI オフ・背景", "WAAPI off · background"),
            "WaapiToggleOffHoverBack" => Get("WAAPI オフ・ホバー", "WAAPI off · hover"),
            "WaapiToggleOffFore" => Get("WAAPI オフ・文字", "WAAPI off · text"),
            "WaapiToggleOnBack" => Get("WAAPI オン・背景", "WAAPI on · background"),
            "WaapiToggleOnHoverBack" => Get("WAAPI オン・ホバー", "WAAPI on · hover"),
            "WaapiToggleOnFore" => Get("WAAPI オン・文字", "WAAPI on · text"),
            "StatusBarBack" => Get("背景", "Background"),
            "WaapiBarBack" => Get("WAAPI バー背景", "WAAPI bar background"),
            "StatusBarTitleFore" => Get("見出し", "Heading"),
            "StatusBarDetailFore" => Get("詳細文字", "Detail text"),
            "StatusBarErrorDetailFore" => Get("エラー文字", "Error text"),
            "StatusBarConnectedBadgeBack" => Get("CONNECT", "CONNECT"),
            "StatusBarDisconnectedBadgeBack" => Get("DISCONNECT", "DISCONNECT"),
            "KeepTargetLockFore" => Get("Keep Target・施錠", "Keep Target · locked"),
            "KeepTargetLockHoverFore" => Get("Keep Target・施錠ホバー", "Keep Target · locked hover"),
            "KeepTargetUnlockFore" => Get("Keep Target・開錠", "Keep Target · unlocked"),
            "KeepTargetUnlockHoverFore" => Get("Keep Target・開錠ホバー", "Keep Target · unlocked hover"),
            "StatusExportButtonFill" => Get("EXPORT・塗り", "EXPORT · fill"),
            "StatusExportButtonHoverFill" => Get("EXPORT・ホバー塗り", "EXPORT · hover fill"),
            "StatusExportButtonBack" => Get("EXPORT・枠", "EXPORT · border"),
            "StatusExportButtonHoverBack" => Get("EXPORT・ホバー枠", "EXPORT · hover border"),
            "StatusExportButtonPressedBack" => Get("EXPORT・押下枠", "EXPORT · pressed border"),
            "StatusExportButtonFore" => Get("EXPORT・文字", "EXPORT · text"),
            "WindowBack" => Get("ウィンドウ背景", "Window background"),
            "ColorPanelBack" => Get("色パネル背景", "Color panel background"),
            "ExportButtonFill" => Get("OK・塗り", "OK · fill"),
            "ExportButtonHoverFill" => Get("OK・ホバー塗り", "OK · hover fill"),
            "ExportButtonBack" => Get("OK・枠", "OK · border"),
            "ExportButtonHoverBack" => Get("OK・ホバー枠", "OK · hover border"),
            "ExportButtonPressedBack" => Get("OK・押下枠", "OK · pressed border"),
            "ExportButtonFore" => Get("OK・文字", "OK · text"),
            "ClearButtonFill" => Get("キャンセル・塗り", "Cancel · fill"),
            "ClearButtonHoverFill" => Get("キャンセル・ホバー塗り", "Cancel · hover fill"),
            "ClearButtonBack" => Get("キャンセル・枠", "Cancel · border"),
            "ClearButtonHoverBack" => Get("キャンセル・ホバー枠", "Cancel · hover border"),
            "ClearButtonPressedBack" => Get("キャンセル・押下枠", "Cancel · pressed border"),
            "ClearButtonFore" => Get("キャンセル・文字", "Cancel · text"),
            _ => token,
        };
    }
}
