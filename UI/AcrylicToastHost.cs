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
    private readonly TextBlock _text;
    private DispatcherTimer? _holdTimer;
    private DispatcherTimer? _fadeTimer;
    private Color _fadeBase;
    private int _fadeStep;

    public AcrylicToastHost()
    {
        _text = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = DesignMetrics.PlaylistPdfPageNumberFontSize,
            FontWeight = FontWeights.Normal,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        _text.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        _glass.Padding = DesignMetrics.PlaylistPdfPageNumberPadding;
        _glass.Content = _text;
    }

    public string Text
    {
        get => _text.Text;
        set => _text.Text = value ?? string.Empty;
    }

    public TextAlignment TextAlignment
    {
        get => _text.TextAlignment;
        set => _text.TextAlignment = value;
    }

    public Thickness Padding
    {
        get => _glass.Padding;
        set => _glass.Padding = value;
    }

    public double FontSize
    {
        get => _text.FontSize;
        set => _text.FontSize = value;
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

    /// <summary>表示して <paramref name="hold"/> 後に疑似フェードして閉じる。</summary>
    public void Show(string text, TimeSpan hold)
    {
        StopFade();
        _text.Text = text ?? string.Empty;
        // いったん閉じて開き直し、拡縮直後の古い Placement を捨てる。
        if (_glass.IsOpen)
        {
            _glass.IsOpen = false;
        }

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
        _text.Text = text ?? string.Empty;
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
        var t = Math.Clamp(_fadeStep / (double)FadeSteps, 0, 1);
        var alpha = (byte)Math.Max(1, (int)Math.Round(_fadeBase.A * (1 - t)));
        var tint = Color.FromArgb(alpha, _fadeBase.R, _fadeBase.G, _fadeBase.B);
        if (!_glass.TryApplyTint(tint) || _fadeStep >= FadeSteps)
        {
            StopFade();
            _glass.IsOpen = false;
        }
    }

    private void StopFade()
    {
        _fadeTimer?.Stop();
        _fadeStep = 0;
    }
}
