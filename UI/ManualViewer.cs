using System.Diagnostics;
using System.Windows;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// GitHub Pages 上のユーザーマニュアルを既定ブラウザで開く。
/// アプリの表示言語（JP／EN）に合わせて URL を切り替える。
/// </summary>
internal static class ManualViewer
{
    public static void Open(Window? owner)
    {
        try
        {
            var url = UiStrings.IsJapanese
                ? AppVersion.ManualJaUrl
                : AppVersion.ManualEnUrl;
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                owner,
                UiStrings.ErrManualOpenFailed(ex.Message),
                UiStrings.DialogManualTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
