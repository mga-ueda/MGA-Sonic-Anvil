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
            Assert.True(TransportToolbarCatalog.IsCommandVisible(settings, item.Command));
        }
    }

    [Fact]
    public void BuildAutoLayout_OrdersByMenuWithPlaybackFirst()
    {
        var layout = TransportToolbarCatalog.BuildAutoLayout(
        [
            nameof(TransportCommand.Delete),
            nameof(TransportCommand.Open),
            nameof(TransportCommand.TogglePlayback),
        ]);
        Assert.Equal(
            [
                nameof(TransportCommand.TogglePlayback),
                TransportToolbarCatalog.SeparatorToken,
                nameof(TransportCommand.Open),
                TransportToolbarCatalog.SeparatorToken,
                nameof(TransportCommand.Delete),
            ],
            layout);
        Assert.Equal(
            TransportToolbarCatalog.CreateDefaultLayout(),
            TransportToolbarCatalog.BuildAutoLayout(TransportToolbarCatalog.CreateDefaultLayout()));
    }

    [Fact]
    public void Catalog_EveryCommandMapsToOneMenuItem()
    {
        var missing = TransportToolbarCatalog.All
            .Where(item => TransportToolbarCatalog.PlacementOf(item) is null)
            .Select(item => $"{item.Command}→{item.MenuCommand}")
            .ToArray();
        Assert.True(missing.Length == 0, "上部メニューに無い: " + string.Join(", ", missing));

        var duplicated = TransportToolbarCatalog.All
            .GroupBy(item => item.MenuCommand)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .ToArray();
        Assert.True(duplicated.Length == 0, "同じメニュー項目に複数ボタン: " + string.Join(", ", duplicated));
    }

    [Fact]
    public void Ordered_FollowsMenuCategoriesAndOrder()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var menu = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel(), includeHere: false)
                .OfType<WaveMenuItemEntry>()
                .ToArray();
            string CategoryOf(WaveMenuCommand command) => UiStrings.WithoutAccessKey(
                menu.First(category => WaveformContextMenuBuilder.Commands(category.Children ?? []).Contains(command)).Header);

            var ordered = TransportToolbarCatalog.OrderedWithGroupTitles();
            foreach (var (item, title) in ordered)
            {
                Assert.Equal(CategoryOf(item.MenuCommand), title);
            }

            // 再生カテゴリが先頭、以降はメニューのカテゴリ順で、各カテゴリは一続き。
            var titles = ordered.Select(pair => pair.GroupTitle).Distinct().ToArray();
            Assert.Equal(UiStrings.WithoutAccessKey(UiStrings.WaveMenuCatPlayback), titles[0]);
            var menuTitles = menu.Select(category => UiStrings.WithoutAccessKey(category.Header))
                .Where(title => titles.Contains(title) && title != titles[0])
                .ToArray();
            Assert.Equal(menuTitles, titles.Skip(1));
            Assert.Equal(titles.Length, ordered.Select(pair => pair.GroupTitle)
                .Where((title, i) => i == 0 || title != ordered[i - 1].GroupTitle).Count());

            var menuOrder = WaveformContextMenuBuilder.Commands(menu).ToList();
            foreach (var group in ordered.GroupBy(pair => pair.GroupTitle))
            {
                var indexes = group.Select(pair => menuOrder.IndexOf(pair.Item.MenuCommand)).ToArray();
                Assert.Equal(indexes.Order(), indexes);
            }
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void BuildAutoLayout_SplitsSectionsLikeMenuSeparatorsAndSubmenus()
    {
        var layout = TransportToolbarCatalog.BuildAutoLayout(
        [
            nameof(TransportCommand.TogglePlayback),
            nameof(TransportCommand.LoopPlay),
            nameof(TransportCommand.GoToStart),
            nameof(TransportCommand.GoToEnd),
            nameof(TransportCommand.Undo),
            nameof(TransportCommand.Redo),
            nameof(TransportCommand.Cut),
        ]);
        Assert.Equal(
            [
                // 再生：同じかたまり → シーク サブメニュー
                nameof(TransportCommand.TogglePlayback),
                nameof(TransportCommand.LoopPlay),
                TransportToolbarCatalog.SectionSeparatorToken,
                nameof(TransportCommand.GoToStart),
                nameof(TransportCommand.GoToEnd),
                TransportToolbarCatalog.SeparatorToken,
                // 編集：元に戻す系 → セパレータ → クリップボード系
                nameof(TransportCommand.Undo),
                nameof(TransportCommand.Redo),
                TransportToolbarCatalog.SectionSeparatorToken,
                nameof(TransportCommand.Cut),
            ],
            layout);
    }

    [Fact]
    public void NormalizeLayout_KeepsSectionSeparatorAndPrefersGroupBoundary()
    {
        var normalized = TransportToolbarCatalog.NormalizeLayout(
        [
            "TogglePlayback",
            TransportToolbarCatalog.SectionSeparatorToken,
            "GoToStart",
            TransportToolbarCatalog.SectionSeparatorToken,
            TransportToolbarCatalog.SeparatorToken,
            "Open",
        ]);
        Assert.Equal(
            [
                "TogglePlayback",
                TransportToolbarCatalog.SectionSeparatorToken,
                "GoToStart",
                TransportToolbarCatalog.SeparatorToken,
                "Open",
            ],
            normalized);
    }

    [Fact]
    public void ResolveLayout_AlwaysRebuildsToCatalogOrder()
    {
        var layout = new[]
        {
            nameof(TransportCommand.Open),
            nameof(TransportCommand.TogglePlayback),
        };
        // Hidden を Layout と同期した状態（カスタマイズ保存後と同じ）。
        var hidden = TransportToolbarCatalog.HiddenFromLayout(layout);
        var resolved = TransportToolbarCatalog.ResolveLayout(layout, hidden);
        Assert.Equal(
            [
                nameof(TransportCommand.TogglePlayback),
                TransportToolbarCatalog.SeparatorToken,
                nameof(TransportCommand.Open),
            ],
            resolved);
    }

    [Fact]
    public void ResolveLayout_ShowsNewCatalogCommandsByDefault()
    {
        // 古い Layout／Hidden に無い新規コマンドは、表示が既定なので足す。
        var legacyShown = TransportToolbarCatalog.AllCommands
            .Where(command => command is not TransportCommand.Bounce)
            .Select(command => command.ToString());
        var legacyLayout = TransportToolbarCatalog.BuildAutoLayout(legacyShown);
        // 追加前に同期された Hidden には Bounce がまだ無い（現行 HiddenFromLayout だと入ってしまう）。
        var legacyHidden = TransportToolbarCatalog.HiddenFromLayout(legacyLayout)
            .Where(name => name is not nameof(TransportCommand.Bounce))
            .ToArray();
        Assert.DoesNotContain(nameof(TransportCommand.Bounce), legacyHidden);

        var resolved = TransportToolbarCatalog.ResolveLayout(legacyLayout, legacyHidden);
        Assert.Contains(nameof(TransportCommand.Bounce), resolved);
    }

    [Fact]
    public void ResolveLayout_KeepsExplicitlyHiddenCommandsHidden()
    {
        var layout = TransportToolbarCatalog.CreateDefaultLayout()
            .Where(entry => entry is not nameof(TransportCommand.Bounce))
            .ToArray();
        var hidden = TransportToolbarCatalog.HiddenFromLayout(layout);
        Assert.Contains(nameof(TransportCommand.Bounce), hidden);

        var resolved = TransportToolbarCatalog.ResolveLayout(layout, hidden);
        Assert.DoesNotContain(nameof(TransportCommand.Bounce), resolved);
    }

    [Fact]
    public void NormalizeLayout_MapsLegacyBounceNamesToBounce()
    {
        var normalized = TransportToolbarCatalog.NormalizeLayout(
            ["BounceSingle", "Open", "BounceAll"]);
        Assert.Contains(nameof(TransportCommand.Bounce), normalized);
        Assert.DoesNotContain("BounceSingle", normalized);
        Assert.DoesNotContain("BounceAll", normalized);
        Assert.Equal(1, normalized.Count(entry => entry == nameof(TransportCommand.Bounce)));
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
    public void CreateDefaultLayout_ContainsEveryCommandWithSeparators()
    {
        var layout = TransportToolbarCatalog.CreateDefaultLayout();
        Assert.Contains(TransportToolbarCatalog.SeparatorToken, layout);
        Assert.Contains(TransportToolbarCatalog.SectionSeparatorToken, layout);
        Assert.Equal("TogglePlayback", layout[0]);
        Assert.False(TransportToolbarCatalog.IsSeparator(layout[^1]));
        // Stop は再生トグルで兼ねるためカタログ非掲載
        Assert.DoesNotContain("Stop", layout);
        Assert.Equal(
            TransportToolbarCatalog.AllCommands.Select(command => command.ToString()).Order(),
            layout.Where(entry => !TransportToolbarCatalog.IsSeparator(entry)).Order());
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
        Assert.DoesNotContain(TransportCommand.ToggleWaapi, TransportToolbarCatalog.AllCommands);
        Assert.True(TransportToolbarCatalog.TryGetItem(TransportCommand.Cut, out var cut));
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
                settings.HiddenTransportToolbarCommands =
                    TransportToolbarCatalog.HiddenFromLayout(settings.TransportToolbarLayout);
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
    public void SetCommandsEnabled_HidesGroupsWithNoPressableButtons()
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
                string[] layout =
                [
                    nameof(TransportCommand.Cut),
                    nameof(TransportCommand.Copy),
                    TransportToolbarCatalog.SeparatorToken,
                    nameof(TransportCommand.Open),
                ];
                bar.ApplyLayoutFromSettings(new AppSettings
                {
                    TransportToolbarLayout = layout,
                    HiddenTransportToolbarCommands = TransportToolbarCatalog.HiddenFromLayout(layout),
                });
                Assert.Equal(2, bar.ItemsHost.Children.Count);

                // メニュー順でファイル（Open）→ 編集（Cut/Copy）。プレイヤー相当: 編集は不可、Open は常に可。
                bar.SetCommandsEnabled(enabled: true, allowEdit: false);
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)bar.ItemsHost.Children[0]).Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, ((System.Windows.FrameworkElement)bar.ItemsHost.Children[1]).Visibility);
                Assert.True(bar.HasVisibleCommands);

                bar.SetCommandsEnabled(enabled: true, allowEdit: true);
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)bar.ItemsHost.Children[0]).Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.FrameworkElement)bar.ItemsHost.Children[1]).Visibility);
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
    public void SetCommandsEnabled_DisablesMarkerJumpWhenNoMarkers()
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
                bar.ApplyLayoutFromSettings(new AppSettings
                {
                    TransportToolbarLayout =
                    [
                        nameof(TransportCommand.TogglePlayback),
                        nameof(TransportCommand.PreviousMarker),
                        nameof(TransportCommand.NextMarker),
                        nameof(TransportCommand.DeleteMarkers),
                    ],
                });

                bar.SetCommandsEnabled(
                    enabled: true,
                    allowEdit: true,
                    new TransportBar.CommandAvailability(HasMarkers: false));
                Assert.True(bar.ButtonFor(TransportCommand.TogglePlayback)!.IsEnabled);
                Assert.False(bar.ButtonFor(TransportCommand.PreviousMarker)!.IsEnabled);
                Assert.False(bar.ButtonFor(TransportCommand.NextMarker)!.IsEnabled);
                Assert.False(bar.ButtonFor(TransportCommand.DeleteMarkers)!.IsEnabled);

                bar.SetCommandsEnabled(
                    enabled: true,
                    allowEdit: true,
                    new TransportBar.CommandAvailability(HasMarkers: true));
                Assert.True(bar.ButtonFor(TransportCommand.PreviousMarker)!.IsEnabled);
                Assert.True(bar.ButtonFor(TransportCommand.NextMarker)!.IsEnabled);
                Assert.True(bar.ButtonFor(TransportCommand.DeleteMarkers)!.IsEnabled);
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
    public void SetCommandsEnabled_DisablesBounceAndMergeWhenUnavailable()
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
                bar.ApplyLayoutFromSettings(new AppSettings
                {
                    TransportToolbarLayout =
                    [
                        nameof(TransportCommand.TogglePlayback),
                        nameof(TransportCommand.Bounce),
                        nameof(TransportCommand.MergeTabs),
                    ],
                });

                bar.SetCommandsEnabled(
                    enabled: true,
                    allowEdit: true,
                    new TransportBar.CommandAvailability(CanBounce: false, CanMergeTabs: false));
                Assert.True(bar.ButtonFor(TransportCommand.TogglePlayback)!.IsEnabled);
                Assert.False(bar.ButtonFor(TransportCommand.Bounce)!.IsEnabled);
                Assert.False(bar.ButtonFor(TransportCommand.MergeTabs)!.IsEnabled);

                bar.SetCommandsEnabled(
                    enabled: true,
                    allowEdit: false,
                    new TransportBar.CommandAvailability(CanBounce: false, CanMergeTabs: true));
                Assert.False(bar.ButtonFor(TransportCommand.Bounce)!.IsEnabled);
                Assert.True(bar.ButtonFor(TransportCommand.MergeTabs)!.IsEnabled);

                bar.SetCommandsEnabled(
                    enabled: true,
                    allowEdit: true,
                    new TransportBar.CommandAvailability(CanBounce: true, CanMergeTabs: true));
                Assert.True(bar.ButtonFor(TransportCommand.Bounce)!.IsEnabled);
                Assert.True(bar.ButtonFor(TransportCommand.MergeTabs)!.IsEnabled);
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
        // 保存並び・セパレータは捨て、表示セットをカタログ順へ組み直す。
        // Layout にあるコマンドは Hidden より優先。それ以外は Hidden に無いものだけ足す。
        var layout = new[]
        {
            "Save",
            TransportToolbarCatalog.SeparatorToken,
            "Open",
            nameof(TransportCommand.TogglePlayback),
        };
        var hidden = TransportToolbarCatalog.HiddenFromLayout(layout);
        hidden = [.. hidden, "Open"]; // Layout にある Open は Hidden でも残る
        var resolved = TransportToolbarCatalog.ResolveLayout(layout, hidden);
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
