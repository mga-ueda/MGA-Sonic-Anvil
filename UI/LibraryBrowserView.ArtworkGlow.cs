using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed partial class LibraryBrowserView
{
    public void SetArtwork(AudioDocument? document, bool keepCurrentIfEmpty = false) =>
        ApplyArtwork(
            document?.Artwork,
            document is { } d && AllowsJacketReplace(d.SourceKind),
            keepCurrentIfEmpty);

    /// <summary>
    /// デコードとウォッシュ生成（重い）。UI スレッド以外で呼ぶ。結果はすべて Freeze 済み。
    /// </summary>
    internal static PreparedArtworkPresentation PrepareArtworkPresentation(byte[]? bytes)
    {
        var next = bytes is { Length: > 0 } ? bytes : null;
        if (next is null)
        {
            return new PreparedArtworkPresentation(null, null, CreateFallbackWashTurns(), decodedOk: false);
        }

        var decoded = TryCreateBitmap(next, ArtworkDecodeMaxEdge);
        if (decoded is null)
        {
            return new PreparedArtworkPresentation(next, null, CreateFallbackWashTurns(), decodedOk: false);
        }

        return new PreparedArtworkPresentation(
            next,
            decoded,
            CreateAmbientWashTurns(decoded),
            decodedOk: true);
    }

    /// <summary>裏で作ったウォッシュを載せるだけ。デコードしない。</summary>
    internal void ApplyPreparedArtwork(
        PreparedArtworkPresentation prepared,
        bool allowReplace,
        bool keepCurrentIfEmpty = false)
    {
        JacketReplaceEnabled = allowReplace;
        var next = prepared.Bytes;
        if (ArtworkEquals(_artworkBytes, next) && _jacketBitmap is not null)
        {
            return;
        }

        if (next is null && keepCurrentIfEmpty && _artworkBytes is not null)
        {
            return;
        }

        if (next is not null && !prepared.DecodedOk)
        {
            // 次の画像が読めないときは、今の実ジャケットを外してフォールバックへ落とさない。
            if (_artworkBytes is not null)
            {
                return;
            }
        }

        if (next is not null && prepared.DecodedOk && prepared.Bitmap is not null)
        {
            _artworkBytes = next;
            _jacketBitmap = prepared.Bitmap;
            ApplyArtworkGlow(prepared.Bitmap, prepared.Turns);
            return;
        }

        _artworkBytes = next;
        _jacketBitmap = null;
        ApplyArtworkGlow(null, prepared.Turns);
    }

    private void ApplyArtwork(byte[]? bytes, bool allowReplace, bool keepCurrentIfEmpty = false)
    {
        ApplyPreparedArtwork(PrepareArtworkPresentation(bytes), allowReplace, keepCurrentIfEmpty);
    }

    private void ShowJacketDisplay(BitmapSource? decoded, bool realArt)
    {
        var bitmap = decoded;
        _jacketBitmap = bitmap;
        ApplyArtworkGlow(realArt && bitmap is not null ? bitmap : null);
    }

    internal readonly struct PreparedArtworkPresentation
    {
        public PreparedArtworkPresentation(
            byte[]? bytes,
            BitmapSource? bitmap,
            Brush[] turns,
            bool decodedOk)
        {
            Bytes = bytes;
            Bitmap = bitmap;
            Turns = turns;
            DecodedOk = decodedOk;
        }

        public byte[]? Bytes { get; }

        public BitmapSource? Bitmap { get; }

        public Brush[] Turns { get; }

        public bool DecodedOk { get; }
    }

    private void ApplyGlowVeil()
    {
        var veil = PlayerChrome.Get("PlayerGlowVeilBrush");
        var brush = CreateDitheredVeilBrush(veil);
        _veil.Background = brush;
        _waveVeil.Background = brush;
        var opacity = veil.A / 255d;
        _veil.Opacity = opacity;
        _waveVeil.Opacity = opacity;
    }

    /// <summary>
    /// ウィンドウ全体を一枚のウォッシュ。プレイヤー中だけ伸ばし、リスト側の別アニメは畳む。
    /// </summary>
    internal void BindWaveformGlow(Grid host)
    {
        _waveGlowHost = host;
        // レイアウト寸法は固定。窓の拡縮はカバー用スケールだけを変え、ビットマップは描き直さない。
        _waveGlow.Width = GlowCacheWidth;
        _waveGlow.Height = GlowCacheHeight;
        var drift = new TransformGroup();
        drift.Children.Add(_waveGlowCover);
        drift.Children.Add(_waveGlowScale);
        drift.Children.Add(_waveGlowTranslate);
        _waveGlow.RenderTransform = drift;
        _waveGlow.RenderTransformOrigin = new Point(0.5, 0.5);
        _waveGlow.CacheMode = CreateGlowBitmapCache();
        _waveGlow.IsHitTestVisible = false;
        _waveGlow.SnapsToDevicePixels = false;
        _waveGlow.UseLayoutRounding = false;
        _waveGlow.Visibility = Visibility.Collapsed;
        host.SizeChanged += (_, _) => SyncWaveGlowCover();
        _waveVeil.IsHitTestVisible = false;
        _waveVeil.Visibility = Visibility.Collapsed;
        if (_waveGlow.Children.Count == 0)
        {
            _waveGlow.Children.Add(_waveGlowFrom);
            _waveGlow.Children.Add(_waveGlowTo);
        }

        _waveGlowCanvas.Children.Add(_waveGlow);
        host.Children.Add(_waveGlowCanvas);
        host.Children.Add(_wallpaper);
        host.Children.Add(_waveVeil);
        ApplyGlowVeil();
        CopyGlowWash(_glowFrom, _glowTo, _waveGlowFrom, _waveGlowTo);

        PlaceArtworkGlow();
        SyncWaveGlowCover();
        ContinueGlowDrift();
    }

    /// <summary>
    /// 動的背景の上に壁紙を乗せる。パスが空、または読めないときは出さない。
    /// </summary>
    internal void ApplyWallpaper(string? path, double opacity)
    {
        var bitmap = WallpaperBitmap.TryLoad(path);
        _wallpaper.Source = bitmap;
        if (bitmap is null)
        {
            _wallpaper.Opacity = 0;
            _wallpaper.Visibility = Visibility.Collapsed;
            return;
        }

        _wallpaper.Opacity = AppSettings.ClampWallpaperOpacity(opacity);
        _wallpaper.Visibility = Visibility.Visible;
    }

    /// <summary>動画／PDF を窓全体の背面（ウォッシュより前、クロームより後ろ）に置く。</summary>
    internal void BindWindowVisual(Grid host)
    {
        host.IsHitTestVisible = false;
        host.ClipToBounds = true;
        if (_pinnedPdfHost.Children.Count == 0)
        {
            _pinnedPdfHost.Children.Add(_pinnedPdfImage);
            _pinnedPdfHost.Children.Add(_pinnedPdfDim);
        }

        if (!ReferenceEquals(VisualTreeHelper.GetParent(_pinnedPdfHost), host))
        {
            host.Children.Insert(0, _pinnedPdfHost);
        }

        if (!ReferenceEquals(VisualTreeHelper.GetParent(_visualStage), host))
        {
            host.Children.Add(_visualStage);
        }
    }

    internal FrameworkElement VideoHudFrostTarget => _visualStage;

    /// <summary>動画本再生／ミニのメーター・波形・ファイル名の背後を少し暗くする。</summary>
    internal void SetVideoHudFrost(bool on, Geometry? clip) => _visualStage.SetHudFrost(on, clip);

    /// <summary>エディタの背景。プレイヤーのフォールバックウォッシュをウィンドウ全体に出す。</summary>
    internal void UseWindowFallbackWash()
    {
        ClearPdfBackgroundPin();
        _extendGlow = true;
        ApplyArtworkGlow(null);
    }

    /// <summary>プレイヤー表示中はウィンドウ全体に一枚で広げる。編集画面では畳む。</summary>
    internal void SetGlowExtendsWaveform(bool extend)
    {
        if (_extendGlow == extend)
        {
            return;
        }

        _extendGlow = extend;
        PlaceArtworkGlow();
        SyncWaveGlowCover();
        ContinueGlowDrift();
    }

    private bool UseUnifiedGlow => _artworkGlow && _extendGlow && _waveGlowHost is not null;

    private void ApplyArtworkGlow(BitmapSource? bitmap, Brush[]? preparedTurns = null)
    {
        var turns = preparedTurns
            ?? (bitmap is null ? CreateFallbackWashTurns() : CreateAmbientWashTurns(bitmap));
        GlowUsesFallback = bitmap is null;
        var glowChanged = !_artworkGlow;
        _artworkGlow = true;
        ApplyWashTurns(turns);

        PlaceArtworkGlow();
        SyncWaveGlowCover();
        ContinueGlowDrift();

        if (glowChanged)
        {
            var preserve = SelectedSessions;
            var active = SelectedSession;
            BeginRowSync();
            try
            {
                ApplyGridStyles();
                ApplyRowSelectionCore(active, preserve);
            }
            finally
            {
                EndRowSync();
            }
        }
    }

    private void ApplyWashTurns(Brush[] turns)
    {
        if (ReferenceEquals(_washTurns, turns) && _washTurns.Length > 0)
        {
            return;
        }

        _washTurns = turns;
        _washTurn = 0;
        var wash = turns[0];
        if (!ReferenceEquals(_glowWash, wash))
        {
            CrossfadeGlowWash(wash);
        }

        SyncWashTurns();
    }

    private void CrossfadeGlowWash(Brush wash) =>
        CrossfadeGlowWash(wash, GlowCrossfadeSeconds);

    private void CrossfadeGlowWash(Brush wash, double seconds)
    {
        var previous = _glowWash;
        _glowWash = wash;
        if (previous is null)
        {
            ApplyGlowWashImmediate(wash);
            return;
        }

        BeginGlowCrossfade(_glowFrom, _glowTo, previous, wash, seconds);
        BeginGlowCrossfade(_waveGlowFrom, _waveGlowTo, previous, wash, seconds);
    }

    private void ApplyGlowWashImmediate(Brush wash)
    {
        ApplyGlowWashImmediate(_glowFrom, _glowTo, wash);
        ApplyGlowWashImmediate(_waveGlowFrom, _waveGlowTo, wash);
    }

    private static void ApplyGlowWashImmediate(Border from, Border to, Brush wash)
    {
        StopGlowCrossfade(from, to);
        from.Background = null;
        from.Opacity = 0;
        to.Background = wash;
        to.Opacity = 1;
    }

    private static void BeginGlowCrossfade(Border from, Border to, Brush previous, Brush next, double seconds)
    {
        StopGlowCrossfade(from, to);
        from.Background = previous;
        from.Opacity = 1;
        to.Opacity = 0;
        to.Background = next;
        from.BeginAnimation(UIElement.OpacityProperty, CreateGlowCrossfade(1, 0, seconds));
        to.BeginAnimation(UIElement.OpacityProperty, CreateGlowCrossfade(0, 1, seconds));
    }

    private static void StopGlowCrossfade(Border from, Border to)
    {
        var fromOpacity = from.Opacity;
        var toOpacity = to.Opacity;
        from.BeginAnimation(UIElement.OpacityProperty, null);
        to.BeginAnimation(UIElement.OpacityProperty, null);
        from.Opacity = fromOpacity;
        to.Opacity = toOpacity;
    }

    private static void CopyGlowWash(Border from, Border to, Border waveFrom, Border waveTo)
    {
        StopGlowCrossfade(waveFrom, waveTo);
        waveFrom.Background = from.Background;
        waveFrom.Opacity = from.Opacity;
        waveTo.Background = to.Background;
        waveTo.Opacity = to.Opacity;
    }

    private static Border CreatePaneFocusLine()
    {
        var line = new Border
        {
            Height = PaneFocusLineHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Background = CreatePaneFocusLineBrush(),
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
            Visibility = Visibility.Collapsed,
        };
        // 上辺のグラデだけなので左上・右上に既定の GUI 角丸を付ける。
        BindingOperations.SetBinding(
            line,
            Border.CornerRadiusProperty,
            new Binding(nameof(CornerChrome.Radius))
            {
                Source = DesignMetrics.Chrome,
                Mode = BindingMode.OneWay,
                Converter = PaneFocusTopCornerConverter.Instance,
            });
        return line;
    }

    private sealed class PaneFocusTopCornerConverter : IValueConverter
    {
        internal static readonly PaneFocusTopCornerConverter Instance = new();

        public object Convert(
            object value,
            Type targetType,
            object parameter,
            System.Globalization.CultureInfo culture) =>
            PaneFocusTopCorners(value is double pixels ? pixels : null);

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }

    private void ApplyPaneFocusLineBrush()
    {
        var brush = CreatePaneFocusLineBrush();
        _treeFocusLine.Background = brush;
        _favoritesFocusLine.Background = brush;
        _listFocusLine.Background = brush;
    }

    internal static Color PaneFocusLineColor(UiTheme theme) =>
        PlayerChrome.Get("PlayerPaneFocusLineBrush", theme);

    internal static Brush CreatePaneFocusLineBrush() =>
        CreatePaneFocusLineBrush(UiThemeService.Painted);

    internal static Brush CreatePaneFocusLineBrush(UiTheme theme)
    {
        var top = PaneFocusLineColor(theme);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        brush.GradientStops.Add(new GradientStop(top, 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, top.R, top.G, top.B), 1));
        brush.Freeze();
        return brush;
    }

    private static Border CreateGlowWashLayer() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        IsHitTestVisible = false,
        SnapsToDevicePixels = false,
        UseLayoutRounding = false,
        Opacity = 0,
    };

    /// <summary>
    /// ドリフトをビットマップ化して、背面ウォッシュの再描画がリストまで汚さない。
    /// </summary>
    private static BitmapCache CreateGlowBitmapCache() => new()
    {
        EnableClearType = false,
        SnapsToDevicePixels = false,
        RenderAtScale = GlowCacheRenderAtScale,
    };

    private void PlaceArtworkGlow()
    {
        // PDF 固定背景は PlayerVisualHost 側。通常のジャケットウォッシュは重ねない。
        if (_pdfBackgroundPinned)
        {
            _pinnedPdfHost.Visibility = Visibility.Visible;
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
            HideWaveGlowHost();
            Background = Brushes.Transparent;
            _root.Background = Brushes.Transparent;
            return;
        }

        _pinnedPdfHost.Visibility = Visibility.Collapsed;

        if (_visualStage.IsShown)
        {
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
            Background = Brushes.Transparent;
            _root.Background = Brushes.Transparent;
            if (UseUnifiedGlow)
            {
                _waveGlow.Visibility = Visibility.Visible;
                _waveVeil.Visibility = Visibility.Visible;
                _waveGlowHost!.Visibility = Visibility.Visible;
            }
            else
            {
                HideWaveGlowHost();
            }

            return;
        }

        if (UseUnifiedGlow)
        {
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
            _waveGlow.Visibility = Visibility.Visible;
            _waveVeil.Visibility = Visibility.Visible;
            _waveGlowHost!.Visibility = Visibility.Visible;
            Background = Brushes.Transparent;
            _root.Background = Brushes.Transparent;
            return;
        }

        if (_artworkGlow)
        {
            _glowHost.Visibility = Visibility.Visible;
            _veil.Visibility = Visibility.Visible;
        }
        else
        {
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
        }

        HideWaveGlowHost();
        RestoreLibrarySurface();
    }

    private void HideWaveGlowHost()
    {
        _waveGlow.Visibility = Visibility.Collapsed;
        _waveVeil.Visibility = Visibility.Collapsed;
        if (_waveGlowHost is not null)
        {
            _waveGlowHost.Visibility = Visibility.Collapsed;
        }
    }

    private void RestoreLibrarySurface()
    {
        SetResourceReference(BackgroundProperty, "SurfaceBackBrush");
        _root.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackBrush");
    }

    /// <summary>
    /// すでに動いているドリフトは止めない。止めると位置が初期値に戻り、キャッシュも描き直される。
    /// アニメの載せ先（窓全体／リスト内）が変わったときだけ付け替える。
    /// </summary>
    private void ContinueGlowDrift()
    {
        var useWave = UseUnifiedGlow;
        if (_glowDriftRunning && _glowDriftUsesWave == useWave)
        {
            SyncWashTurns();
            return;
        }

        if (_glowDriftRunning)
        {
            StopGlowDrift();
        }

        SyncGlowDrift();
        SyncWashTurns();
    }

    /// <summary>ホストサイズに合わせて拡大するだけ。要素寸法は変えない。</summary>
    private void SyncWaveGlowCover()
    {
        if (_waveGlowHost is null)
        {
            return;
        }

        var width = _waveGlowHost.ActualWidth;
        var height = _waveGlowHost.ActualHeight;
        if (width < 1 || height < 1)
        {
            return;
        }

        Canvas.SetLeft(_waveGlow, (width - GlowCacheWidth) / 2);
        Canvas.SetTop(_waveGlow, (height - GlowCacheHeight) / 2);
        var coverX = (width + (GlowDriftBleed * 2)) / GlowCacheWidth;
        var coverY = (height + (GlowDriftBleed * 2)) / GlowCacheHeight;
        var cover = Math.Max(coverX, coverY);
        if (Math.Abs(_waveGlowCover.ScaleX - cover) < 0.0001
            && Math.Abs(_waveGlowCover.ScaleY - cover) < 0.0001)
        {
            return;
        }

        _waveGlowCover.ScaleX = cover;
        _waveGlowCover.ScaleY = cover;
    }

    private void SyncGlowDrift()
    {
        if (_pdfBackgroundPinned)
        {
            if (_glowDriftRunning)
            {
                StopGlowDrift();
            }

            return;
        }

        var run = _artworkGlow && GlowAnimationAlive();
        if (run == _glowDriftRunning)
        {
            return;
        }

        if (run)
        {
            StartGlowDrift();
        }
        else
        {
            StopGlowDrift();
        }
    }

    private void StartGlowDrift()
    {
        _glowDriftUsesWave = UseUnifiedGlow;
        if (_glowDriftUsesWave)
        {
            AnimateGlowDrift(_waveGlowScale, _waveGlowTranslate);
        }
        else
        {
            AnimateGlowDrift(_glowScale, _glowTranslate);
        }

        _glowDriftRunning = true;
        _glowDriftStartCount++;
    }

    private void AnimateGlowDrift(ScaleTransform scale, TranslateTransform translate)
    {
        var settings = AppStorage.Settings;
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            CreateGlowDriftPulse(
                GlowDriftScaleFrom,
                GlowDriftScaleTo,
                settings.ScaleBackgroundAnimationSeconds(GlowDriftScaleSeconds)));
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            CreateGlowDriftPulse(
                GlowDriftScaleFrom,
                GlowDriftScaleTo,
                settings.ScaleBackgroundAnimationSeconds(GlowDriftScaleSeconds)));
        translate.BeginAnimation(
            TranslateTransform.XProperty,
            CreateGlowDriftPulse(
                -GlowDriftX,
                GlowDriftX,
                settings.ScaleBackgroundAnimationSeconds(GlowDriftXSeconds)));
        translate.BeginAnimation(
            TranslateTransform.YProperty,
            CreateGlowDriftPulse(
                -GlowDriftY,
                GlowDriftY,
                settings.ScaleBackgroundAnimationSeconds(GlowDriftYSeconds)));
    }

    private void StopGlowDrift()
    {
        _glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _glowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _glowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _waveGlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _waveGlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _waveGlowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _waveGlowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _glowScale.ScaleX = GlowDriftScaleFrom;
        _glowScale.ScaleY = GlowDriftScaleFrom;
        _glowTranslate.X = 0;
        _glowTranslate.Y = 0;
        _waveGlowScale.ScaleX = GlowDriftScaleFrom;
        _waveGlowScale.ScaleY = GlowDriftScaleFrom;
        _waveGlowTranslate.X = 0;
        _waveGlowTranslate.Y = 0;
        _glowDriftRunning = false;
    }

    private bool GlowAnimationAlive() =>
        AppStorage.Settings.EnableBackgroundAnimation
        && IsLoaded
        && (IsVisible || (_extendGlow && _waveGlowHost is { Visibility: Visibility.Visible }));

    /// <summary>設定の背景色アニメ ON/OFF・速度をドリフトとウォッシュ回転へ反映する。</summary>
    internal void ApplyBackgroundAnimationSetting()
    {
        // 速度変更時は動き中でも付け直す（Duration は作り直しが必要）。
        if (_glowDriftRunning)
        {
            StopGlowDrift();
        }

        SyncGlowDrift();
        if (_artworkGlow && GlowAnimationAlive() && _washTurns.Length > 1)
        {
            StartWashTurns();
        }
        else
        {
            StopWashTurns();
        }
    }

    private void SyncWashTurns()
    {
        var run = _artworkGlow && GlowAnimationAlive() && _washTurns.Length > 1;
        if (!run)
        {
            StopWashTurns();
            return;
        }

        // 色の回りも、動いているタイマーはリセットしない。
        if (_washTurnTimer.IsEnabled)
        {
            return;
        }

        StartWashTurns();
    }

    private void StartWashTurns()
    {
        _washTurnTimer.Stop();
        _washTurnTimer.Interval = TimeSpan.FromSeconds(
            AppStorage.Settings.ScaleBackgroundAnimationSeconds(GlowWashTurnSeconds));
        _washTurnTimer.Start();
    }

    private void StopWashTurns() => _washTurnTimer.Stop();

    private void AdvanceWashTurn()
    {
        if (_washTurns.Length == 0)
        {
            return;
        }

        _washTurn = (_washTurn + 1) % _washTurns.Length;
        CrossfadeGlowWash(
            _washTurns[_washTurn],
            AppStorage.Settings.ScaleBackgroundAnimationSeconds(GlowWashTurnFadeSeconds));
    }

    internal static DoubleAnimation CreateGlowDriftPulse(double from, double to, double seconds)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(anim, GlowDriftFrameRate);
        return anim;
    }

    internal static DoubleAnimation CreateGlowCrossfade(double from, double to) =>
        CreateGlowCrossfade(from, to, GlowCrossfadeSeconds);

    /// <summary>
    /// 均等パワー（sin/cos）。直線だと中間で特徴色が沈む。
    /// </summary>
    internal static DoubleAnimation CreateGlowCrossfade(double from, double to, double seconds)
    {
        var rising = to > from;
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase
            {
                EasingMode = rising ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };
        Timeline.SetDesiredFrameRate(anim, GlowCrossfadeFrameRate);
        return anim;
    }

    private static bool ArtworkEquals(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.AsSpan().SequenceEqual(right);
    }

    /// <summary>
    /// ジャケットの特徴色で放射グラデの色だまりを重ねた超軽量ウォッシュ。BlurEffect は使わない。
    /// ホストは回さず、90度刻みで描き直した4枚をクロスフェードする。
    /// </summary>
    internal static Brush CreateAmbientWash(BitmapSource source, int quarterTurns = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateWash(SampleArtworkColors(source, 3), quarterTurns);
    }

    internal static Brush[] CreateAmbientWashTurns(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var colors = SampleArtworkColors(source, 3);
        var turns = new Brush[GlowWashTurnSteps];
        for (var i = 0; i < turns.Length; i++)
        {
            turns[i] = CreateWash(colors, i);
        }

        return turns;
    }

    /// <summary>ジャケットが無い、または未読み込みのときのネイビー・シアン・白。</summary>
    internal static Brush CreateFallbackAmbientWash() => CreateFallbackWashTurns()[0];

    internal static Brush[] CreateFallbackWashTurns()
    {
        if (_fallbackWashTurns is not null)
        {
            return _fallbackWashTurns;
        }

        var turns = new Brush[GlowWashTurnSteps];
        for (var i = 0; i < turns.Length; i++)
        {
            turns[i] = CreateFallbackWash(i);
        }

        _fallbackWashTurns = turns;
        return turns;
    }

    private static Brush CreateFallbackWash(int quarterTurns)
    {
        var group = new DrawingGroup();
        var navy = new SolidColorBrush(FallbackWashNavy);
        navy.Freeze();
        group.Children.Add(new GeometryDrawing(navy, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        AddColorBlob(group, FallbackWashCyan, new Point(0.72, 0.38), 0.52, alpha: 200);
        AddColorBlob(group, FallbackWashBlue, new Point(0.30, 0.62), 0.46, alpha: 175);
        AddColorBlob(group, FallbackWashNavy, new Point(0.50, 0.22), 0.58, alpha: 170);
        ApplyWashTurn(group, quarterTurns);
        return FreezeWash(group);
    }

    private static Brush CreateWash(Color[] colors, int quarterTurns)
    {
        var group = new DrawingGroup();
        AddColorBlob(group, colors[0], new Point(0.32, 0.38), 0.55, alpha: 210);
        AddColorBlob(group, colors[1], new Point(0.72, 0.42), 0.50, alpha: 180);
        AddColorBlob(group, colors[2], new Point(0.48, 0.78), 0.58, alpha: 160);
        ApplyWashTurn(group, quarterTurns);
        return FreezeWash(group);
    }

    private static void ApplyWashTurn(DrawingGroup group, int quarterTurns)
    {
        var turns = ((quarterTurns % GlowWashTurnSteps) + GlowWashTurnSteps) % GlowWashTurnSteps;
        if (turns == 0)
        {
            return;
        }

        var rotate = new RotateTransform(turns * 90, 0.5, 0.5);
        rotate.Freeze();
        group.Transform = rotate;
    }

    private static DrawingBrush FreezeWash(DrawingGroup group)
    {
        group.Freeze();
        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.Freeze();
        return brush;
    }

    private static void AddColorBlob(
        DrawingGroup group,
        Color color,
        Point center,
        double radius,
        byte alpha)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = center,
            Center = center,
            RadiusX = radius,
            RadiusY = radius * 0.88,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        // 折れ線だと傾きの折れ目が輪（マッハバンド）に見えるので、滑らかな減衰を細かい停止点で近似する。
        for (var i = 0; i <= WashFalloffStops; i++)
        {
            var t = i / (double)WashFalloffStops;
            var a = (byte)Math.Round(alpha * WashFalloff(t));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(a, color.R, color.G, color.B), t));
        }

        brush.Freeze();
        group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
    }
    internal const int WashFalloffStops = 10;

    /// <summary>色だまりの減衰（中心 1、端 0）。端で傾き 0 になり、途中に折れ目が無い。</summary>
    internal static double WashFalloff(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var u = 1 - t;
        var smooth = 1 - (t * t * (3 - (2 * t)));
        return (0.5 * u * u) + (0.5 * smooth);
    }

    internal const int VeilDitherTileSize = 64;

    /// <summary>ヴェール色の ±この値（8bit）で揺らす。ヴェール不透明度を掛けた分が実際の揺れ幅。</summary>
    internal const int VeilDitherAmplitude = 4;

    private static readonly Dictionary<Color, Brush> VeilDitherBrushes = [];

    /// <summary>
    /// ヴェール色に微小な三角分布ノイズを混ぜたタイル。暗いグラデーションの 8bit の帯を崩す。
    /// 全面に塗っている単色をタイル画像に替えるだけなので、合成の手間はほぼ変わらない。
    /// </summary>
    internal static Brush CreateDitheredVeilBrush(Color veil)
    {
        var key = Color.FromRgb(veil.R, veil.G, veil.B);
        if (VeilDitherBrushes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        const int size = VeilDitherTileSize;
        var pixels = new byte[size * size * 4];
        var random = new Random(0x5A17);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var noise = (int)Math.Round((random.NextDouble() + random.NextDouble() - 1) * VeilDitherAmplitude);
            pixels[i] = (byte)Math.Clamp(key.B + noise, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(key.G + noise, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(key.R + noise, 0, 255);
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        VeilDitherBrushes[key] = brush;
        return brush;
    }

    /// <summary>彩度の高いピクセルから特徴色を最大 count 色取り出す。</summary>
    internal static Color[] SampleArtworkColors(BitmapSource source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        count = Math.Clamp(count, 1, 6);
        var pixels = CopySamplePixels(source, GlowSampleEdge);
        if (pixels.Length < 4)
        {
            return Enumerable.Repeat(PlayerChrome.Get("PlayerFallbackWashNavyBrush"), count).ToArray();
        }

        var candidates = new List<(Color Color, double Score, double Hue)>(pixels.Length / 4);
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];
            if (a < 32)
            {
                continue;
            }

            ToHsl(r, g, b, out var h, out var s, out var l);
            if (s < 0.08 || l is < 0.08 or > 0.92)
            {
                continue;
            }

            // 彩度と中間明度を優先。暗すぎ／白飛びは弱い。
            var score = s * (1.0 - Math.Abs(l - 0.45) * 1.4);
            candidates.Add((BoostAmbient(Color.FromRgb(r, g, b), s, l), score, h));
        }

        if (candidates.Count == 0)
        {
            // ほぼ無彩度なら平均色を少し持ち上げる。
            long sumR = 0, sumG = 0, sumB = 0, n = 0;
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 32)
                {
                    continue;
                }

                sumB += pixels[i];
                sumG += pixels[i + 1];
                sumR += pixels[i + 2];
                n++;
            }

            var avg = n == 0
                ? PlayerChrome.Get("PlayerFallbackWashNavyBrush")
                : BoostAmbient(
                    Color.FromRgb((byte)(sumR / n), (byte)(sumG / n), (byte)(sumB / n)),
                    saturation: 0.35,
                    lightness: 0.45);
            return Enumerable.Repeat(avg, count).ToArray();
        }

        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        var picked = new List<Color>(count);
        foreach (var candidate in candidates)
        {
            if (picked.Any(existing => HueDistance(existing, candidate.Hue) < 28))
            {
                continue;
            }

            picked.Add(candidate.Color);
            if (picked.Count >= count)
            {
                break;
            }
        }

        while (picked.Count < count)
        {
            picked.Add(picked[^1]);
        }

        return picked.ToArray();
    }

    private static byte[] CopySamplePixels(BitmapSource source, int maxEdge)
    {
        maxEdge = Math.Max(4, maxEdge);
        var edge = Math.Max(source.PixelWidth, source.PixelHeight);
        BitmapSource sample = source;
        if (edge > maxEdge)
        {
            var scale = maxEdge / (double)edge;
            var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
            var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
            sample = new TransformedBitmap(source, new ScaleTransform(width / (double)source.PixelWidth, height / (double)source.PixelHeight));
        }

        var converted = new FormatConvertedBitmap(sample, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static Color BoostAmbient(Color color, double saturation, double lightness)
    {
        ToHsl(color.R, color.G, color.B, out var h, out var s, out var l);
        s = Math.Clamp(Math.Max(s, saturation) * 1.25 + 0.08, 0.2, 0.95);
        // 1トーン落として、背面アニメが前面のUIを食わないようにする。
        l = Math.Clamp(Math.Max(l, lightness * 0.9) - 0.06, 0.22, 0.65);
        FromHsl(h, s, l, out var r, out var g, out var b);
        return Color.FromRgb(r, g, b);
    }

    private static double HueDistance(Color color, double hue)
    {
        ToHsl(color.R, color.G, color.B, out var h, out _, out _);
        var d = Math.Abs(h - hue) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static void ToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rf = r / 255d;
        var gf = g / 255d;
        var bf = b / 255d;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        l = (max + min) * 0.5;
        var d = max - min;
        if (d < 1e-6)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == rf)
        {
            h = ((gf - bf) / d + (gf < bf ? 6 : 0)) * 60;
        }
        else if (max == gf)
        {
            h = ((bf - rf) / d + 2) * 60;
        }
        else
        {
            h = ((rf - gf) / d + 4) * 60;
        }
    }

    private static void FromHsl(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        if (s < 1e-6)
        {
            var gray = (byte)Math.Clamp((int)Math.Round(l * 255), 0, 255);
            r = g = b = gray;
            return;
        }

        double Hue(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1d / 6)
            {
                return p + (q - p) * 6 * t;
            }

            if (t < 0.5)
            {
                return q;
            }

            if (t < 2d / 3)
            {
                return p + (q - p) * (2d / 3 - t) * 6;
            }

            return p;
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        var hk = h / 360d;
        r = (byte)Math.Clamp((int)Math.Round(Hue(p, q, hk + 1d / 3) * 255), 0, 255);
        g = (byte)Math.Clamp((int)Math.Round(Hue(p, q, hk) * 255), 0, 255);
        b = (byte)Math.Clamp((int)Math.Round(Hue(p, q, hk - 1d / 3) * 255), 0, 255);
    }

    internal static BitmapSource? TryCreateBitmap(byte[]? bytes, int decodeMaxEdge = 0)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            if (decodeMaxEdge > 0)
            {
                image.DecodePixelWidth = decodeMaxEdge;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    internal BitmapSource? ArtworkForGroup(CollectionViewGroup? group)
    {
        if (_group == LibraryFileGroup.None || group?.Name is not { } name)
        {
            return null;
        }

        if (GroupShowsRowVideoJackets(group))
        {
            return null;
        }

        var key = name.ToString() ?? string.Empty;
        var active = _grid.SelectedItem as LibraryFileRow;
        var activeInGroup = LibraryGroupJacketPick.ContainsActive(group.Items, active);
        if (!activeInGroup && _groupJackets.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!_groupJackets.ContainsKey(key))
        {
            var first = LibraryGroupJacketPick.Resolve(group.Items, active: null);
            _groupJackets[key] = BitmapForGroupRow(first);
        }

        if (activeInGroup)
        {
            return BitmapForGroupRow(LibraryGroupJacketPick.Resolve(group.Items, active));
        }

        return _groupJackets[key];
    }

    private BitmapSource? BitmapForGroupRow(LibraryFileRow? row)
    {
        if (row?.Tag is DocumentSession session
            && session.Document.Artwork is { Length: > 0 } bytes)
        {
            var decoded = TryCreateBitmap(bytes, ArtworkDecodeMaxEdge);
            if (decoded is not null)
            {
                return decoded;
            }
        }

        return null;
    }

    internal static bool GroupShowsRowVideoJackets(CollectionViewGroup group) =>
        LibraryArtworkScan.UsesRowJackets(group.Items);

    internal CollectionViewGroup? PlaylistGroupAt(int index)
    {
        if (_grid.ItemsSource is not ListCollectionView { Groups: { } groups }
            || index < 0
            || index >= groups.Count)
        {
            return null;
        }

        return groups[index] as CollectionViewGroup;
    }

    internal CollectionViewGroup? PlaylistGroupNamed(string name)
    {
        if (_grid.ItemsSource is not ListCollectionView { Groups: { } groups })
        {
            return null;
        }

        foreach (var item in groups)
        {
            if (item is CollectionViewGroup group
                && string.Equals(group.Name?.ToString(), name, StringComparison.Ordinal))
            {
                return group;
            }
        }

        return null;
    }

    private void InvalidateGroupJackets()
    {
        _groupJackets.Clear();
        if (_groupJacketsInvalidateQueued)
        {
            return;
        }

        _groupJacketsInvalidateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _groupJacketsInvalidateQueued = false;
            GroupArtworkChanged?.Invoke();
        }, DispatcherPriority.Background);
    }

    /// <summary>選択行のジャケットへグループ枠を付け替える。1曲目のキャッシュは残す。</summary>
    private void RefreshGroupJacketsForActiveRow()
    {
        if (_group == LibraryFileGroup.None)
        {
            return;
        }

        GroupArtworkChanged?.Invoke();
        _ = EnsureSessionArtworkAsync(SelectedSession, generation: null);
    }

    private async Task EnsureGroupArtworkAsync()
    {
        var gen = ++_groupArtLoad;
        var pending = new List<(DocumentSession Session, string Path)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = SelectedSession;
        var grouped = _group != LibraryFileGroup.None;
        foreach (var row in _rows)
        {
            if (row.Tag is not DocumentSession session
                || session.Document.HasArtwork
                || !LibraryArtworkScan.CanRead(session.Document.SourceKind)
                || session.Document.SourcePath is not { Length: > 0 } path)
            {
                continue;
            }

            if (!LibraryArtworkScan.ShouldLoad(
                    grouped,
                    session.Document.SourceKind,
                    ReferenceEquals(session, selected),
                    row.GroupKey,
                    seen))
            {
                continue;
            }

            pending.Add((session, path));
        }

        foreach (var (session, path) in pending)
        {
            if (gen != _groupArtLoad)
            {
                return;
            }

            await ApplyEmbeddedArtworkAsync(session, path, gen).ConfigureAwait(true);
        }
    }

    private Task EnsureSessionArtworkAsync(DocumentSession? session, int? generation)
    {
        if (session is null
            || session.Document.HasArtwork
            || !LibraryArtworkScan.CanRead(session.Document.SourceKind)
            || session.Document.SourcePath is not { Length: > 0 } path)
        {
            return Task.CompletedTask;
        }

        return ApplyEmbeddedArtworkAsync(session, path, generation);
    }

    private async Task ApplyEmbeddedArtworkAsync(DocumentSession session, string path, int? generation)
    {
        var kind = session.Document.SourceKind;
        byte[] bytes;
        try
        {
            if (LibraryMediaCache.TryGetArtworkProbe(path, out var hasCached))
            {
                bytes = hasCached && LibraryMediaCache.TryGetArtwork(path, out var cached)
                    ? cached
                    : [];
            }
            else
            {
                bytes = LibraryArtworkScan.IsPdfKind(kind)
                    ? await LibraryPdfPages.RenderPngAsync(path, 0, VideoArtwork.Edge).ConfigureAwait(true) ?? []
                    : await Task.Run(() => ReadEmbeddedArtwork(kind, path)).ConfigureAwait(true);
                var artPath = path;
                if (bytes.Length > 0)
                {
                    var copy = bytes;
                    _ = Task.Run(() => LibraryMediaCache.PutArtwork(artPath, copy));
                }
                else
                {
                    _ = Task.Run(() => LibraryMediaCache.PutNoArtwork(artPath));
                }
            }
        }
        catch (Exception)
        {
            return;
        }
        if (generation is { } gen && gen != _groupArtLoad)
        {
            return;
        }

        if (bytes.Length == 0 || session.Document.HasArtwork)
        {
            return;
        }

        session.Document.SetArtwork(bytes);
        UpdateSessionRow(session);
        if (ReferenceEquals(session, SelectedSession))
        {
            SetArtwork(session.Document);
        }
    }
}
