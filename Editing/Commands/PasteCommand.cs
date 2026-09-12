using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class PasteCommand : IEditCommand
{
    private readonly long _insertFrame;
    private readonly float[] _inserted;
    private readonly float[]? _replaced;
    private readonly WaveSelection _selectionBefore;
    private readonly long _cursorBefore;
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly MarkerSnapshot[] _pastedMarkers;
    private readonly WaveRegion[] _pastedRegions;
    private readonly WaveSelection _sampleLoopBefore;
    private readonly WaveRegion[] _regionsBefore;

    public PasteCommand(
        long insertFrame,
        float[] inserted,
        float[]? replaced,
        WaveSelection selectionBefore,
        long cursorBefore,
        MarkerSnapshot[] markersBefore,
        MarkerSnapshot[] pastedMarkers,
        WaveRegion[] pastedRegions,
        WaveSelection sampleLoopBefore,
        WaveRegion[] regionsBefore,
        string summary)
    {
        _insertFrame = insertFrame;
        _inserted = inserted;
        _replaced = replaced;
        _selectionBefore = selectionBefore;
        _cursorBefore = cursorBefore;
        _markersBefore = markersBefore;
        _pastedMarkers = pastedMarkers;
        _pastedRegions = pastedRegions;
        _sampleLoopBefore = sampleLoopBefore;
        _regionsBefore = regionsBefore;
        Summary = summary;
    }

    public string Name => "Paste";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        if (_replaced is { Length: > 0 })
        {
            var removedFrames = _replaced.Length / document.Channels;
            document.DeleteRange(_insertFrame, removedFrames);
            document.ApplyDeleteToMarkers(_insertFrame, removedFrames);
            document.ApplyDeleteToSampleLoop(_insertFrame, removedFrames);
            document.ApplyDeleteToRegion(_insertFrame, removedFrames);
        }

        document.InsertRange(_insertFrame, _inserted);
        var insertedFrames = _inserted.Length / document.Channels;
        document.ApplyInsertToMarkers(_insertFrame, insertedFrames);
        document.ApplyInsertToSampleLoop(_insertFrame, insertedFrames);
        document.ApplyInsertToRegion(_insertFrame, insertedFrames);
        document.ApplyPastedMarkers(_insertFrame, _pastedMarkers);
        document.ApplyPastedRegions(_insertFrame, _pastedRegions);
        document.Selection = WaveSelection.Empty;
        document.CursorFrame = _insertFrame + insertedFrames;
    }

    public void Revert(AudioDocument document)
    {
        var insertedFrames = _inserted.Length / document.Channels;
        document.DeleteRange(_insertFrame, insertedFrames);
        if (_replaced is { Length: > 0 })
        {
            document.InsertRange(_insertFrame, _replaced);
        }

        document.ReplaceMarkers(_markersBefore);
        document.SetSampleLoop(_sampleLoopBefore);
        document.SetRegions(_regionsBefore);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}
