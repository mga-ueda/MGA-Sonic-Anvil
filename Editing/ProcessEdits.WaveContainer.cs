using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    /// <summary>
    /// ヘッダ修復／チャンク削除／チャンネルマスクの作業 WAVE を差し替える。
    /// before／after は SourcePath ではなく作業コピー（null＝元ファイルを直接参照）。
    /// <paramref name="afterChannelMask"/> が null のときは今のマスクを維持する。
    /// </summary>
    public static IEditCommand? ReplaceWaveContainer(
        AudioDocument document,
        string? beforeContainerPath,
        string? afterContainerPath,
        string historyName,
        int? afterChannelMask = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.Equals(beforeContainerPath, afterContainerPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(afterContainerPath))
        {
            return null;
        }

        var beforeMask = document.ChannelMask;
        var afterMask = afterChannelMask ?? beforeMask;
        var summary = UiStrings.EditHistoryName(historyName);
        return new ReplaceWaveContainerCommand(
            beforeContainerPath,
            afterContainerPath,
            summary,
            beforeMask,
            afterMask)
        {
            // 別タブへの履歴ペーストはパス依存なので不可。
            Replay = null,
            Persist = null,
        };
    }
}
