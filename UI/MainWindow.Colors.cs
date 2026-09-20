using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private ColorDevPanelWindow? _colorDevPanel;
    private bool _appColorRefreshQueued;

    private void ShowColorDevPanel()
    {
        if (_colorDevPanel is null)
        {
            _colorDevPanel = new ColorDevPanelWindow
            {
                Owner = this,
                Topmost = Topmost,
            };
            _colorDevPanel.ColorsChanged += (_, _) => QueueAppColorRefresh();
            _colorDevPanel.Closed += (_, _) => _colorDevPanel = null;
            PositionColorDevPanel(_colorDevPanel);
        }

        _colorDevPanel.RefreshAppearance();
        _colorDevPanel.Present();
    }

    private void QueueAppColorRefresh()
    {
        if (_appColorRefreshQueued)
        {
            return;
        }

        _appColorRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _appColorRefreshQueued = false;
            ApplyUiColors(includeColorPanel: false);
        });
    }

    private void PositionColorDevPanel(ColorDevPanelWindow panel)
    {
        if (WindowPlacement.TryApplyColorPanel(panel, AppStorage.Settings))
        {
            return;
        }

        WindowPlacement.CenterOnOwner(panel, this);
    }

    private void OnUiScaleChanged()
    {
        // 編集履歴は等倍のまま（ルートの表示倍率を打ち消す）。
        HistoryOverlay.LayoutTransform = UiScaleService.CreateCounterTransform();
        ApplyUiColors();
        AlignBrandLicense();
    }

    internal void ApplyUiColors() => ApplyUiColors(includeColorPanel: true);

    internal void ApplyUiColors(bool includeColorPanel)
    {
        ApplyStatusFieldChrome();
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        LoadBrandLogo();
        BrandLicenseHost.ApplyColors();
        ForEachWaveform(view => view.RefreshAppearance());
        RefreshTileChrome();
        Overview.RefreshAppearance();
        Spectrum.RefreshAppearance();
        VectorScope.InvalidateVisual();
        LevelMeterBarPaint.Invalidate();
        LevelMeter.InvalidateVisual();
        HistoryStrip.InvalidateVisual();
        HistoryOverlay.RefreshAppearance();
        RefreshHistoryOverlay();
        LoudnessMeter.ApplyValueColors();
        Transport.RefreshAppearance();
        TimeScrollStrip.RefreshAppearance();
        TabScrollLeft.InvalidateVisual();
        TabScrollRight.InvalidateVisual();
        _waapiToggle?.InvalidateVisual();
        WaapiBar.RefreshAppearance();
        RefreshTabHeaders();
        RefreshStatus();
        if (includeColorPanel)
        {
            _colorDevPanel?.RefreshAppearance();
        }

        _tabTimeTable?.RefreshAppearance();
        LibraryBrowser.RefreshAppearance();
    }

    private void ApplyStatusFieldChrome()
    {
        var player = IsLibraryMaximized;
        StatusTimes.UsePlayerComboFill(player);
        if (player)
        {
            AssignSpeakerBrush("PlayerComboFillBrush", "PlayerComboFillBrush");
            AssignSpeakerBrush("PlayerComboHoverFillBrush", "PlayerComboHoverFillBrush");
            AssignSpeakerBrush("PlayerComboDisabledFillBrush", "PlayerComboDisabledFillBrush");
            AssignSpeakerBrush("PlayerComboDropFillBrush", "PlayerComboDropFillBrush");
            return;
        }

        AssignSpeakerBrush("PlayerComboFillBrush", "StatusTimecodeFillBrush");
        AssignSpeakerBrush("PlayerComboHoverFillBrush", "StatusTimecodeFillBrush");
        AssignSpeakerBrush("PlayerComboDisabledFillBrush", "StatusTimecodeFillBrush");
        AssignSpeakerBrush("PlayerComboDropFillBrush", "StatusTimecodeFillBrush");
    }

    private void AssignSpeakerBrush(string localKey, string sourceKey)
    {
        if (Application.Current?.TryFindResource(sourceKey) is not SolidColorBrush source)
        {
            return;
        }

        // テンプレートはローカルキーを DynamicResource で引く。
        // アプリ資源のブラシをそのまま入れると親が二重になるので、その時点の色をコピーする。
        var copy = (SolidColorBrush)source.Clone();
        if (copy.CanFreeze)
        {
            copy.Freeze();
        }

        SpeakerMenu.Resources[localKey] = copy;
    }
}
