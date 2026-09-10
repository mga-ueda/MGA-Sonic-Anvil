using System.Media;
using System.Runtime.InteropServices;
using System.Windows;

namespace MgaSonicAnvil.UI;

/// <summary>
/// オーナー中央のメッセージボックス（WPF）。
/// WPF の <see cref="MessageBox.Show"/> はシステムサウンドを鳴らさないことがあるため、
/// 表示前に OS のシステムイベント音を明示再生する。
/// </summary>
/// <remarks>
/// <see cref="SystemSounds"/>（内部 MessageBeep）は一部の Windows 10/11 で無音になるため、
/// winmm の <c>PlaySound</c> + システム別名（SystemAsterisk 等）を使う。
/// </remarks>
internal static class OwnerCenteredMessageBox
{
    private const uint SndAsync = 0x0001;
    private const uint SndAlias = 0x00010000;
    private const uint SndSystem = 0x00200000;

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        PlayFor(icon);

        if (owner is null)
        {
            return MessageBox.Show(text, caption, buttons, icon, defaultResult);
        }

        return MessageBox.Show(owner, text, caption, buttons, icon, defaultResult);
    }

    /// <summary>
    /// <see cref="MessageBoxImage"/> に対応する Windows システムイベント音を再生する。
    /// （Error/Hand、Warning/Exclamation、Information/Asterisk は同一値）。
    /// </summary>
    public static void PlayFor(MessageBoxImage icon) => PlaySystemSound(icon);

    private static void PlaySystemSound(MessageBoxImage icon)
    {
        // Control Panel「サウンド」に登録されているシステムイベント別名。
        var alias = icon switch
        {
            MessageBoxImage.Error => "SystemHand",
            MessageBoxImage.Question => "SystemQuestion",
            MessageBoxImage.Warning => "SystemExclamation",
            MessageBoxImage.Information => "SystemAsterisk",
            _ => null,
        };
        if (alias is null)
        {
            return;
        }

        // SND_NODEFAULT は付けない: 別名に音が未割当でも既定のシステム音へ落とす。
        if (PlaySound(alias, IntPtr.Zero, SndAlias | SndAsync | SndSystem))
        {
            return;
        }

        // PlaySound が使えない環境向けフォールバック
        switch (icon)
        {
            case MessageBoxImage.Error:
                SystemSounds.Hand.Play();
                break;
            case MessageBoxImage.Question:
                SystemSounds.Question.Play();
                break;
            case MessageBoxImage.Warning:
                SystemSounds.Exclamation.Play();
                break;
            case MessageBoxImage.Information:
                SystemSounds.Asterisk.Play();
                break;
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? sound, IntPtr module, uint soundFlags);
}
