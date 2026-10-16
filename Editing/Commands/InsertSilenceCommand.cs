using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class InsertSilenceCommand : IEditCommand
{
    private readonly long _insertFrame;
    private readonly float[] _samples;
    private readonly WaveSelection _selectionBefore;
    private readonly long _cursorBefore;
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly WaveSelection _sampleLoopBefore;
    private readonly WaveRegion[] _regionsBefore;

    public InsertSilenceCommand(
        long insertFrame,
        float[] samples,
        WaveSelection selectionBefore,
        long cursorBefore,
        MarkerSnapshot[] markersBefore,
        WaveSelection sampleLoopBefore,
        WaveRegion[] regionsBefore,
        string summary)
    {
        _insertFrame = insertFrame;
        _samples = samples;
        _selectionBefore = selectionBefore;
        _cursorBefore = cursorBefore;
        _markersBefore = markersBefore;
        _sampleLoopBefore = sampleLoopBefore;
        _regionsBefore = regionsBefore;
        Summary = summary;
    }

    public string Name => "Insert Silence";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        document.InsertRange(_insertFrame, _samples);
        var frames = _samples.Length / document.Channels;
        document.ApplyInsertToMarkers(_insertFrame, frames);
        document.ApplyInsertToSampleLoop(_insertFrame, frames);
        document.ApplyInsertToRegion(_insertFrame, frames);
        document.Selection = new WaveSelection(_insertFrame, _insertFrame + frames);
        document.CursorFrame = _insertFrame;
    }

    public void Revert(AudioDocument document)
    {
        var frames = _samples.Length / document.Channels;
        document.DeleteRange(_insertFrame, frames);
        document.ReplaceMarkers(_markersBefore);
        document.SetSampleLoop(_sampleLoopBefore);
        document.SetRegions(_regionsBefore);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}
