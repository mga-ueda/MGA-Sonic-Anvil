using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>等分の連続操作をドキュメント状態と突き合わせる。</summary>
internal static class DocumentRangeDivide
{
    /// <summary>連続操作中は state。無ければ同じ種類の両端が既にあれば 1 回目済みとみなす。</summary>
    public static int ResolvePreviousParts(
        RangeDivideState? state,
        AudioDocument document,
        WaveSelection range,
        bool regions)
    {
        if (state is { } current && current.Matches(document, range))
        {
            return current.Parts;
        }

        return (regions ? HasRegionsAtEnds(document, range) : HasMarkersAtEnds(document, range))
            ? 1
            : 0;
    }

    /// <summary>選択の両端にマーカーがあれば、マーカーの両端打ちは済んでいる。</summary>
    public static bool HasMarkersAtEnds(AudioDocument document, WaveSelection range) =>
        RangeDivide.HasMarkersAtEnds(range, document.HasMarkerAt);

    /// <summary>選択の両端にリージョン端があれば、リージョン 1 本は済んでいる。</summary>
    public static bool HasRegionsAtEnds(AudioDocument document, WaveSelection range) =>
        RangeDivide.HasRegionsAtEnds(range, document.Regions);
}

internal readonly record struct RangeDivideState(
    AudioDocument Document,
    long StartFrame,
    long EndFrame,
    int Parts)
{
    public bool Matches(AudioDocument document, WaveSelection range) =>
        ReferenceEquals(Document, document)
        && StartFrame == range.StartFrame
        && EndFrame == range.EndFrame
        && Parts > 0;
}
