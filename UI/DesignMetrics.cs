using System.Windows;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal static class DesignMetrics
{
    public const double DesignDpi = 144d;
    public const double DipDpi = 96d;

    public static double Dip(double designPxAt144) => designPxAt144 * DipDpi / DesignDpi;

    public static double From96(double value96) => value96;

    /// <summary>
    /// 固定2行（上段 TRANS/EDIT/FILE、下段 MARK/VIEW/HELP）と右の履歴・ラウドネス・スペアナ・メーターが
    /// 折り返さず並ぶ下限。枠のぶんを足す。
    /// </summary>
    public static double WindowMinWidth =>
        Math.Ceiling(TransportFixedRowWidth + TransportSideChromeWidth + WindowFramePad);

    public static double WindowMinHeight => From96(420);

    public static double WindowDefaultWidth => WindowMinWidth;

    public static double WindowDefaultHeight => From96(573);

    public static double ProjectBarHeight => From96(30);

    public static GridLength ProjectBarHeightGrid => new(ProjectBarHeight);

    public static double StatusBarHeight => From96(28);

    public static double WaapiBarHeight => From96(32);

    public static double TipsHeaderHeight => From96(20);

    /// <summary>本文約 5 行。はみ出しはスクロール。</summary>
    public static double TipsBodyHeight => From96(88);

    /// <summary>Tips 枠の固定高さ（見出し + 本文）。</summary>
    public static double TipsPanelHeight => TipsHeaderHeight + TipsBodyHeight;

    public static double StatusExportButtonWidth => From96(80);

    public static double StatusExportButtonHeight => From96(24);

    public static double StatusOutputPathHeight => From96(22);

    public static double DocumentTabBarHeight => From96(22);

    public static double DocumentTabScrollButtonWidth => From96(20);

    public static double RulerHeight => From96(16);

    public static double MarkerLaneRowHeight => From96(16);

    public static double MarkerLaneHeight => MarkerLaneRowHeight * 2;

    public static double DbScaleWidth => From96(40);

    public static GridLength DbScaleWidthGrid => new(DbScaleWidth);

    /// <summary>全体表示の波形左端を、本波形（dB 目盛の右）に揃える。</summary>
    public static Thickness OverviewPad => new(0, 3, 0, 3);

    public static double WaveformHostMinHeight => From96(180);

    /// <summary>波形下の時間スクロール行（＋－／矢印と同じ高さ）。</summary>
    public static double WaveformScrollBarHeight => From96(16);

    public static GridLength WaveformScrollBarHeightGrid => new(WaveformScrollBarHeight);

    public static double WaveformScrollButtonWidth => From96(16);

    public static GridLength WaveformScrollButtonWidthGrid => new(WaveformScrollButtonWidth);

    /// <summary>つまみ両端の拡縮ヒット幅。</summary>
    public static double WaveformScrollHandleWidth => From96(8);

    /// <summary>スペクトログラム左の表示ブーストバー。時間スクロールの半分。</summary>
    public static double SpectrogramBoostBarWidth => WaveformScrollBarHeight * 0.5;

    /// <summary>ブーストつまみ。バーより大きいが、バーの 2 倍にはしない。</summary>
    public static double SpectrogramBoostThumbSize => From96(12);

    /// <summary>ブーストバーの上下余白。</summary>
    public static double SpectrogramBoostBarPad => From96(8);

    /// <summary>右寄せ dB 目盛の幅（数字＋隙間）。重ね表示のとき、バーはこの左側で中央。</summary>
    public static double SpectrogramBoostLabelReserve(double labelWidth) =>
        7 + Math.Max(0, labelWidth);

    public static double SpectrogramBoostBarLeft(double? labelWidth = null) =>
        labelWidth is { } width
            ? Math.Max(0, (DbScaleWidth - SpectrogramBoostLabelReserve(width) - SpectrogramBoostThumbSize) * 0.5)
            : Math.Max(0, (DbScaleWidth - SpectrogramBoostThumbSize) * 0.5);

    /// <summary>上段＋下段 MARK。ホスト上余白 2 + 各行ボタン。行間は 0。</summary>
    public static double TransportBarHeight =>
        From96(2) + TransportButtonSide * 2 + TransportRowGap;

    /// <summary>上段と下段（MARK / VIEW / HELP）のあいだ。</summary>
    public static double TransportRowGap => 0;

    public static Thickness TransportBottomRowMargin => new(0, TransportRowGap, 0, 0);

    /// <summary>TransportBar 左右マージン（8+8）。</summary>
    public static double TransportHostPaddingX => From96(16);

    /// <summary>グループ見出し（9pt Bold）の余裕込み。いちばん長い TRANS でも足りる。</summary>
    public static double TransportGroupLabelReserve => From96(40);

    public static double TransportGroupWidth(int buttonCount) =>
        TransportGroupLabelReserve
        + TransportGroupLabelGap
        + buttonCount * (TransportButtonSide + TransportButtonGap)
        + TransportGroupGap;

    /// <summary>上段: TRANS(2) EDIT(9) FILE(4)。VIEW は下段へ折り返す。</summary>
    public static double TransportTopRowWidth =>
        TransportHostPaddingX
        + TransportGroupWidth(2)
        + TransportGroupWidth(9)
        + TransportGroupWidth(4);

    /// <summary>下段: MARK(3) VIEW(6) HELP(3)。</summary>
    public static double TransportBottomRowWidth =>
        TransportHostPaddingX
        + TransportGroupWidth(3)
        + TransportGroupWidth(6)
        + TransportGroupWidth(3);

    public static double TransportFixedRowWidth =>
        Math.Max(TransportTopRowWidth, TransportBottomRowWidth);

    /// <summary>初期 214×32 の 2/3。トランスポート左下、ボタン列の下。</summary>
    public static double BrandLogoWidth => From96(214) * 2 / 3;

    public static double BrandLogoHeight => From96(32) * 2 / 3;

    public static double BrandLicenseFontSize => From96(8);

    public static double BrandLicenseLineBox
    {
        get
        {
            var spacing = SystemFonts.MessageFontFamily.LineSpacing;
            return BrandLicenseFontSize * (spacing > 0 ? spacing : 1.2);
        }
    }

    public static double BrandLicenseHeight =>
        BrandLicenseLineBox * BrandLicenseAlign.LineCount(UiStrings.CopyrightText);

    public static double BrandLogoTextGap => From96(8);

    public static double BrandRowPad => From96(6);

    public static double BrandRowHeight =>
        Math.Max(BrandLogoHeight, BrandLicenseHeight) + BrandRowPad;

    public static double TransportHostMinHeight => TransportBarHeight + BrandRowHeight;

    /// <summary>トランスポート右の履歴・ラウドネス・スペアナと、右端メーター列。</summary>
    public static double TransportSideChromeWidth =>
        HistoryStripWidth + From96(6)
        + LoudnessMeterWidth + From96(6)
        + SpectrumWidth + From96(1)
        + LevelMeterWidth;

    /// <summary>ウィンドウ枠（リサイズ辺）。</summary>
    public static double WindowFramePad => From96(24);

    public static double TransportButtonSide => Dip(45);

    public static double TransportWaapiButtonWidth => From96(56);

    public static double TransportButtonGap => Dip(2);

    public static double TransportGroupGap => Dip(10);

    public static double TransportGroupLabelFontSize => From96(9);

    public static double TransportGroupLabelGap => Dip(3);

    public static double TransportSpeakerComboWidth => From96(168);

    public static double StatusTimecodeFontSize => From96(13);

    public static double StatusTimecodePadX => From96(4);

    public static Thickness StatusTimecodePadding => new(StatusTimecodePadX, 0, StatusTimecodePadX, 0);

    /// <summary>時刻文字が切れず、左右余白で中央に見える幅。</summary>
    public static double StatusTimecodeWidth => From96(96);

    public static double StatusTimecodeBoxHeight => From96(22);

    public static double StatusTimecodeLabelWidth => From96(22);

    /// <summary>
    /// F12 アナライザー最大化でのメーター／スペアナ／ラウドネス／ゴニオの倍率。
    /// DIP のまま掛けるので、OS の DPI とアプリの表示倍率は別途乗る。
    /// </summary>
    public const double AnalyzerMaximizeScale = 1.5;

    /// <summary>F10 プレイヤーのジャケット枠。以前のリスト高さ追従の約 1/4 の 1.2 倍。</summary>
    public static double LibraryJacketSize => From96(96) * 1.2;

    /// <summary>F10 グループ見出し左のジャケット。</summary>
    public static double LibraryGroupJacketSize => From96(96);

    /// <summary>F10 ファイルリストの上下分割で、リスト側の最低高さ。</summary>
    public static double LibraryPaneMinHeight => From96(140);

    /// <summary>F10 左のフォルダツリー幅（既定）。</summary>
    public static double LibraryExplorerWidth => From96(220);

    /// <summary>F10 フォルダツリーの最小幅。</summary>
    public static double LibraryExplorerMinWidth => From96(120);

    /// <summary>F10 フォルダツリーの最大幅。</summary>
    public static double LibraryExplorerMaxWidth => From96(480);

    /// <summary>F10 フォルダツリーとリストの区切り。1px。</summary>
    public static double LibraryExplorerSplitterWidth => From96(1);

    public static double ClampLibraryExplorerWidth(double width)
    {
        if (width <= 0)
        {
            return LibraryExplorerWidth;
        }

        return Math.Clamp(width, LibraryExplorerMinWidth, LibraryExplorerMaxWidth);
    }

    /// <summary>F10 プレイヤーの波形エリア高さ（従来 200dip の約 2/3）。</summary>
    public static double LibraryWaveformHeight => From96(133);

    /// <summary>スペアナ全体（LED メーター + 下の周波数数値）。ゴニオ＋位相バーと同じ高さ。</summary>
    public static double SpectrumHeight => VectorScopeHeight;

    /// <summary>周波数数値の重なりを避けるため、右揃えのまま左へ足す幅。</summary>
    public static double SpectrumExtraWidth => From96(72);

    public static double SpectrumWidth =>
        LevelMeterWidth * SpectrumAnalyzer.OriginalAspect + SpectrumExtraWidth;

    /// <summary>ラウドネス左の履歴プレビュー幅。長い題は切る。</summary>
    public static double HistoryStripWidth => From96(264);

    /// <summary>履歴プレビュー1行。</summary>
    public static double HistoryStripRowHeight => From96(14);

    /// <summary>ラウドネスの Consolas 12 で約1文字。左右余白に使う。</summary>
    public static double LoudnessMeterCharWidth => From96(7);

    /// <summary>スペアナ左のラウドネスメーター幅。列はキャプション／値／単位の最大幅の合計。</summary>
    public static double LoudnessMeterWidth => From96(208);

    /// <summary>正方形ゴニオ直下の位相バー高さ。</summary>
    public static double VectorScopeCorrelationHeight => From96(18);

    /// <summary>正方形ゴニオ + 下の位相バー。トランスポート行をこれに揃える。</summary>
    public static double VectorScopeHeight => LevelMeterWidth + VectorScopeCorrelationHeight;

    public static GridLength VectorScopeHeightGrid => new(VectorScopeHeight);

    /// <summary>タブ＋ボタン2行＋ロゴ行。ゴニオより高いときはこちらに合わせる。</summary>
    public static double TransportChromeHeight =>
        Math.Max(VectorScopeHeight, DocumentTabBarHeight + TransportHostMinHeight);

    public static GridLength TransportChromeHeightGrid => new(TransportChromeHeight);

    public static double AudioInputHeight => Dip(30);

    /// <summary>1 列タブ（一般／編集／書き出し／Wwise）の本文幅。</summary>
    public static double SettingsPanelWidth => From96(560);

    /// <summary>設定ウィンドウの下限幅（タブ見出しが切れない程度）。</summary>
    public static double SettingsWindowMinWidth => From96(480);

    /// <summary>オーディオタブ内容の外側に足す余白。</summary>
    public static double SettingsWindowContentMargin => From96(16);

    /// <summary>設定ウィンドウの初期高さ。チャンネル数で中はスクロール。</summary>
    public static double SettingsWindowHeight => From96(600);

    public static double SettingsWindowMinHeight => From96(420);

    public static double SettingsWindowMaxHeight => From96(780);

    /// <summary>設定のラベルとコンボのあいだ。</summary>
    public static double SettingsLabelComboGap => From96(8);

    /// <summary>設定列の縦スクロールバー。スタイルと幅計算で共用。</summary>
    public static double SettingsScrollBarWidth => From96(10);

    public static GridLength SettingsScrollBarWidthGrid => new(SettingsScrollBarWidth);

    /// <summary>列のコンボ／メーターとスクロールバーのあいだ。</summary>
    public static double SettingsScrollBarGap => From96(8);

    /// <summary>オーディオタブの入力／出力列のすき間。</summary>
    public static double SettingsColumnGap => From96(20);

    public static GridLength SettingsColumnGapGrid => new(SettingsColumnGap);

    /// <summary>入力ルーティング行の横レベルバー。</summary>
    public static double SettingsLevelBarWidth => From96(88);

    /// <summary>再生ポート行の Sine −20 dB ボタン。</summary>
    public static double SettingsSineButtonWidth => From96(108);

    /// <summary>再生ポート行の Voice ボタン。</summary>
    public static double SettingsVoiceButtonWidth => From96(56);

    /// <summary>ポートコンボとテストボタンのあいだ。</summary>
    public static double SettingsTestButtonGap => From96(6);

    /// <summary>ファイル Ch コンボの下限幅（「Ch16」）。</summary>
    public static double SettingsFileLaneComboMinWidth => From96(72);

    /// <summary>設定のラウドネス値（-70.0）。</summary>
    public static double SettingsLoudnessBoxWidth => From96(72);

    public static double AudioDialogButtonWidth => Dip(162);

    public static double ConfirmSaveButtonWidth => From96(88);

    public static double SaveBatchButtonWidth => From96(200);

    public static double AudioDialogButtonHeight => Dip(48);

    public static Thickness AudioPad => new(Dip(18));

    public static double FadeOptionRowHeight => AudioInputHeight;

    /// <summary>目盛 22×2 + バー 14×4。枠なしの既定かつ最小幅。</summary>
    public static double LevelMeterWidth => From96(100);

    public static GridLength LevelMeterWidthGrid => new(LevelMeterWidth);

    /// <summary>バーが最大太さになる列幅（16ch）。チャンネル数が少ないとこれより狭い。</summary>
    public static double LevelMeterWidthMax =>
        LevelMeterSurroundLayout.FilledColumnWidth(ChannelLayout.MaxChannels);

    /// <summary>メーター列左端のドラッグ幅。</summary>
    public static double MeterColumnSplitterWidth => From96(4);

    public static double ClampMeterColumnWidth(double width) =>
        ClampMeterColumnWidth(width, ChannelLayout.MaxChannels);

    public static double ClampMeterColumnWidth(double width, int channels)
    {
        if (width <= 0)
        {
            return LevelMeterWidth;
        }

        return Math.Clamp(width, LevelMeterWidth, LevelMeterSurroundLayout.FilledColumnWidth(channels));
    }
}
