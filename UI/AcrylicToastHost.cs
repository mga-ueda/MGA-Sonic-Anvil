using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// DWM Acrylic の一時トースト。
/// HWND の Opacity フェードは Acrylic（非レイヤード）と両立しないため、
/// 閉じるときはティントαを下げてから閉じる（疑似フェード）。
/// </summary>
internal sealed class AcrylicToastHost
{
    private const int FadeSteps = 5;

    private readonly AcrylicGlassPopup _glass = new();
    private readonly StackPanel _body;
    private readonly TextBlock _text;
    private readonly TextBlock _subText;
    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _fadeTimer;
    private Color _fadeBase;
    private int _fadeStep;

    public AcrylicToastHost()
    {
        _text = CreateLine();
        _subText = CreateLine();
        _subText.Visibility = Visibility.Collapsed;
        _body = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _body.Children.Add(_text);
        _body.Children.Add(_subText);
        _glass.Padding = DesignMetrics.PlaylistPdfPageNumberPadding;
        _glass.Content = _body;
        _glass.LiveTint = AdjustLiveTint;
    }

    public bool IsOpen => _glass.IsOpen;

    public string Text
    {
        get => JoinLines(_text.Text, VisibleSubText());
        set => ApplyLines(value);
    }

    public TextAlignment TextAlignment
    {
        get => _text.TextAlignment;
        set
        {
            _text.TextAlignment = value;
            _subText.TextAlignment = value;
        }
    }

    public Thickness Padding
    {
        get => _glass.Padding;
        set => _glass.Padding = value;
    }

    public double FontSize
    {
        get => _text.FontSize;
        set
        {
            _text.FontSize = value;
            _subText.FontSize = value;
        }
    }

    public UIElement? PlacementTarget
    {
        get => _glass.PlacementTarget;
        set => _glass.PlacementTarget = value;
    }

    public PlacementMode Placement
    {
        get => _glass.Placement;
        set => _glass.Placement = value;
    }

    public CustomPopupPlacementCallback? CustomPopupPlacementCallback
    {
        get => _glass.CustomPopupPlacementCallback;
        set => _glass.CustomPopupPlacementCallback = value;
    }

    public void SetOverlayScale(double scale) => _glass.SetOverlayScale(scale);

    /// <summary>開いているとき、PlacementTarget のレイアウト後に位置をやり直す。</summary>
    public void RefreshPlacementAfterLayout() => _glass.RefreshPlacementAfterLayout();

    /// <summary>表示して <paramref name="hold"/> 後に疑似フェードして閉じる。</summary>
    public void Show(string text, TimeSpan hold)
    {
        StopFade();
        ApplyLines(text);
        // いったん閉じて開き直し、拡縮直後の古い Placement／2 行分の古い寸法を捨てる。
        if (_glass.IsOpen)
        {
            _glass.IsOpen = false;
        }

        // 行数変化後の DesiredSize を確定してから開く（空の 2 行目が残らないように）。
        _body.InvalidateMeasure();
        _body.UpdateLayout();
        _glass.IsOpen = true;
        _glass.RefreshPlacementAfterLayout();
        _holdTimer ??= new DispatcherTimer();
        _holdTimer.Tick -= OnHoldTick;
        _holdTimer.Tick += OnHoldTick;
        _holdTimer.Interval = hold;
        _holdTimer.Stop();
        _holdTimer.Start();
    }

    /// <summary>既定の 1 秒ホールドで表示する。</summary>
    public void Show(string text) => Show(text, LibraryExplorerTypeahead.ToastIdleClear);

    /// <summary>タイマーなしで開く（呼び出し側が <see cref="Hide"/> する）。</summary>
    public void ShowSticky(string text)
    {
        StopFade();
        _holdTimer?.Stop();
        ApplyLines(text);
        if (_glass.IsOpen)
        {
            _glass.IsOpen = false;
        }

        _body.InvalidateMeasure();
        _body.UpdateLayout();
        _glass.IsOpen = true;
    }

    /// <param name="instant">true なら即閉じ（終了時など）。false ならティントαフェード。</param>
    public void Hide(bool instant = true)
    {
        _holdTimer?.Stop();
        if (instant || !_glass.IsOpen)
        {
            StopFade();
            _glass.IsOpen = false;
            return;
        }

        BeginTintFade();
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        _holdTimer?.Stop();
        Hide(instant: false);
    }

    private void BeginTintFade()
    {
        StopFade();
        _fadeBase = AcrylicBackdrop.ResolveTintColor(AcrylicBackdrop.ToastTintBrushKey);
        _fadeStep = 0;
        var step = TimeSpan.FromSeconds(LibraryPlayerMode.ChromeFadeSeconds / FadeSteps);
        if (step < TimeSpan.FromMilliseconds(16))
        {
            step = TimeSpan.FromMilliseconds(16);
        }

        _fadeTimer ??= new DispatcherTimer();
        _fadeTimer.Interval = step;
        _fadeTimer.Tick -= OnFadeTick;
        _fadeTimer.Tick += OnFadeTick;
        _fadeTimer.Start();
        OnFadeTick(_fadeTimer, EventArgs.Empty);
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        _fadeStep++;
        if (!_glass.TryApplyTint(FadeTint(_fadeBase, _fadeStep)) || _fadeStep >= FadeSteps)
        {
            StopFade();
            _glass.IsOpen = false;
        }
    }

    /// <summary>表示中の色変更ではフェードの進みを保ったまま、新しいティント色へ載せ替える。</summary>
    private Color AdjustLiveTint(Color tint)
    {
        if (_fadeTimer is not { IsEnabled: true })
        {
            return tint;
        }

        _fadeBase = tint;
        return FadeTint(tint, _fadeStep);
    }

    private static Color FadeTint(Color tint, int step)
    {
        var t = Math.Clamp(step / (double)FadeSteps, 0, 1);
        var alpha = (byte)Math.Max(1, (int)Math.Round(tint.A * (1 - t)));
        return Color.FromArgb(alpha, tint.R, tint.G, tint.B);
    }

    private void StopFade()
    {
        _fadeTimer?.Stop();
        _fadeStep = 0;
    }

    private void ApplyLines(string? text)
    {
        SplitLines(text, out var primary, out var secondary);
        _text.Text = primary;
        if (string.IsNullOrEmpty(secondary))
        {
            _subText.Text = string.Empty;
            _subText.Visibility = Visibility.Collapsed;
        }
        else
        {
            _subText.Text = secondary;
            _subText.Visibility = Visibility.Visible;
        }
    }

    private string? VisibleSubText() =>
        _subText.Visibility == Visibility.Visible ? _subText.Text : null;

    internal static void SplitLines(string? text, out string primary, out string secondary)
    {
        primary = string.Empty;
        secondary = string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var parts = normalized.Split('\n');
        primary = parts[0].TrimEnd();
        for (var i = 1; i < parts.Length; i++)
        {
            var line = parts[i].TrimEnd();
            if (line.Length == 0)
            {
                // 空行は捨てる（Player mode の偽 2 行目など）。
                continue;
            }

            secondary = string.IsNullOrEmpty(secondary) ? line : secondary + "\n" + line;
        }
    }

    private static string JoinLines(string primary, string? secondary) =>
        string.IsNullOrEmpty(secondary) ? primary ?? string.Empty : primary + "\n" + secondary;

    private static TextBlock CreateLine()
    {
        var line = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = DesignMetrics.PlaylistPdfPageNumberFontSize,
            FontWeight = FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        line.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        return line;
    }
}
