using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TileSearchBlurLayoutTests
{
    [Fact]
    public void BodyMargin_StaysZeroSoHitAndMissTilesAlign()
    {
        Assert.Equal(new Thickness(0), TileSearchBlurLayout.BodyMargin);
    }

    [Fact]
    public void ClipLayersToBounds_OnlyMatchedTiles()
    {
        Assert.False(TileSearchBlurLayout.ClipLayersToBounds(veiled: true));
        Assert.True(TileSearchBlurLayout.ClipLayersToBounds(veiled: false));
    }

    [Fact]
    public void HitAndMissTiles_KeepHeaderAndWaveformAligned()
    {
        RunSta(() =>
        {
            var pair = LayoutPair(TileSearchBlurLayout.BodyMargin);
            AssertEqualRect(pair.HitHeader, pair.MissHeader);
            AssertEqualRect(pair.HitWave, pair.MissWave);
        });
    }

    [Fact]
    public void NegativeOverscanMargin_ShiftsMissTileOffHitTile()
    {
        RunSta(() =>
        {
            var pair = LayoutPair(new Thickness(-TileSearchBlurLayout.Radius));
            Assert.NotEqual(pair.HitHeader.Y, pair.MissHeader.Y);
            Assert.NotEqual(pair.HitWave.Y, pair.MissWave.Y);
            Assert.NotEqual(pair.HitWave.Height, pair.MissWave.Height);
        });
    }

    private static void AssertEqualRect(Rect a, Rect b)
    {
        Assert.Equal(a.Y, b.Y, 3);
        Assert.Equal(a.Height, b.Height, 3);
        Assert.Equal(a.Width, b.Width, 3);
    }

    private static (Rect HitHeader, Rect HitWave, Rect MissHeader, Rect MissWave) LayoutPair(Thickness missBodyMargin)
    {
        var grid = new Grid { Width = 400, Height = 200 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var hit = CreatePane(new Thickness(0), clipLayers: true);
        var miss = CreatePane(missBodyMargin, clipLayers: false);
        Grid.SetColumn(hit.Host, 0);
        Grid.SetColumn(miss.Host, 1);
        grid.Children.Add(hit.Host);
        grid.Children.Add(miss.Host);

        grid.Measure(new Size(400, 200));
        grid.Arrange(new Rect(0, 0, 400, 200));
        grid.UpdateLayout();

        return (
            BoundsInGrid(hit.Header, grid),
            BoundsInGrid(hit.Wave, grid),
            BoundsInGrid(miss.Header, grid),
            BoundsInGrid(miss.Wave, grid));
    }

    private static (Border Host, Border Header, Border Wave) CreatePane(Thickness bodyMargin, bool clipLayers)
    {
        var header = new Border { Height = 24, Background = Brushes.Gray };
        var wave = new Border { Background = Brushes.Black };
        var body = new DockPanel { Margin = bodyMargin };
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(wave);
        var layers = new Grid { ClipToBounds = clipLayers };
        layers.Children.Add(body);
        var host = new Border { Child = layers, ClipToBounds = false };
        return (host, header, wave);
    }

    private static Rect BoundsInGrid(FrameworkElement element, Visual grid)
    {
        var origin = element.TransformToAncestor(grid).Transform(new Point(0, 0));
        return new Rect(origin, element.RenderSize);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
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
}
