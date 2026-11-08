using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ControlCornerRadiusSettingsTests
{
    [Fact]
    public void Settings_DefaultIsFiveAndRoundTrips()
    {
        Assert.Equal(AppSettings.ControlCornerRadiusDefault, new AppSettings().ControlCornerRadius);
        Assert.Equal(5, AppSettings.CreateDefault().ResolvedControlCornerRadius());
        Assert.Equal(0, AppSettings.ClampControlCornerRadius(-1));
        Assert.Equal(16, AppSettings.ClampControlCornerRadius(40));
        Assert.Equal(AppSettings.ControlCornerRadiusDefault, AppSettings.ClampControlCornerRadius(double.NaN));
        Assert.Equal(6, AppSettings.NudgeControlCornerRadius(5, 1));
        Assert.Equal(4, AppSettings.NudgeControlCornerRadius(5, -1));
        Assert.Equal(0, AppSettings.NudgeControlCornerRadius(0, -1));
        Assert.Equal(16, AppSettings.NudgeControlCornerRadius(16, 1));

        var json = JsonSerializer.Serialize(
            new AppSettings { ControlCornerRadius = 8 },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(8, back!.ResolvedControlCornerRadius());

        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(AppSettings.ControlCornerRadiusDefault, missing!.ResolvedControlCornerRadius());
    }

    [Fact]
    public void CornerBinding_FollowsTheSingleRadius()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                var previous = DesignMetrics.ControlCornerRadius;
                try
                {
                    var border = new Border();
                    DesignMetrics.BindCorner(border);
                    DesignMetrics.ApplyControlCornerRadius(9);
                    Assert.Equal(9, border.CornerRadius.TopLeft);
                    Assert.Equal(9, border.CornerRadius.BottomRight);
                    var button = new RoundedButton();
                    Assert.Equal(9, button.CornerRadius);

                    var file = new LibraryFileRow { Name = "a.wav" };
                    file.IsPlaylistSelected = true;
                    file.SelectionBandEdge = SelectionBandEdge.Solo;
                    var row = new DataGridRow
                    {
                        DataContext = file,
                        Template = LibraryBrowserView.PlaylistRowTemplate(
                            new SolidColorBrush(Colors.Cyan),
                            new SolidColorBrush(Colors.Gray)),
                    };
                    var window = new Window
                    {
                        Content = row,
                        Width = 240,
                        Height = 48,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.ToolWindow,
                    };
                    window.Show();
                    row.ApplyTemplate();
                    row.UpdateLayout();
                    var band = FindNamedBorder(row, LibraryBrowserView.PlaylistRowBandName);
                    Assert.NotNull(band);
                    Assert.Equal(9, band!.CornerRadius.TopLeft);
                    Assert.Equal(9, band.CornerRadius.BottomRight);

                    file.SelectionBandEdge = SelectionBandEdge.Middle;
                    Assert.Equal(0, band.CornerRadius.TopLeft);
                    Assert.Equal(0, band.CornerRadius.BottomRight);

                    file.SelectionBandEdge = SelectionBandEdge.First;
                    Assert.Equal(9, band.CornerRadius.TopLeft);
                    Assert.Equal(0, band.CornerRadius.BottomLeft);

                    DesignMetrics.ApplyControlCornerRadius(AppSettings.ControlCornerRadiusDefault);
                    Assert.Equal(AppSettings.ControlCornerRadiusDefault, border.CornerRadius.TopLeft);
                    Assert.Equal(AppSettings.ControlCornerRadiusDefault, button.CornerRadius);
                    Assert.Equal(AppSettings.ControlCornerRadiusDefault, band.CornerRadius.TopLeft);
                    Assert.Equal(0, band.CornerRadius.BottomLeft);
                    window.Close();
                }
                finally
                {
                    DesignMetrics.ApplyControlCornerRadius(previous);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error?.ToString());
    }

    private static Border? FindNamedBorder(DependencyObject root, string name)
    {
        if (root is Border { Name: { } borderName } border && borderName == name)
        {
            return border;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindNamedBorder(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
