using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class AddMarkerCommand : IEditCommand
{
    private readonly long _frame;
    private readonly MarkerSnapshot[] _markersBefore;

    public AddMarkerCommand(long frame, MarkerSnapshot[] markersBefore, string summary)
    {
        _frame = frame;
        _markersBefore = markersBefore;
        Summary = summary;
    }

    public string Name => "Add Marker";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.TryAddMarker(_frame);

    public void Revert(AudioDocument document) => document.ReplaceMarkers(_markersBefore);
}
