using System.Windows;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// フェード／音量／ピッチ／タイムストレッチ／フォーマット試聴の開始・失敗ロールバック共通処理。
/// </summary>
internal static class EffectPreviewSession
{
    public static void MarkStarted(EffectPreviewState state, bool hideHostTransport = false)
    {
        state.Previewing = true;
        state.StartedAt = Environment.TickCount64;
        if (hideHostTransport)
        {
            state.HideHostTransport = true;
        }
    }

    public static void Fail(
        Window owner,
        EffectPreviewState state,
        Exception ex,
        Action pauseSoft,
        bool clearHideHostTransport = false,
        Action? extraCleanup = null)
    {
        state.Previewing = false;
        if (clearHideHostTransport)
        {
            state.HideHostTransport = false;
        }

        extraCleanup?.Invoke();
        pauseSoft();
        OwnerCenteredMessageBox.Show(
            owner,
            ex.Message,
            UiStrings.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
