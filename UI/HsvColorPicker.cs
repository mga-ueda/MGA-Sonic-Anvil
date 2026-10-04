using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>彩度×明度の正方形、色相バー、明暗、Hex / RGB / 透明度。ドラッグ中はライブで色が変わる。</summary>
internal sealed class HsvColorPicker : Grid
{
    private const double ChannelValueWidth = 56;

    private readonly SvBoard _sv = new();
    private readonly HueBar _hue = new();
    private readonly BrightnessBar _brightness = new();
    private readonly ChannelBar _red = new(Channel.R);
    private readonly ChannelBar _green = new(Channel.G);
    private readonly ChannelBar _blue = new(Channel.B);
    private readonly ChannelBar _alpha = new(Channel.A);
    private readonly TextBlock _hexCaption = new();
    private readonly TextBlock _brightnessCaption = new();
    private readonly TextBlock _alphaCaption = new();
    private readonly TextBox _hexBox = new();
    private readonly TextBox _rBox = new();
    private readonly TextBox _gBox = new();
    private readonly TextBox _bBox = new();
    private readonly TextBox _aBox = new();
    private readonly Border _previewBack = new();
    private readonly Border _preview = new();
    private FrameworkElement? _alphaRow;
    private bool _suppressFields;
    private HsvColor _hsv = new(0d, 0d, 1d);
    private byte _alphaValue = 255;

    public event EventHandler? ColorChanged;

    public event EventHandler? ColorCommitted;

    public HsvColorPicker()
    {
        Build();
        _sv.Moved += (_, _) => SetHsv(_hsv.WithSaturationValue(_sv.Saturation, _sv.Value), raise: true);
        _hue.Moved += (_, _) => SetHsv(_hsv.WithHue(_hue.Hue), raise: true);
        _brightness.Moved += (_, _) => SetHsv(_hsv.WithSaturationValue(_hsv.S, _brightness.Value), raise: true);
        _red.Moved += (_, _) => SetRgbChannel(Channel.R, _red.Value);
        _green.Moved += (_, _) => SetRgbChannel(Channel.G, _green.Value);
        _blue.Moved += (_, _) => SetRgbChannel(Channel.B, _blue.Value);
        _alpha.Moved += (_, _) => SetAlpha(_alpha.Value);
        _sv.Committed += (_, _) => ColorCommitted?.Invoke(this, EventArgs.Empty);
        _hue.Committed += (_, _) => ColorCommitted?.Invoke(this, EventArgs.Empty);
        _brightness.Committed += (_, _) => ColorCommitted?.Invoke(this, EventArgs.Empty);
        _red.Committed += (_, _) => ColorCommitted?.Invoke(this, EventArgs.Empty);
        _green.Committed += (_, _) => ColorCommitted?.Invoke(this, EventArgs.Empty);
        _blue.Committed += (_, _) => ColorCommitted?.Invoke(this, EventArgs.Empty);
        _alpha.Committed += (_, _) => ColorCommitted?.Invoke(this, EventArgs.Empty);
        _hexBox.LostFocus += (_, _) => TryApplyHex(_hexBox.Text, commit: true);
        _hexBox.TextChanged += (_, _) => TryApplyHex(_hexBox.Text, commit: false);
        _hexBox.KeyDown += Field_KeyDown;
        _rBox.LostFocus += (_, _) => TryApplyChannel(_rBox, Channel.R, commit: true);
        _gBox.LostFocus += (_, _) => TryApplyChannel(_gBox, Channel.G, commit: true);
        _bBox.LostFocus += (_, _) => TryApplyChannel(_bBox, Channel.B, commit: true);
        _aBox.LostFocus += (_, _) => TryApplyChannel(_aBox, Channel.A, commit: true);
        _rBox.TextChanged += (_, _) => TryApplyChannel(_rBox, Channel.R, commit: false);
        _gBox.TextChanged += (_, _) => TryApplyChannel(_gBox, Channel.G, commit: false);
        _bBox.TextChanged += (_, _) => TryApplyChannel(_bBox, Channel.B, commit: false);
        _aBox.TextChanged += (_, _) => TryApplyChannel(_aBox, Channel.A, commit: false);
        _rBox.KeyDown += Field_KeyDown;
        _gBox.KeyDown += Field_KeyDown;
        _bBox.KeyDown += Field_KeyDown;
        _aBox.KeyDown += Field_KeyDown;
        SetHsv(_hsv, raise: false);
        ApplyLocalizedText();
        RefreshChrome();
    }

    public Color Color
    {
        get
        {
            var rgb = _hsv.ToRgb();
            return Color.FromArgb(_alphaValue, rgb.R, rgb.G, rgb.B);
        }
    }

    public void SetColor(Color color)
    {
        _alphaValue = color.A;
        var next = HsvColor.FromRgb(color);
        if (next.S < 0.001)
        {
            next = new HsvColor(_hsv.H, next.S, next.V);
        }

        SetHsv(next, raise: false);
    }

    public void ApplyLocalizedText()
    {
        _hexCaption.Text = UiStrings.ColorDevHex;
        _brightnessCaption.Text = UiStrings.ColorDevBrightness;
        _alphaCaption.Text = UiStrings.ColorDevAlpha;
        TipService.Set(this, UiStrings.ColorDevPickHint);
        TipService.Set(_alphaRow ?? _aBox, UiStrings.ColorDevPickHint);
        TipService.Set(_hexBox, UiStrings.ColorDevPickHint);
    }

    public void RefreshChrome()
    {
        StyleField(_hexBox);
        StyleField(_rBox);
        StyleField(_gBox);
        StyleField(_bBox);
        StyleField(_aBox);
        _hexCaption.Foreground = ThemeBrush("MutedForeBrush");
        _brightnessCaption.Foreground = ThemeBrush("MutedForeBrush");
        _alphaCaption.Foreground = ThemeBrush("MutedForeBrush");
        _preview.BorderBrush = ThemeBrush("ChromeBorderBrush");
        _previewBack.Background = CheckerboardBrush();
        _sv.InvalidateVisual();
        _hue.InvalidateVisual();
        _brightness.InvalidateVisual();
        _red.InvalidateVisual();
        _green.InvalidateVisual();
        _blue.InvalidateVisual();
        _alpha.InvalidateVisual();
    }

    internal static (double S, double V) SvFromPoint(double x, double y, Size size)
    {
        if (size.Width <= 0d || size.Height <= 0d)
        {
            return (0d, 0d);
        }

        return (
            Math.Clamp(x / size.Width, 0d, 1d),
            Math.Clamp(1d - y / size.Height, 0d, 1d));
    }

    internal static double HueFromPoint(double x, double width) =>
        width <= 0d ? 0d : Math.Clamp(x / width, 0d, 1d) * 360d;

    internal static double ChannelFromPoint(double x, double width) =>
        width <= 0d ? 0d : Math.Clamp(x / width, 0d, 1d) * 255d;

    internal static double UnitFromPoint(double x, double width) =>
        width <= 0d ? 0d : Math.Clamp(x / width, 0d, 1d);

    private void Build()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(176) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _sv.MinHeight = 140;
        _hue.Height = 18;
        _hue.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(_sv);
        Children.Add(_hue);
        SetRow(_hue, 1);

        var hexRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hexCaption.VerticalAlignment = VerticalAlignment.Center;
        _hexCaption.Margin = new Thickness(0, 0, 8, 0);
        _hexBox.FontFamily = new FontFamily("Consolas");
        _hexBox.MaxLength = 9;
        _hexBox.Margin = new Thickness(0, 0, 8, 0);
        _previewBack.Width = 36;
        _previewBack.Height = 28;
        _previewBack.CornerRadius = new CornerRadius(6);
        _previewBack.Background = CheckerboardBrush();
        _preview.Width = 36;
        _preview.Height = 28;
        _preview.CornerRadius = new CornerRadius(6);
        _preview.BorderThickness = new Thickness(1);
        var previewHost = new Grid();
        previewHost.Children.Add(_previewBack);
        previewHost.Children.Add(_preview);
        hexRow.Children.Add(_hexCaption);
        hexRow.Children.Add(_hexBox);
        hexRow.Children.Add(previewHost);
        SetColumn(_hexBox, 1);
        SetColumn(previewHost, 2);
        Children.Add(hexRow);
        SetRow(hexRow, 2);

        Children.Add(BrightnessRow(3, new Thickness(0, 10, 0, 0)));
        Children.Add(ChannelRow("R", _red, _rBox, 4, new Thickness(0, 10, 0, 0)));
        Children.Add(ChannelRow("G", _green, _gBox, 5, new Thickness(0, 6, 0, 0)));
        Children.Add(ChannelRow("B", _blue, _bBox, 6, new Thickness(0, 6, 0, 0)));
        _alphaRow = LabeledChannelRow(_alphaCaption, _alpha, _aBox, 7, new Thickness(0, 6, 0, 0));
        Children.Add(_alphaRow);
    }

    private FrameworkElement BrightnessRow(int row, Thickness margin)
    {
        var host = new Grid { Margin = margin };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _brightnessCaption.VerticalAlignment = VerticalAlignment.Center;
        _brightnessCaption.Margin = new Thickness(0, 0, 8, 0);
        _brightnessCaption.FontWeight = FontWeights.SemiBold;
        _brightness.Height = 16;
        _brightness.Margin = new Thickness(0, 2, 0, 2);
        host.Children.Add(_brightnessCaption);
        host.Children.Add(_brightness);
        SetColumn(_brightness, 1);
        SetRow(host, row);
        return host;
    }

    private FrameworkElement ChannelRow(string caption, ChannelBar bar, TextBox box, int row, Thickness margin)
    {
        var label = new TextBlock
        {
            Text = caption,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Width = 18,
        };
        return LabeledChannelRow(label, bar, box, row, margin, labelWidth: 18);
    }

    private FrameworkElement LabeledChannelRow(
        FrameworkElement label,
        ChannelBar bar,
        TextBox box,
        int row,
        Thickness margin,
        double labelWidth = double.NaN)
    {
        var host = new Grid { Margin = margin };
        host.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = double.IsNaN(labelWidth) ? GridLength.Auto : new GridLength(labelWidth),
        });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ChannelValueWidth) });
        if (label is TextBlock text)
        {
            text.VerticalAlignment = VerticalAlignment.Center;
            text.FontWeight = FontWeights.SemiBold;
            text.Margin = new Thickness(0, 0, 8, 0);
        }

        bar.Height = 16;
        bar.Margin = new Thickness(0, 2, 8, 2);
        box.FontFamily = new FontFamily("Consolas");
        box.MaxLength = 3;
        box.TextAlignment = TextAlignment.Center;
        host.Children.Add(label);
        host.Children.Add(bar);
        host.Children.Add(box);
        SetColumn(bar, 1);
        SetColumn(box, 2);
        SetRow(host, row);
        return host;
    }

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (sender == _hexBox)
        {
            TryApplyHex(_hexBox.Text, commit: true);
            return;
        }

        if (sender == _rBox)
        {
            TryApplyChannel(_rBox, Channel.R, commit: true);
        }
        else if (sender == _gBox)
        {
            TryApplyChannel(_gBox, Channel.G, commit: true);
        }
        else if (sender == _bBox)
        {
            TryApplyChannel(_bBox, Channel.B, commit: true);
        }
        else if (sender == _aBox)
        {
            TryApplyChannel(_aBox, Channel.A, commit: true);
        }
    }

    private void TryApplyHex(string text, bool commit)
    {
        if (_suppressFields)
        {
            return;
        }

        if (!UiColors.TryParseColor(text, out var parsed))
        {
            if (commit)
            {
                WriteFields();
            }

            return;
        }

        var hex = text.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        // 6 桁は RGB だけ差し替え、いまの透明度を保つ。8 桁はアルファ込み。
        _alphaValue = hex.Length == 8 ? parsed.A : _alphaValue;
        var next = HsvColor.FromRgb(parsed);
        if (next.S < 0.001)
        {
            next = new HsvColor(_hsv.H, next.S, next.V);
        }

        SetHsv(next, raise: true);
        if (commit)
        {
            ColorCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TryApplyChannel(TextBox box, Channel channel, bool commit)
    {
        if (_suppressFields)
        {
            return;
        }

        if (!byte.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            if (commit)
            {
                WriteFields();
            }

            return;
        }

        if (channel == Channel.A)
        {
            SetAlpha(value);
        }
        else
        {
            SetRgbChannel(channel, value);
        }

        if (commit)
        {
            ColorCommitted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetAlpha(double value)
    {
        _alphaValue = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        SetHsv(_hsv, raise: true);
    }

    private void SetRgbChannel(Channel channel, double value)
    {
        var rgb = _hsv.ToRgb();
        var byteValue = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
        var next = channel switch
        {
            Channel.R => Color.FromRgb(byteValue, rgb.G, rgb.B),
            Channel.G => Color.FromRgb(rgb.R, byteValue, rgb.B),
            _ => Color.FromRgb(rgb.R, rgb.G, byteValue),
        };
        var hsv = HsvColor.FromRgb(next);
        if (hsv.S < 0.001)
        {
            hsv = new HsvColor(_hsv.H, hsv.S, hsv.V);
        }

        SetHsv(hsv, raise: true);
    }

    private void SetHsv(HsvColor hsv, bool raise)
    {
        _hsv = hsv;
        var rgb = hsv.ToRgb();
        var argb = Color.FromArgb(_alphaValue, rgb.R, rgb.G, rgb.B);
        _sv.SetHsv(hsv);
        _hue.Hue = hsv.H;
        _brightness.Set(hsv);
        _red.Set(argb, rgb.R);
        _green.Set(argb, rgb.G);
        _blue.Set(argb, rgb.B);
        _alpha.Set(argb, _alphaValue);
        _preview.Background = UiColors.Brush(argb);
        WriteFields();
        if (raise)
        {
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void WriteFields()
    {
        if (_suppressFields)
        {
            return;
        }

        _suppressFields = true;
        try
        {
            var color = Color;
            _hexBox.Text = UiColors.FormatColor(color);
            _rBox.Text = color.R.ToString(CultureInfo.InvariantCulture);
            _gBox.Text = color.G.ToString(CultureInfo.InvariantCulture);
            _bBox.Text = color.B.ToString(CultureInfo.InvariantCulture);
            _aBox.Text = color.A.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressFields = false;
        }
    }

    private static void StyleField(TextBox box)
    {
        if (Application.Current?.TryFindResource("DarkTextBoxStyle") is Style style)
        {
            box.Style = style;
        }

        box.Padding = new Thickness(4, 2, 4, 2);
        box.MinHeight = 28;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        ImeComposition.Disable(box);
    }

    private static Brush ThemeBrush(string key) => WpfControlHelpers.FrozenBrush(Theme.Get(key));

    internal static Brush CheckerboardBrush()
    {
        const double cell = 6d;
        var group = new DrawingGroup();
        var light = WpfControlHelpers.FrozenBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
        var dark = WpfControlHelpers.FrozenBrush(Color.FromRgb(0x88, 0x88, 0x88));
        using (var dc = group.Open())
        {
            dc.DrawRectangle(light, null, new Rect(0, 0, cell * 2, cell * 2));
            dc.DrawRectangle(dark, null, new Rect(0, 0, cell, cell));
            dc.DrawRectangle(dark, null, new Rect(cell, cell, cell, cell));
        }

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell * 2, cell * 2),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    private enum Channel
    {
        R,
        G,
        B,
        A,
    }

    private abstract class DragSurface : FrameworkElement
    {
        public event EventHandler? Moved;

        public event EventHandler? Committed;

        protected DragSurface()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Focusable = false;
            Cursor = Cursors.Hand;
        }

        protected abstract void DragTo(Point point);

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            CaptureMouse();
            DragTo(e.GetPosition(this));
            Moved?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            base.OnMouseLeftButtonDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (IsMouseCaptured)
            {
                DragTo(e.GetPosition(this));
                Moved?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
                Committed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }

            base.OnMouseLeftButtonUp(e);
        }
    }

    private sealed class SvBoard : DragSurface
    {
        private HsvColor _hsv = new(0d, 0d, 1d);

        public double Saturation => _hsv.S;

        public double Value => _hsv.V;

        public void SetHsv(HsvColor hsv)
        {
            _hsv = hsv;
            InvalidateVisual();
        }

        protected override void DragTo(Point point)
        {
            var (s, v) = SvFromPoint(point.X, point.Y, RenderSize);
            _hsv = _hsv.WithSaturationValue(s, v);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(RenderSize);
            if (bounds.Width <= 0d || bounds.Height <= 0d)
            {
                return;
            }

            var clip = WpfControlHelpers.RoundedRectGeometry(bounds, 8);
            dc.PushClip(clip);
            var hue = _hsv.HueRgb();
            var sat = new LinearGradientBrush(Colors.White, hue, new Point(0, 0.5), new Point(1, 0.5));
            sat.Freeze();
            var val = new LinearGradientBrush(
                Color.FromArgb(0, 0, 0, 0),
                Colors.Black,
                new Point(0.5, 0),
                new Point(0.5, 1));
            val.Freeze();
            dc.DrawRectangle(sat, null, bounds);
            dc.DrawRectangle(val, null, bounds);
            dc.Pop();

            var border = new Pen(ThemeBrush("ChromeBorderBrush"), 1);
            if (border.CanFreeze)
            {
                border.Freeze();
            }

            dc.DrawGeometry(null, border, clip);
            DrawThumb(dc, _hsv.S * bounds.Width, (1d - _hsv.V) * bounds.Height, _hsv.ToRgb());
        }
    }

    private sealed class HueBar : DragSurface
    {
        private static readonly GradientStopCollection Stops = CreateStops();

        public double Hue { get; set; }

        protected override void DragTo(Point point)
        {
            Hue = HueFromPoint(point.X, RenderSize.Width);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(RenderSize);
            if (bounds.Width <= 0d || bounds.Height <= 0d)
            {
                return;
            }

            var fill = new LinearGradientBrush(Stops, new Point(0, 0.5), new Point(1, 0.5));
            fill.Freeze();
            var clip = WpfControlHelpers.RoundedRectGeometry(bounds, 5);
            dc.PushClip(clip);
            dc.DrawRectangle(fill, null, bounds);
            dc.Pop();
            var border = new Pen(ThemeBrush("ChromeBorderBrush"), 1);
            if (border.CanFreeze)
            {
                border.Freeze();
            }

            dc.DrawGeometry(null, border, clip);
            var x = Hue / 360d * bounds.Width;
            DrawThumb(dc, x, bounds.Height * 0.5, new HsvColor(Hue, 1d, 1d).ToRgb());
        }

        private static GradientStopCollection CreateStops()
        {
            var stops = new GradientStopCollection
            {
                new(Color.FromRgb(255, 0, 0), 0d),
                new(Color.FromRgb(255, 255, 0), 1d / 6d),
                new(Color.FromRgb(0, 255, 0), 2d / 6d),
                new(Color.FromRgb(0, 255, 255), 3d / 6d),
                new(Color.FromRgb(0, 0, 255), 4d / 6d),
                new(Color.FromRgb(255, 0, 255), 5d / 6d),
                new(Color.FromRgb(255, 0, 0), 1d),
            };
            stops.Freeze();
            return stops;
        }
    }

    private sealed class BrightnessBar : DragSurface
    {
        private HsvColor _hsv = new(0d, 0d, 1d);

        public double Value { get; private set; } = 1d;

        public void Set(HsvColor hsv)
        {
            _hsv = hsv;
            Value = hsv.V;
            InvalidateVisual();
        }

        protected override void DragTo(Point point)
        {
            Value = UnitFromPoint(point.X, RenderSize.Width);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(RenderSize);
            if (bounds.Width <= 0d || bounds.Height <= 0d)
            {
                return;
            }

            var dark = new HsvColor(_hsv.H, _hsv.S, 0d).ToRgb();
            var bright = new HsvColor(_hsv.H, _hsv.S, 1d).ToRgb();
            var fill = new LinearGradientBrush(dark, bright, new Point(0, 0.5), new Point(1, 0.5));
            fill.Freeze();
            var clip = WpfControlHelpers.RoundedRectGeometry(bounds, 4);
            dc.PushClip(clip);
            dc.DrawRectangle(fill, null, bounds);
            dc.Pop();
            var border = new Pen(ThemeBrush("ChromeBorderBrush"), 1);
            if (border.CanFreeze)
            {
                border.Freeze();
            }

            dc.DrawGeometry(null, border, clip);
            DrawThumb(dc, Value * bounds.Width, bounds.Height * 0.5, _hsv.ToRgb());
        }
    }

    private sealed class ChannelBar : DragSurface
    {
        private readonly Channel _channel;
        private Color _rgb = Colors.White;

        public ChannelBar(Channel channel) => _channel = channel;

        public double Value { get; private set; }

        public void Set(Color rgb, byte value)
        {
            _rgb = rgb;
            Value = value;
            InvalidateVisual();
        }

        protected override void DragTo(Point point)
        {
            Value = ChannelFromPoint(point.X, RenderSize.Width);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(RenderSize);
            if (bounds.Width <= 0d || bounds.Height <= 0d)
            {
                return;
            }

            var clip = WpfControlHelpers.RoundedRectGeometry(bounds, 4);
            dc.PushClip(clip);
            if (_channel == Channel.A)
            {
                dc.DrawRectangle(CheckerboardBrush(), null, bounds);
                var start = Color.FromArgb(0, _rgb.R, _rgb.G, _rgb.B);
                var end = Color.FromArgb(255, _rgb.R, _rgb.G, _rgb.B);
                var alphaFill = new LinearGradientBrush(start, end, new Point(0, 0.5), new Point(1, 0.5));
                alphaFill.Freeze();
                dc.DrawRectangle(alphaFill, null, bounds);
            }
            else
            {
                var start = _channel switch
                {
                    Channel.R => Color.FromRgb(0, _rgb.G, _rgb.B),
                    Channel.G => Color.FromRgb(_rgb.R, 0, _rgb.B),
                    _ => Color.FromRgb(_rgb.R, _rgb.G, 0),
                };
                var end = _channel switch
                {
                    Channel.R => Color.FromRgb(255, _rgb.G, _rgb.B),
                    Channel.G => Color.FromRgb(_rgb.R, 255, _rgb.B),
                    _ => Color.FromRgb(_rgb.R, _rgb.G, 255),
                };
                var fill = new LinearGradientBrush(start, end, new Point(0, 0.5), new Point(1, 0.5));
                fill.Freeze();
                dc.DrawRectangle(fill, null, bounds);
            }

            dc.Pop();
            var border = new Pen(ThemeBrush("ChromeBorderBrush"), 1);
            if (border.CanFreeze)
            {
                border.Freeze();
            }

            dc.DrawGeometry(null, border, clip);
            var thumb = _channel == Channel.A
                ? Color.FromArgb(255, _rgb.R, _rgb.G, _rgb.B)
                : Color.FromRgb(_rgb.R, _rgb.G, _rgb.B);
            DrawThumb(dc, Value / 255d * bounds.Width, bounds.Height * 0.5, thumb);
        }
    }

    private static void DrawThumb(DrawingContext dc, double x, double y, Color fill)
    {
        var center = new Point(x, y);
        var outer = new Pen(Brushes.White, 2);
        outer.Freeze();
        var ring = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(180, 0, 0, 0)), 1);
        ring.Freeze();
        dc.DrawEllipse(WpfControlHelpers.FrozenBrush(fill), outer, center, 7, 7);
        dc.DrawEllipse(null, ring, center, 8, 8);
    }
}
