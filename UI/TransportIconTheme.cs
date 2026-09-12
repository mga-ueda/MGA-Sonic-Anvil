using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>輸送アイコンはダーク／ライトとも線画。塗りは使わない。</summary>
internal static class TransportIconTheme
{
    public static bool Outline(UiTheme theme)
    {
        _ = theme;
        return true;
    }
}
