using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    private static int EditMask(AudioDocument document, int channel, int channelMask) =>
        ChannelSolo.ResolveMask(channel, channelMask, document.Channels);

    /// <summary>範囲を写像して同じ操作を作り直すレシピを付ける。写像後に空なら適用しない。</summary>
    private static void AttachRangeReplay(
        IEditCommand command,
        int sourceRate,
        WaveSelection range,
        Func<AudioDocument, WaveSelection, IEditCommand?> factory)
    {
        command.Replay = target =>
        {
            var mapped = EditReplay.MapRange(range, sourceRate, target);
            return mapped.IsEmpty ? null : factory(target, mapped);
        };
    }

}
