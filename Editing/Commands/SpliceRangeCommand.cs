using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class SpliceRangeCommand : IEditCommand
{
    private readonly long _startFrame;
    private readonly float[] _before;
    private readonly float[] _after;
    private readonly WaveSelection _selectionBefore;
    private readonly WaveSelection _selectionAfter;
    private readonly long _cursorBefore;
    private readonly long _cursorAfter;
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly MarkerSnapshot[] _markersAfter;
    private readonly WaveSelection _sampleLoopBefore;
    private readonly WaveSelection _sampleLoopAfter;
    private readonly WaveRegion[] _regionsBefore;
    private readonly WaveRegion[] _regionsAfter;

    public SpliceRangeCommand(
        string name,
        long startFrame,
        float[] before,
        float[] after,
        WaveSelection selectionBefore,
        WaveSelection selectionAfter,
        long cursorBefore,
        long cursorAfter,
        MarkerSnapshot[] markersBefore,
        MarkerSnapshot[] markersAfter,
        WaveSelection sampleLoopBefore,
        WaveSelection sampleLoopAfter,
        WaveRegion[] regionsBefore,
        WaveRegion[] regionsAfter,
        string? summary = null)
    {
        Name = name;
        Summary = string.IsNullOrWhiteSpace(summary) ? UiStrings.EditHistoryName(name) : summary;
        _startFrame = startFrame;
        _before = before;
        _after = after;
        _selectionBefore = selectionBefore;
        _selectionAfter = selectionAfter;
        _cursorBefore = cursorBefore;
        _cursorAfter = cursorAfter;
        _markersBefore = markersBefore;
        _markersAfter = markersAfter;
        _sampleLoopBefore = sampleLoopBefore;
        _sampleLoopAfter = sampleLoopAfter;
        _regionsBefore = regionsBefore;
        _regionsAfter = regionsAfter;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        var oldFrames = _before.Length / Math.Max(1, document.Channels);
        document.SpliceRange(_startFrame, oldFrames, _after);
        document.ReplaceMarkers(_markersAfter, markDirty: false);
        document.SetSampleLoop(_sampleLoopAfter, markDirty: false);
        document.SetRegions(_regionsAfter, markDirty: false);
        document.Selection = _selectionAfter;
        document.CursorFrame = _cursorAfter;
    }

    public void Revert(AudioDocument document)
    {
        var newFrames = _after.Length / Math.Max(1, document.Channels);
        document.SpliceRange(_startFrame, newFrames, _before);
        document.ReplaceMarkers(_markersBefore, markDirty: false);
        document.SetSampleLoop(_sampleLoopBefore, markDirty: false);
        document.SetRegions(_regionsBefore, markDirty: false);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}
