using System.Windows.Data;
using System.Windows.Markup;

namespace MgaSonicAnvil.UI;

/// <summary>XAML の CornerRadius を <see cref="DesignMetrics.Chrome"/> へ結ぶ。</summary>
internal sealed class ControlCornerExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        DesignMetrics.CornerBinding().ProvideValue(serviceProvider);
}
