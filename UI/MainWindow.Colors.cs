using System.Windows;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
#if DEBUG
    private ColorDevPanelWindow? _colorDevPanel;

    private void ShowColorDevPanel()
    {
        if (_colorDevPanel is null)
        {
            _colorDevPanel = new ColorDevPanelWindow
            {
                Owner = this,
                Topmost = Topmost,
            };
            _colorDevPanel.ColorsChanged += (_, _) => ApplyUiColors();
            _colorDevPanel.Closed += (_, _) => _colorDevPanel = null;
            PositionColorDevPanel(_colorDevPanel);
        }

        _colorDevPanel.RefreshRows();
        _colorDevPanel.Show();
        _colorDevPanel.Activate();
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

    private void ApplyUiColors()
    {
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        Waveform.RefreshAppearance();
        Overview.RefreshAppearance();
        Spectrum.InvalidateVisual();
        VectorScope.InvalidateVisual();
        LevelMeter.InvalidateVisual();
        LoudnessMeter.ApplyValueColors();
        Transport.RefreshAppearance();
        SettingsGear.RefreshAppearance();
        _waapiToggle?.InvalidateVisual();
        RebuildTabBar();
    }
#endif
}
