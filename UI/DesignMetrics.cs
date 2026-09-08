using System.Windows;

namespace MgaSonicAnvil.UI;

internal static class DesignMetrics
{
    public const double DesignDpi = 144d;
    public const double DipDpi = 96d;

    public static double Dip(double designPxAt144) => designPxAt144 * DipDpi / DesignDpi;

    public static double From96(double value96) => value96;

    public static double ProjectBarHeight => From96(30);

    public static double ActionBarHeight => From96(44);

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

    public static double TransportPadX => Dip(12);

    public static double TransportCurrentTimeWidth => Dip(168);

    public static double ToolbarButtonSide => From96(24);

    /// <summary>スペアナ全体（LED メーター + 下の周波数数値）。ゴニオ＋位相バーと同じ高さ。</summary>
    public static double SpectrumHeight => VectorScopeHeight;

    /// <summary>周波数数値の重なりを避けるため、右揃えのまま左へ足す幅。</summary>
    public static double SpectrumExtraWidth => From96(80);

    /// <summary>dB 数値がバーに被らないよう、左目盛へ足す幅。</summary>
    public static double SpectrumPadLeftExtra => From96(8);

    /// <summary>正方形ゴニオ直下の位相バー高さ。</summary>
    public static double VectorScopeCorrelationHeight => From96(18);

    /// <summary>正方形ゴニオ + 下の位相バー。トランスポート行をこれに揃える。</summary>
    public static double VectorScopeHeight => LevelMeterWidth + VectorScopeCorrelationHeight;

    public static GridLength VectorScopeHeightGrid => new(VectorScopeHeight);

    public static double ActionButtonWidth => From96(108);

    public static double ActionButtonHeight => From96(32);

    public static double AudioInputHeight => Dip(30);

    public static GridLength AudioInputHeightGrid => new(AudioInputHeight);

    public static double AudioDialogButtonWidth => Dip(162);

    public static double AudioDialogButtonHeight => Dip(48);

    public static Thickness AudioPad => new(Dip(18));

    /// <summary>目盛 22×2 + バー 14×4。枠なしの最小幅。</summary>
    public static double LevelMeterWidth => From96(100);

    public static GridLength LevelMeterWidthGrid => new(LevelMeterWidth);
}
