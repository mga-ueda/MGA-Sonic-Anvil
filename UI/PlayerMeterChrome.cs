using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal static class PlayerChrome
{
    /// <summary>ジャケットウォッシュ越しに載せるラッチ面の既定アルファ。</summary>
    internal const byte LatchToggleWashAlpha = 0x8C;

    private static readonly (string Source, string Player)[] LatchToggleWashPairs =
    [
        ("LatchToggleOffBackBrush", "PlayerLatchToggleOffBackBrush"),
        ("LatchToggleOffHoverBackBrush", "PlayerLatchToggleOffHoverBackBrush"),
        ("LatchToggleOnBackBrush", "PlayerLatchToggleOnBackBrush"),
        ("LatchToggleOnHoverBackBrush", "PlayerLatchToggleOnHoverBackBrush"),
    ];

    internal static Color Get(string key) => Get(key, UiThemeService.Painted);

    internal static Color Get(string key, UiTheme theme)
    {
        if (theme == UiThemeService.Painted
            && Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return UiThemePalette.ColorFor(theme, key);
    }

    internal static Brush Brush(string key) => WpfControlHelpers.FrozenBrush(Get(key));

    /// <summary>
    /// ラッチ面色にウォッシュ用アルファを掛ける（RGB はそのまま）。
    /// 色設定でαを下げているときはその値を使い、不透明（255）のときだけ既定ウォッシュαにする。
    /// </summary>
    internal static Color WashLatchToggleFace(Color face) =>
        Color.FromArgb(
            face.A < 255 ? face.A : LatchToggleWashAlpha,
            face.R,
            face.G,
            face.B);

    /// <summary><c>LatchToggle*</c> から <c>PlayerLatchToggle*</c> を半透明に焼成する。</summary>
    internal static void SyncLatchToggleWashBrushes()
    {
        foreach (var (source, player) in LatchToggleWashPairs)
        {
            UiColors.Set(player, WashLatchToggleFace(Get(source)));
        }
    }

    internal static string LatchTogglePlayerKey(string latchToggleKey) => latchToggleKey switch
    {
        "LatchToggleOffBackBrush" => "PlayerLatchToggleOffBackBrush",
        "LatchToggleOffHoverBackBrush" => "PlayerLatchToggleOffHoverBackBrush",
        "LatchToggleOnBackBrush" => "PlayerLatchToggleOnBackBrush",
        "LatchToggleOnHoverBackBrush" => "PlayerLatchToggleOnHoverBackBrush",
        _ => latchToggleKey,
    };
}

/// <summary>プレイヤーのメーター罫線。色設定の Player* ブラシを使い、既定はエディタ色からの焼成。</summary>
internal static class PlayerMeterChrome
{
    internal const string TrackBorderKey = "PlayerLevelMeterTrackBorderBrush";
    internal const string TickKey = "PlayerLevelMeterTickBrush";
    internal const string ScopeGridKey = "PlayerVectorScopeGridBrush";
    internal const string HullKey = "PlayerSurroundHullStrokeBrush";

    internal const double GridTowardWhite = 0.28;
    internal const double GridTowardBlack = 0.24;
    internal const byte TickAlphaLift = 0x28;

    internal static Color Grid(string editorKey, bool player) =>
        player ? PlayerChrome.Get(PlayerKey(editorKey)) : Theme.Get(editorKey);

    internal static string PlayerKey(string editorKey) => editorKey switch
    {
        "LevelMeterTrackBorderBrush" => TrackBorderKey,
        "LevelMeterTickBrush" => TickKey,
        "VectorScopeGridBrush" => ScopeGridKey,
        "SurroundHullStrokeBrush" => HullKey,
        _ => editorKey,
    };

    /// <summary>色設定の既定。エディタ色をプレイヤー用に焼成する。</summary>
    internal static Color Grid(Color source, bool player)
    {
        if (!player)
        {
            return source;
        }

        var darken = IsOpaqueLight(source);
        var t = darken ? GridTowardBlack : GridTowardWhite;
        var toward = darken ? (byte)0 : (byte)255;
        var a = !darken && source.A < 0x90
            ? (byte)Math.Min(0xFF, source.A + TickAlphaLift)
            : source.A;
        return Color.FromArgb(
            a,
            Mix(source.R, toward, t),
            Mix(source.G, toward, t),
            Mix(source.B, toward, t));
    }

    private static bool IsOpaqueLight(Color source)
    {
        if (source.A < 0x90)
        {
            return false;
        }

        return (((0.2126 * source.R) + (0.7152 * source.G) + (0.0722 * source.B)) / 255d) > 0.5;
    }

    private static byte Mix(byte from, byte to, double t) =>
        (byte)Math.Round(from + ((to - from) * t));
}
