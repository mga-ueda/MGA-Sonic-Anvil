using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>Acrylic オーバーレイの見た目方針。</summary>
internal enum AcrylicOverlayKind
{
    /// <summary>通常トースト。DWM Round 角丸＋システム影。</summary>
    Toast,

    /// <summary>
    /// タイムコード。角丸はトーストと同じ DWM Round（枠は設定の CornerRadius）。
    /// TOPMOST だけ外す（常時表示のため他アプリの下に回れるようにする）。
    /// </summary>
    Timecode,
}

/// <summary>
/// DWM Acrylic のオーバーレイ（Popup）。
/// トースト／タイムコードとも DWM Round 角丸。枠の <see cref="Border.CornerRadius"/> は設定へバインド。
/// </summary>
internal sealed class AcrylicGlassPopup
{
    private static readonly object TrackGate = new();
    private static readonly List<WeakReference<AcrylicGlassPopup>> Tracked = [];

    private readonly Popup _popup;
    private readonly Border _plate;

    /// <summary>色のライブ更新時にティントを加工する（フェード中は現在のαを保つ）。</summary>
    public Func<Color, Color>? LiveTint { get; set; }

    public AcrylicGlassPopup()
        : this(AcrylicOverlayKind.Toast)
    {
    }

    public AcrylicGlassPopup(AcrylicOverlayKind kind)
    {
        _ = kind;
        _plate = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            Effect = null,
        };
        DesignMetrics.BindCorner(_plate);
        _plate.SetResourceReference(Border.BorderBrushProperty, "PlayerComboDropBorderBrush");
        _plate.SizeChanged += (_, _) => ApplyWindowChrome();

        _popup = new Popup
        {
            AllowsTransparency = false,
            Placement = PlacementMode.Center,
            StaysOpen = true,
            Focusable = false,
            IsHitTestVisible = false,
            Child = _plate,
        };
        // トースト／タイムコードとも DWM Round。Acrylic 塗りは OS 固定半径。
        AcrylicPopup.Attach(
            _popup,
            AcrylicBackdrop.ToastTintBrushKey,
            roundCorners: true,
            paintChildBackground: false);
        _popup.Opened += (_, _) =>
            _popup.Dispatcher.BeginInvoke(ApplyWindowChrome, DispatcherPriority.Loaded);
        Track(this);
    }

    /// <summary>開いているトースト／タイムコードへ、今の <c>AcrylicToastTintBrush</c> を載せ直す。</summary>
    public static void RefreshOpenTints()
    {
        AcrylicGlassPopup[] live;
        lock (TrackGate)
        {
            PruneTracked();
            live = Tracked
                .Select(weak => weak.TryGetTarget(out var popup) ? popup : null)
                .Where(popup => popup is not null)
                .Cast<AcrylicGlassPopup>()
                .ToArray();
        }

        var tint = AcrylicBackdrop.ResolveTintColor(AcrylicBackdrop.ToastTintBrushKey);
        foreach (var popup in live)
        {
            popup.ApplyLiveTint(tint);
        }
    }

    /// <summary>開いているオーバーレイの HWND 状態（ホストに合わせた TOPMOST の付け外し）を載せ直す。</summary>
    public static void RefreshOpenCornerClips()
    {
        AcrylicGlassPopup[] live;
        lock (TrackGate)
        {
            PruneTracked();
            live = Tracked
                .Select(weak => weak.TryGetTarget(out var popup) ? popup : null)
                .Where(popup => popup is not null)
                .Cast<AcrylicGlassPopup>()
                .ToArray();
        }

        foreach (var popup in live)
        {
            popup.ApplyWindowChrome();
        }
    }

    /// <summary>タイムコード専用ホスト（角丸はトーストと同じ DWM Round）。</summary>
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
        IsOpen && AcrylicBackdrop.TryApplyTintToVisual(_plate, tint, AcrylicCornerStyle.Round);

    private void ApplyLiveTint(Color tint)
    {
        if (!IsOpen)
        {
            return;
        }

        if (LiveTint is not null)
        {
            tint = LiveTint(tint);
        }

        TryApplyTint(tint);
    }

    /// <summary>
    /// Popup の Z 順をホストに合わせる。
    /// 通常は TOPMOST を外して他アプリの手前に残さない。
    /// ホストが全画面 Topmost のときは同じ帯に残し、背面へ回らないようにする。
    /// </summary>
    private void ApplyWindowChrome()
    {
        if (!_popup.IsOpen
            || PresentationSource.FromVisual(_plate) is not HwndSource source
            || source.Handle == IntPtr.Zero)
        {
            return;
        }

        var ownerTopmost = _popup.PlacementTarget is DependencyObject target
            && Window.GetWindow(target) is { Topmost: true };

        if (ownerTopmost)
        {
            AcrylicBackdrop.EnsurePopupTopmost(source.Handle);
        }
        else
        {
            AcrylicBackdrop.ClearPopupTopmost(source.Handle);
        }
    }

    private static void Track(AcrylicGlassPopup popup)
    {
        lock (TrackGate)
        {
            PruneTracked();
            foreach (var weak in Tracked)
            {
                if (weak.TryGetTarget(out var existing) && ReferenceEquals(existing, popup))
                {
                    return;
                }
            }

            Tracked.Add(new WeakReference<AcrylicGlassPopup>(popup));
        }
    }

    private static void PruneTracked()
    {
        for (var i = Tracked.Count - 1; i >= 0; i--)
        {
            if (!Tracked[i].TryGetTarget(out _))
            {
                Tracked.RemoveAt(i);
            }
        }
    }

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
        ApplyWindowChrome();
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
