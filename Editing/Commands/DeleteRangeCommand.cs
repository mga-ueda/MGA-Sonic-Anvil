using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class DeleteRangeCommand : IEditCommand
{
    private readonly long _startFrame;
    private readonly float[] _removed;
    private readonly WaveSelection _selectionBefore;
    private readonly long _cursorBefore;
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly WaveSelection _sampleLoopBefore;
    private readonly WaveRegion[] _regionsBefore;

    public DeleteRangeCommand(
        long startFrame,
        float[] removed,
        WaveSelection selectionBefore,
        long cursorBefore,
        MarkerSnapshot[] markersBefore,
        WaveSelection sampleLoopBefore,
        WaveRegion[] regionsBefore,
        string summary)
    {
        _startFrame = startFrame;
        _removed = removed;
        _selectionBefore = selectionBefore;
        _cursorBefore = cursorBefore;
        _markersBefore = markersBefore;
        _sampleLoopBefore = sampleLoopBefore;
        _regionsBefore = regionsBefore;
        Summary = summary;
    }

    public string Name => "Delete";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        var frames = _removed.Length / document.Channels;
        document.DeleteRange(_startFrame, frames);
        document.ApplyDeleteToMarkers(_startFrame, frames);
        document.ApplyDeleteToSampleLoop(_startFrame, frames);
        document.ApplyDeleteToRegion(_startFrame, frames);
        document.Selection = WaveSelection.Empty;
        document.CursorFrame = _startFrame;
    }

    public void Revert(AudioDocument document)
    {
        document.InsertRange(_startFrame, _removed);
        document.ReplaceMarkers(_markersBefore);
        document.SetSampleLoop(_sampleLoopBefore);
        document.SetRegions(_regionsBefore);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}
