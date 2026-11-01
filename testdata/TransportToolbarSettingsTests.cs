using System.Text.Json;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportToolbarSettingsTests
{
    [Fact]
    public void Settings_DefaultShowsToolbarAndAllIcons()
    {
        var settings = new AppSettings();
        Assert.True(settings.ShowTransportToolbar);
        Assert.Empty(settings.HiddenTransportToolbarCommands);
        Assert.Empty(settings.TransportToolbarLayout);
        Assert.True(AppSettings.CreateDefault().ShowTransportToolbar);
        foreach (var item in TransportToolbarCatalog.All)
        {
            Assert.Equal(
                item.InDefaultLayout,
                TransportToolbarCatalog.IsCommandVisible(settings, item.Command));
        }
    }

    [Fact]
    public void BuildAutoLayout_OrdersByCatalogAndInsertsGroupSeparators()
    {
        var layout = TransportToolbarCatalog.BuildAutoLayout(
        [
            nameof(TransportCommand.Open),
            nameof(TransportCommand.TogglePlayback),
            nameof(TransportCommand.Delete),
        ]);
        Assert.Equal(
            [
                nameof(TransportCommand.TogglePlayback),
                TransportToolbarCatalog.SeparatorToken,
                nameof(TransportCommand.Delete),
                TransportToolbarCatalog.SeparatorToken,
                nameof(TransportCommand.Open),
            ],
            layout);
        Assert.Equal(
            TransportToolbarCatalog.CreateDefaultLayout(),
            TransportToolbarCatalog.BuildAutoLayout(TransportToolbarCatalog.CreateDefaultLayout()));
    }

    [Fact]
    public void ResolveLayout_AlwaysRebuildsToCatalogOrder()
    {
        var resolved = TransportToolbarCatalog.ResolveLayout(
            [
                nameof(TransportCommand.Open),
                nameof(TransportCommand.TogglePlayback),
            ],
            hidden: null);
        Assert.Equal(
            [
                nameof(TransportCommand.TogglePlayback),
                TransportToolbarCatalog.SeparatorToken,
                nameof(TransportCommand.Open),
            ],
            resolved);
    }

    [Fact]
    public void Settings_HiddenCommandsRoundTrip()
    {
        var json = JsonSerializer.Serialize(
            new AppSettings
            {
                ShowTransportToolbar = false,
                HiddenTransportToolbarCommands = ["TogglePlayback", "Record"],
            },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.False(back!.ShowTransportToolbar);
        Assert.Equal(["TogglePlayback", "Record"], back.HiddenTransportToolbarCommands);
        Assert.False(TransportToolbarCatalog.IsCommandVisible(back, TransportCommand.TogglePlayback));
        Assert.True(TransportToolbarCatalog.IsCommandVisible(back, TransportCommand.Open));
    }

    [Fact]
    public void Settings_LayoutRoundTrip()
    {
        var layout = new[]
        {
            "TogglePlayback",
            TransportToolbarCatalog.SeparatorToken,
            "Open",
            "Save",
        };
        var json = JsonSerializer.Serialize(
            new AppSettings
            {
                TransportToolbarLayout = layout,
                HiddenTransportToolbarCommands = ["Record"],
            },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(layout, back!.TransportToolbarLayout);
        Assert.True(TransportToolbarCatalog.IsCommandVisible(back, TransportCommand.TogglePlayback));
        Assert.False(TransportToolbarCatalog.IsCommandVisible(back, TransportCommand.Record));
        Assert.True(TransportToolbarCatalog.IsCommandVisible(back, TransportCommand.Open));
    }

    [Fact]
    public void NormalizeHidden_DropsUnknownAndDuplicates()
    {
        var normalized = TransportToolbarCatalog.NormalizeHidden(
            ["record", "Record", "NotACommand", " ", "Open"]);
        Assert.Equal(["Record", "Open"], normalized);
    }

    [Fact]
    public void CreateDefaultLayout_InsertsGroupSeparators()
    {
        var layout = TransportToolbarCatalog.CreateDefaultLayout();
        Assert.Contains(TransportToolbarCatalog.SeparatorToken, layout);
        Assert.Equal("TogglePlayback", layout[0]);
        Assert.False(TransportToolbarCatalog.IsSeparator(layout[0]));
        Assert.False(TransportToolbarCatalog.IsSeparator(layout[^1]));
        // 既定掲載があるカテゴリ境界（FMT は既定なし、VIEW/OTHER は同一見出し）
        Assert.True(layout.Count(TransportToolbarCatalog.IsSeparator) >= 4);
        Assert.DoesNotContain("Stop", layout);
        Assert.DoesNotContain("Undo", layout);
        Assert.DoesNotContain("LoopPlay", layout);
        // Stop は再生トグルで兼ねるためカタログ非掲載
        Assert.Contains("TogglePlayback", layout);
        Assert.Contains("ExportWave", layout);
        Assert.Contains("CloseTab", layout);
        Assert.Contains("ToggleMinimalChrome", layout);
        Assert.Contains("ToggleLibraryMaximize", layout);
        Assert.Contains("MaximizeWaveform", layout);
        Assert.Contains("ToggleAnalyzerMaximize", layout);
        Assert.Contains("OpenSettings", layout);
    }

    [Fact]
    public void Catalog_IncludesOptionalCommandsForCustomize()
    {
        Assert.DoesNotContain(TransportCommand.Stop, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.Undo, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.Cut, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.SilenceSelection, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.CrossfadeEdit, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.SampleRate, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.ExportWave, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.CloseTab, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.MaximizeWaveform, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.SelectToStart, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.LoopPlay, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.ExportByMarkers, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.CopyFileName, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.MergeTabs, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.ExportByChannels, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.DeleteAllMarkers, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.PreviousTrack, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.NextTrack, TransportToolbarCatalog.AllCommands);
        Assert.Contains(TransportCommand.CopyAllTabTimes, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.TimeZoomIn, TransportToolbarCatalog.AllCommands);
        // メニューのチェックボックストグルは非掲載
        Assert.DoesNotContain(TransportCommand.ToggleRangeClick, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.AlwaysOnTop, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.ToggleWaapi, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.ViewWaveform, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.TileOff, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.TileHorizontal, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.TileVertical, TransportToolbarCatalog.AllCommands);
        Assert.DoesNotContain(TransportCommand.TileGrid, TransportToolbarCatalog.AllCommands);
        Assert.True(TransportToolbarCatalog.TryGetItem(TransportCommand.Cut, out var cut));
        Assert.False(cut.InDefaultLayout);
        Assert.Equal(TransportIcon.Cut, cut.Icon);
    }

    [Fact]
    public void ToolbarLabels_MatchMenuLabelsWithoutAccessKeys()
    {
        Assert.Equal("切り取り", UiStrings.WithoutAccessKey("切り取り(_T)"));
        Assert.Equal("Cut", UiStrings.WithoutAccessKey("Cu_t"));
        Assert.Equal("Select All", UiStrings.WithoutAccessKey("Select _All"));

        Assert.Equal(UiStrings.WithoutAccessKey(UiStrings.WaveMenuCut), UiStrings.LabelToolbarCut);
        Assert.Equal(UiStrings.WithoutAccessKey(UiStrings.WaveMenuSave), UiStrings.LabelToolbarSave);
        Assert.Equal(UiStrings.WithoutAccessKey(UiStrings.WaveMenuExportMp3), UiStrings.LabelToolbarSaveMp3);
        Assert.Equal(UiStrings.WithoutAccessKey(UiStrings.WaveMenuFadeAround), UiStrings.LabelToolbarFadeAround);
        Assert.Equal(UiStrings.LabelLayoutPresetF9, UiStrings.LabelToolbarMinimalChrome);
        Assert.Equal(UiStrings.LabelLayoutPresetF11, UiStrings.LabelToolbarWaveformMaximize);
        Assert.Equal(UiStrings.LabelLayoutPresetF10, UiStrings.LabelToolbarLibraryMaximize);
        Assert.Equal(UiStrings.LabelLayoutPresetF12, UiStrings.LabelToolbarAnalyzerMaximize);
        Assert.Equal(UiStrings.WithoutAccessKey(UiStrings.TabMenuCloseThis), UiStrings.LabelToolbarCloseTab);
    }

    [Fact]
    public void Describe_AppendsModeLimitationNotes()
    {
        Assert.True(TransportToolbarCatalog.TryGetItem(TransportCommand.PreviousTrack, out var previous));
        Assert.Contains(
            UiStrings.NoteToolbarPlayerMode,
            TransportToolbarCatalog.Describe(previous),
            StringComparison.Ordinal);

        Assert.True(TransportToolbarCatalog.TryGetItem(TransportCommand.Delete, out var delete));
        Assert.Contains(
            UiStrings.NoteToolbarEditorModeOnly,
            TransportToolbarCatalog.Describe(delete),
            StringComparison.Ordinal);

        Assert.True(TransportToolbarCatalog.TryGetItem(TransportCommand.ToggleMinimalChrome, out var f9));
        Assert.Contains(
            UiStrings.NoteToolbarLayoutModeSwitch,
            TransportToolbarCatalog.Describe(f9),
            StringComparison.Ordinal);

        Assert.True(TransportToolbarCatalog.TryGetItem(TransportCommand.TogglePlayback, out var play));
        Assert.DoesNotContain(
            UiStrings.NoteToolbarEditorModeOnly,
            TransportToolbarCatalog.Describe(play),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(TransportCommand.TogglePlayback))]
    [InlineData(nameof(TransportCommand.Record))]
    public void IconVariants_ListEveryStateStartingWithCatalogIcon(string commandName)
    {
        Assert.True(TransportToolbarCatalog.TryGetItem(commandName, out var item));
        var command = item.Command;
        var variants = TransportToolbarCatalog.IconVariants(command);

        Assert.True(variants.Count >= 2);
        Assert.Equal(item.Icon, variants[0].Icon);
        Assert.False(variants[0].Playing || variants[0].Latched);
        Assert.Equal(variants.Count, variants.Select(v => (v.Icon, v.Playing, v.Latched)).Distinct().Count());
        Assert.All(variants, v => Assert.False(string.IsNullOrWhiteSpace(v.StateText)));
    }

    [Fact]
    public void ApplyLayoutFromSettings_KeepsButtonsWhenLayoutUnchanged()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                }

                var assembly = Uri.EscapeDataString(typeof(TransportBar).Assembly.GetName().Name!);
                System.Windows.Application.Current!.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/{assembly};component/Themes/UiColors.xaml", UriKind.Absolute),
                });

                var bar = new TransportBar();
                var settings = new AppSettings();
                bar.ApplyLayoutFromSettings(settings);
                var host = bar.ItemsHost;
                Assert.NotEmpty(host.Children);

                // 組み直すと Clear で消える目印。
                var sentinel = new System.Windows.Controls.Canvas();
                host.Children.Add(sentinel);
                bar.ApplyLayoutFromSettings(settings);
                Assert.Contains(sentinel, host.Children.Cast<object>());

                settings.TransportToolbarLayout = [nameof(TransportCommand.Cut)];
                bar.ApplyLayoutFromSettings(settings);
                Assert.DoesNotContain(sentinel, host.Children.Cast<object>());
                Assert.Single(host.Children);
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
            throw new Exception(error.ToString());
        }
    }

    [Fact]
    public void IconVariants_EmptyForSingleIconCommands()
    {
        Assert.Empty(TransportToolbarCatalog.IconVariants(TransportCommand.Cut));
        Assert.Empty(TransportToolbarCatalog.IconVariants(TransportCommand.ToggleSpectrogram));
        Assert.Empty(TransportToolbarCatalog.IconVariants(TransportCommand.ToggleLibraryMaximize));
        Assert.Empty(TransportToolbarCatalog.IconVariants(TransportCommand.MaximizeWaveform));
        Assert.Empty(TransportToolbarCatalog.IconVariants(TransportCommand.ToggleAnalyzerMaximize));
        Assert.Empty(TransportToolbarCatalog.IconVariants(TransportCommand.ToggleMinimalChrome));
    }

    [Fact]
    public void NormalizeLayout_DropsUnknownDuplicatesAndEdgeSeparators()
    {
        var normalized = TransportToolbarCatalog.NormalizeLayout(
        [
            TransportToolbarCatalog.SeparatorToken,
            "TogglePlayback",
            "toggleplayback",
            "NotACommand",
            TransportToolbarCatalog.SeparatorToken,
            TransportToolbarCatalog.SeparatorToken,
            "Open",
            TransportToolbarCatalog.SeparatorToken,
        ]);
        Assert.Equal(
            ["TogglePlayback", TransportToolbarCatalog.SeparatorToken, "Open"],
            normalized);
    }

    [Fact]
    public void ResolveLayout_MigratesHiddenWhenLayoutEmpty()
    {
        var resolved = TransportToolbarCatalog.ResolveLayout(
            layout: null,
            hidden: ["Record", "Open"]);
        Assert.DoesNotContain("Record", resolved);
        Assert.DoesNotContain("Open", resolved);
        Assert.Contains("TogglePlayback", resolved);
        Assert.Contains(TransportToolbarCatalog.SeparatorToken, resolved);
        Assert.False(TransportToolbarCatalog.IsSeparator(resolved[0]));
        Assert.False(TransportToolbarCatalog.IsSeparator(resolved[^1]));
    }

    [Fact]
    public void ResolveLayout_IgnoresStoredOrderAndKeepsShownCommands()
    {
        // 保存並び・セパレータは捨て、Layout に載っているコマンドだけをカタログ順へ組み直す。
        // Layout があるとき Hidden は使わない（表示セットの正本は Layout）。
        var resolved = TransportToolbarCatalog.ResolveLayout(
            ["Save", TransportToolbarCatalog.SeparatorToken, "Open", nameof(TransportCommand.TogglePlayback)],
            hidden: ["Open"]);
        Assert.Equal(
            [
                nameof(TransportCommand.TogglePlayback),
                TransportToolbarCatalog.SeparatorToken,
                "Open",
                "Save",
            ],
            resolved);
    }

    [Fact]
    public void ApplyLayout_SyncsHidden()
    {
        var applied = TransportToolbarCatalog.ApplyLayout(
            ["TogglePlayback", TransportToolbarCatalog.SeparatorToken, "Open"]);
        Assert.Equal(
            ["TogglePlayback", TransportToolbarCatalog.SeparatorToken, "Open"],
            applied.Layout);
        Assert.Contains("Record", applied.Hidden);
        Assert.DoesNotContain("TogglePlayback", applied.Hidden);
        Assert.DoesNotContain("Open", applied.Hidden);
    }
}
