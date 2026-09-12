using System.Windows;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
#if DEBUG
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
        _colorDevPanel.Show();
        _colorDevPanel.Activate();
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
        panel.WindowStartupLocation = WindowStartupLocation.Manual;
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var panelWidth = panel.DesiredSize.Width;
        if (panelWidth <= 1)
        {
            panelWidth = panel.Width;
        }

        var work = SystemParameters.WorkArea;
        var x = Math.Min(Left + ActualWidth + 8, work.Right - panelWidth);
        var y = Math.Max(work.Top, Top);
        panel.Left = Math.Max(work.Left, x);
        panel.Top = y;
    }
#endif

    internal void ApplyUiColors() => ApplyUiColors(includeColorPanel: true);

    internal void ApplyUiColors(bool includeColorPanel)
    {
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        Waveform.RefreshAppearance();
        Overview.RefreshAppearance();
        Spectrum.RefreshAppearance();
        VectorScope.InvalidateVisual();
        LevelMeter.InvalidateVisual();
        HistoryStrip.InvalidateVisual();
        HistoryOverlay.InvalidateVisual();
        LoudnessMeter.ApplyValueColors();
        Transport.RefreshAppearance();
        TimeScrollStrip.RefreshAppearance();
        TabScrollLeft.InvalidateVisual();
        TabScrollRight.InvalidateVisual();
        _waapiToggle?.InvalidateVisual();
        WaapiBar.RefreshAppearance();
        RefreshTabHeaders();
        RefreshStatus();
#if DEBUG
        if (includeColorPanel)
        {
            _colorDevPanel?.RefreshAppearance();
        }
#endif
    }
}
