using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>履歴など疑似ウィンドウの右上 ×。</summary>
internal static class OverlayCaption
{
    public const double CornerInset = 4;

    public static Thickness CornerMargin { get; } = new(0, CornerInset, CornerInset, 0);

    public static void PinCorner(FrameworkElement close)
    {
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = CornerMargin;
    }

    public static Border CloseButton(Action onClose)
    {
        var mark = new CloseMark();
        mark.SetResourceReference(CloseMark.ForegroundProperty, "MutedForeBrush");
        var hit = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = mark,
            Style = CloseStyle(),
        };
        hit.MouseEnter += (_, _) =>
            mark.SetResourceReference(CloseMark.ForegroundProperty, "PrimaryForeBrush");
        hit.MouseLeave += (_, _) =>
            mark.SetResourceReference(CloseMark.ForegroundProperty, "MutedForeBrush");
        hit.MouseLeftButtonDown += (_, e) => e.Handled = true;
        hit.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClose();
        };
        TipService.Set(hit, UiStrings.OverlayClose);
        return hit;
    }

    private static Style CloseStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ColorPanelBackBrush")));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ChromeBorderBrush")));
        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("ScrollThumbBackBrush")));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("ControlHoverBorderBrush")));
        style.Triggers.Add(hover);
        return style;
    }

    private sealed class CloseMark : FrameworkElement
    {
        public static readonly DependencyProperty ForegroundProperty =
            TextBlock.ForegroundProperty.AddOwner(
                typeof(CloseMark),
                new FrameworkPropertyMetadata(
                    Brushes.Black,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (w < 2 || h < 2)
            {
                return;
            }

            var inset = Math.Min(w, h) * 0.30;
            var pen = new Pen(Foreground, 1.2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();
            dc.DrawLine(pen, new Point(inset, inset), new Point(w - inset, h - inset));
            dc.DrawLine(pen, new Point(w - inset, inset), new Point(inset, h - inset));
        }
    }
}
