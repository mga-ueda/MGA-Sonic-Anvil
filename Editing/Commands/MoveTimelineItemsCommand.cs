using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class MoveTimelineItemsCommand : IEditCommand
{
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly MarkerSnapshot[] _markersAfter;
    private readonly WaveRegion[] _regionsBefore;
    private readonly WaveRegion[] _regionsAfter;
    private readonly WaveSelection _loopBefore;
    private readonly WaveSelection _loopAfter;

    public MoveTimelineItemsCommand(
        MarkerSnapshot[] markersBefore,
        MarkerSnapshot[] markersAfter,
        WaveRegion[] regionsBefore,
        WaveRegion[] regionsAfter,
        WaveSelection loopBefore,
        WaveSelection loopAfter,
        string summary)
    {
        _markersBefore = markersBefore;
        _markersAfter = markersAfter;
        _regionsBefore = regionsBefore;
        _regionsAfter = regionsAfter;
        _loopBefore = loopBefore;
        _loopAfter = loopAfter;
        Summary = summary;
    }

    public string Name => "Move Timeline";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        document.ReplaceMarkers(_markersAfter);
        document.SetRegions(_regionsAfter);
        document.SetSampleLoop(_loopAfter);
    }

    public void Revert(AudioDocument document)
    {
        document.ReplaceMarkers(_markersBefore);
        document.SetRegions(_regionsBefore);
        document.SetSampleLoop(_loopBefore);
    }
}
