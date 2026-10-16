using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    /// <summary>
    /// ヒットポイントにマーカーを打つ。解析範囲内の既存マーカーは外し、検出結果で置き換える。
    /// </summary>
    public static IEditCommand? CreateMarkersFromHitPointDetection(
        AudioDocument document,
        WaveSelection range,
        SilenceDetectOptions options,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return null;
        }

        options = options.Normalized();
        var mask = EditMask(document, channel, channelMask);
        var hits = HitPointDetect.CollectFrames(
            document.Interleaved,
            document.Channels,
            range.StartFrame,
            range.EndFrame,
            document.SampleRate,
            mask,
            options);
        if (hits.Count == 0)
        {
            return null;
        }

        var before = document.SnapshotMarkers();
        var after = new List<MarkerSnapshot>(before.Length + hits.Count);
        foreach (var marker in before)
        {
            if (marker.Frame >= range.StartFrame && marker.Frame < range.EndFrame)
            {
                continue;
            }

            after.Add(marker);
        }

        foreach (var frame in hits)
        {
            if (after.Exists(marker => marker.Frame == frame))
            {
                continue;
            }

            after.Add(new MarkerSnapshot(frame, string.Empty));
        }

        after.Sort((left, right) => left.Frame.CompareTo(right.Frame));
        var next = after.ToArray();
        var command = ApplyMarkers(
            document,
            before,
            next,
            "Detect Hit Points");
        if (command is null)
        {
            return null;
        }

        var sourceRate = document.SampleRate;
        command.Replay = target => CreateMarkersFromHitPointDetection(
            target,
            EditReplay.MapRange(range, sourceRate, target),
            options,
            channelMask: mask);
        return command;
    }
}
