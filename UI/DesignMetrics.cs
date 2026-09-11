using System.Windows;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

internal static class DesignMetrics
{
    public const double DesignDpi = 144d;
    public const double DipDpi = 96d;

    public static double Dip(double designPxAt144) => designPxAt144 * DipDpi / DesignDpi;

    public static double From96(double value96) => value96;

    public static double WindowMinWidth => From96(1240);

    public static double WindowMinHeight => From96(420);

    public static double WindowDefaultWidth => From96(1240);

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

    public static double WaveformScrollBarHeight => From96(12);

    public static GridLength WaveformScrollBarHeightGrid => new(WaveformScrollBarHeight);

    public static double TransportBarHeight => Dip(54);

    public static double TransportButtonSide => Dip(45);

    public static double TransportWaapiButtonWidth => From96(56);

    public static double TransportButtonGap => Dip(2);

    public static double TransportGroupGap => Dip(6);

    public static double TransportSpeakerComboWidth => From96(168);

    public static double StatusTimecodeWidth => From96(86);

    public static double StatusTimecodeLabelWidth => From96(22);

    public static double ToolbarButtonSide => From96(24);

    /// <summary>スペアナ全体（LED メーター + 下の周波数数値）。ゴニオ＋位相バーと同じ高さ。</summary>
    public static double SpectrumHeight => VectorScopeHeight;

    /// <summary>周波数数値の重なりを避けるため、右揃えのまま左へ足す幅。</summary>
    public static double SpectrumExtraWidth => From96(72);

    /// <summary>ラウドネス左の履歴プレビュー幅。長い題は切る。</summary>
    public static double HistoryStripWidth => From96(176);

    /// <summary>履歴プレビュー1行。</summary>
    public static double HistoryStripRowHeight => From96(14);

    /// <summary>スペアナ左のラウドネスメーター幅。縦積み1列。</summary>
    public static double LoudnessMeterWidth => From96(232);

    /// <summary>正方形ゴニオ直下の位相バー高さ。</summary>
    public static double VectorScopeCorrelationHeight => From96(18);

    /// <summary>正方形ゴニオ + 下の位相バー。トランスポート行をこれに揃える。</summary>
    public static double VectorScopeHeight => LevelMeterWidth + VectorScopeCorrelationHeight;

    public static GridLength VectorScopeHeightGrid => new(VectorScopeHeight);

    public static double AudioInputHeight => Dip(30);

    /// <summary>1 列タブ（一般／編集／書き出し）の本文幅。</summary>
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
