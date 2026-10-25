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

    /// <summary>既存 IProgress の区間へ 0..1 を写す。背景スレッドでも SyncContext を汚さない。</summary>
    private sealed class ScaledProgress(IProgress<double> inner, double start, double end) : IProgress<double>
    {
        public void Report(double value)
        {
            var t = Math.Clamp(value, 0, 1);
            inner.Report(start + (end - start) * t);
        }

        public static IProgress<double>? Create(IProgress<double>? inner, double start, double end) =>
            inner is null ? null : new ScaledProgress(inner, start, end);
    }
}
