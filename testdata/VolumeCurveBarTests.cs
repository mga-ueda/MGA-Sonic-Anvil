using System.Runtime.ExceptionServices;
using System.Windows;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// V キーの音量カーブ編集モード用コントロール（ダイアログ廃止後の下部バー／波形オーバーレイ）。
/// </summary>
public sealed class VolumeCurveBarTests
{
    /// <summary>
    /// 1 テストにまとめている理由: <see cref="VolumeCurveBar"/> は共有の静的角丸
    /// <see cref="DesignMetrics"/> バインディングを使うため、STA スレッドを跨いで複数インスタンスを
    /// 作ると "DependencySource は同じ Thread 上で作成する必要がある" で失敗する。
    /// </summary>
    [Fact]
    public void DefaultState_ToolToggle_AndGainWrite()
    {
        StaTestRunner.Run(() =>
        {
            var bar = new VolumeCurveBar();
            Assert.Equal(Visibility.Collapsed, bar.Visibility);
            Assert.Equal(VolumeCurveBarTool.Points, bar.Tool);

            // Tool は MainWindow からの一方向同期用。プログラムから設定しても ToolChanged は
            // 再発火しない（ユーザーのラジオ操作だけが発火源）。
            VolumeCurveBarTool? raised = null;
            bar.ToolChanged += tool => raised = tool;
            bar.Tool = VolumeCurveBarTool.Brush;
            Assert.Null(raised);
            Assert.Equal(VolumeCurveBarTool.Brush, bar.Tool);
            // ブラシ時はカーブ形状アイコンをグレーアウト（IsEnabled + Opacity）。
            Assert.False(bar.IsShapeButtonsEnabled);
            Assert.True(bar.ShapeButtonsOpacity < 0.5);

            bar.Tool = VolumeCurveBarTool.Points;
            Assert.Null(raised);
            Assert.Equal(VolumeCurveBarTool.Points, bar.Tool);
            Assert.True(bar.IsShapeButtonsEnabled);
            Assert.Equal(1, bar.ShapeButtonsOpacity);

            // 内部 TextBox が未レイアウトでも書き込みが例外を投げないことだけ確認。
            bar.WriteGainDb(-3f);
            bar.WriteGainDb(0f);
            bar.WriteGainDb(6f);
        });
    }
}

public sealed class VolumeCurveEditLayerTests
{
    [Fact]
    public void DefaultState_IsCollapsedAndNotHitTestable()
    {
        StaTestRunner.Run(() =>
        {
            var layer = new VolumeCurveEditLayer();
            Assert.Equal(Visibility.Collapsed, layer.Visibility);
            Assert.False(layer.IsHitTestVisible);
            Assert.Equal(VolumeCurveEditLayer.EditTool.Points, layer.Tool);
            Assert.Equal(-1, layer.SelectedPointIndex);
        });
    }

    [Fact]
    public void SettingRangeAndCurve_DoesNotThrowWithoutAttachedWaveform()
    {
        StaTestRunner.Run(() =>
        {
            var layer = new VolumeCurveEditLayer
            {
                Range = new WaveSelection(0, 1000),
                Curve = Audio.VolumeCurve.Flat(-6),
                Tool = VolumeCurveEditLayer.EditTool.Brush,
            };

            Assert.Equal(VolumeCurveEditLayer.EditTool.Brush, layer.Tool);
            Assert.Null(layer.TargetWaveform);
        });
    }

    [Fact]
    public void TryAddPointsForSelection_AddsFourPointsForFullRangeSelection()
    {
        StaTestRunner.Run(() =>
        {
            var curve = Audio.VolumeCurve.Flat(0);
            Assert.Equal(2, curve.Points.Count);
            var layer = new VolumeCurveEditLayer
            {
                Range = new WaveSelection(0, 1000),
                Curve = curve,
                Tool = VolumeCurveEditLayer.EditTool.Points,
            };

            Assert.True(layer.TryAddPointsForSelection(new WaveSelection(0, 1000), out _));
            // 外側余地が無いときは両端＋内側退避の 4 点（端点とマージ）。
            Assert.Equal(4, curve.Points.Count);
        });
    }

    [Fact]
    public void TryAddPointsForSelection_AddsFourPointsForSubRange()
    {
        StaTestRunner.Run(() =>
        {
            var curve = Audio.VolumeCurve.Flat(0);
            var layer = new VolumeCurveEditLayer
            {
                Range = new WaveSelection(0, 10_000),
                Curve = curve,
                Tool = VolumeCurveEditLayer.EditTool.Points,
            };

            Assert.True(layer.TryAddPointsForSelection(new WaveSelection(2000, 5000), out _));
            Assert.Equal(6, curve.Points.Count); // 既存端点2 + 新規4

            var pad = VolumeCurveEditLayer.PlateauPadFrames(3000);
            var length = 10_000L - 1;
            double T(long frame) => frame / (double)length;
            // 内側は選択端、外側は pad 分前後。
            Assert.Contains(curve.Points, p => Math.Abs(p.T - T(2000 - pad)) < 1e-9);
            Assert.Contains(curve.Points, p => Math.Abs(p.T - T(2000)) < 1e-9);
            Assert.Contains(curve.Points, p => Math.Abs(p.T - T(4999)) < 1e-9);
            Assert.Contains(curve.Points, p => Math.Abs(p.T - T(4999 + pad)) < 1e-9);
        });
    }

    [Fact]
    public void TryAddPointsForSelection_SecondPlateauDoesNotStickToPrevious()
    {
        StaTestRunner.Run(() =>
        {
            var curve = Audio.VolumeCurve.Flat(0);
            var layer = new VolumeCurveEditLayer
            {
                Range = new WaveSelection(0, 20_000),
                Curve = curve,
                Tool = VolumeCurveEditLayer.EditTool.Points,
            };

            Assert.True(layer.TryAddPointsForSelection(new WaveSelection(2000, 5000), out _));
            Assert.True(layer.TryAddPointsForSelection(new WaveSelection(12_000, 16_000), out _));

            var length = 20_000L - 1;
            double T(long frame) => frame / (double)length;
            Assert.Contains(curve.Points, p => Math.Abs(p.T - T(12_000)) < 1e-9);
            Assert.Contains(curve.Points, p => Math.Abs(p.T - T(15_999)) < 1e-9);
            // 2 回目の台形区間はリニア。
            var iInner = -1;
            for (var i = 0; i < curve.Points.Count; i++)
            {
                if (Math.Abs(curve.Points[i].T - T(12_000)) < 1e-9)
                {
                    iInner = i;
                    break;
                }
            }

            Assert.True(iInner >= 0);
            Assert.Equal(FadeShape.Linear, curve.OutgoingShapeOfPoint(iInner));
        });
    }

    [Fact]
    public void TryAddPointsForSelection_OnFullDocumentRange_PlateauPointsAreNotEndpoints()
    {
        StaTestRunner.Run(() =>
        {
            var curve = Audio.VolumeCurve.Flat(0);
            var layer = new VolumeCurveEditLayer
            {
                Range = new WaveSelection(0, 20_000),
                Curve = curve,
                Tool = VolumeCurveEditLayer.EditTool.Points,
            };

            Assert.True(layer.TryAddPointsForSelection(new WaveSelection(4000, 8000), out _));
            Assert.Equal(6, curve.Points.Count);

            var pad = VolumeCurveEditLayer.PlateauPadFrames(4000);
            var length = 20_000L - 1;
            double T(long frame) => frame / (double)length;
            var plateauTs = new[]
            {
                T(4000 - pad),
                T(4000),
                T(7999),
                T(7999 + pad),
            };
            foreach (var t in plateauTs)
            {
                Assert.True(t > 1e-9 && t < 1d - 1e-9);
                Assert.Contains(curve.Points, p => Math.Abs(p.T - t) < 1e-9);
            }
        });
    }
}

/// <summary>WPF オブジェクトは STA スレッドでないと構築できないため、テストごとに専用スレッドで実行する。</summary>
internal static class StaTestRunner
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                EnsureThemeResources();
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>
    /// <c>FadeCurveIcons</c>／<c>ActionButtonLooks</c> 等が <c>Theme.Get</c> 経由で参照するブラシ。
    /// テストホストには App.xaml が読み込まれないため、実アプリと同じテーマ辞書を直接マージする
    /// （他の WPF コントロールテストと同じ手法。<see cref="TextBoxContextMenuTests"/> 参照）。
    /// </summary>
    private static void EnsureThemeResources()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        var resources = Application.Current!.Resources;
        // 色キー追加後も古いマージ辞書のまま早期 return しない。
        if (resources["ChromeBorderBrush"] is not null
            && resources["VolumeCurveLineBrush"] is not null
            && resources["WavePreviewClipBrush"] is not null)
        {
            return;
        }

        var assembly = Uri.EscapeDataString(typeof(VolumeCurveBar).Assembly.GetName().Name!);
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assembly};component/Themes/UiColors.xaml", UriKind.Absolute),
        });
        if (resources["DarkTextBoxStyle"] is null)
        {
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/{assembly};component/Themes/Controls.xaml",
                    UriKind.Absolute),
            });
        }
    }
}
