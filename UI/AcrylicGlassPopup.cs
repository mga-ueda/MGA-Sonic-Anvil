using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>Acrylic オーバーレイの見た目方針。</summary>
internal enum AcrylicOverlayKind
{
    /// <summary>通常トースト。DWM 角丸＋システム影あり。</summary>
    Toast,

    /// <summary>
    /// タイムコード専用。DWM 角丸／システム影なし（Border の CornerRadius のみ）。
    /// </summary>
    Timecode,
}

/// <summary>
/// DWM Acrylic のオーバーレイ（Popup）。
/// <see cref="AcrylicOverlayKind.Toast"/> は角丸＋システム影あり。
/// <see cref="AcrylicOverlayKind.Timecode"/> は影なし（タイムコード専用）。
/// </summary>
internal sealed class AcrylicGlassPopup
{
    private readonly Popup _popup;
    private readonly Border _plate;
    private readonly bool _roundCorners;

    public AcrylicGlassPopup()
        : this(AcrylicOverlayKind.Toast)
    {
    }

    public AcrylicGlassPopup(AcrylicOverlayKind kind)
    {
        _roundCorners = kind != AcrylicOverlayKind.Timecode;
        _plate = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = DesignMetrics.ControlCorner,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            Effect = null,
        };
        _plate.SetResourceReference(Border.BorderBrushProperty, "PlayerComboDropBorderBrush");

        _popup = new Popup
        {
            AllowsTransparency = false,
            Placement = PlacementMode.Center,
            StaysOpen = true,
            Focusable = false,
            IsHitTestVisible = false,
            Child = _plate,
        };
        AcrylicPopup.Attach(
            _popup,
            AcrylicBackdrop.ToastTintBrushKey,
            roundCorners: _roundCorners);
    }

    /// <summary>タイムコード専用ホスト（影なし）。</summary>
    public static AcrylicGlassPopup CreateTimecode() => new(AcrylicOverlayKind.Timecode);

    public UIElement? Content
    {
        get => _plate.Child;
        set => _plate.Child = value;
    }

    public Thickness Padding
    {
        get => _plate.Padding;
        set => _plate.Padding = value;
    }

    public UIElement? PlacementTarget
    {
        get => _popup.PlacementTarget;
        set => _popup.PlacementTarget = value;
    }

    public PlacementMode Placement
    {
        get => _popup.Placement;
        set => _popup.Placement = value;
    }

    public CustomPopupPlacementCallback? CustomPopupPlacementCallback
    {
        get => _popup.CustomPopupPlacementCallback;
        set
        {
            _popup.CustomPopupPlacementCallback = value;
            if (value is not null)
            {
                _popup.Placement = PlacementMode.Custom;
            }
        }
    }

    public bool IsOpen
    {
        get => _popup.IsOpen;
        set => _popup.IsOpen = value;
    }

    public void SetOverlayScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            scale = 1;
        }

        scale = Math.Clamp(scale, DesignMetrics.OverlayScaleMin, DesignMetrics.OverlayScaleMax);
        _plate.LayoutTransform = Math.Abs(scale - 1d) > 0.0001
            ? UiScaleService.CreatePublishedTransform(scale)
            : Transform.Identity;
        // 拡縮で popupSize が変わるので、開いていれば位置をやり直す。
        RefreshPlacementAfterLayout();
    }

    /// <summary>開いているときだけティント色を載せ直す（αフェード用）。</summary>
    public bool TryApplyTint(Color tint) =>
        IsOpen && AcrylicBackdrop.TryApplyTintToVisual(_plate, tint, _roundCorners);

    /// <summary>
    /// PlacementTarget のレイアウト後に位置をやり直す。
    /// 窓拡縮直後は Center が古い寸法のまま残ることがある。
    /// </summary>
    public void RefreshPlacement()
    {
        if (!_popup.IsOpen)
        {
            return;
        }

        var offset = _popup.HorizontalOffset;
        _popup.HorizontalOffset = offset + 0.01;
        _popup.HorizontalOffset = offset;
    }

    public void RefreshPlacementAfterLayout()
    {
        if (!_popup.IsOpen)
        {
            return;
        }

        _popup.Dispatcher.BeginInvoke(RefreshPlacement, DispatcherPriority.Loaded);
    }
}
