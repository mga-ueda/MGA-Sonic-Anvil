using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class SetSampleLoopCommand : IEditCommand
{
    private readonly WaveSelection _before;
    private readonly WaveSelection _after;
    private readonly MarkerSnapshot[]? _markersBefore;
    private readonly MarkerSnapshot[]? _markersAfter;

    public SetSampleLoopCommand(
        WaveSelection before,
        WaveSelection after,
        string summary,
        MarkerSnapshot[]? markersBefore = null,
        MarkerSnapshot[]? markersAfter = null)
    {
        _before = before;
        _after = after;
        Summary = summary;
        _markersBefore = markersBefore;
        _markersAfter = markersAfter;
    }

    public string Name => "Set Sample Loop";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        document.SetSampleLoop(_after);
        if (_markersAfter is not null)
        {
            document.ReplaceMarkers(_markersAfter);
        }
    }

    public void Revert(AudioDocument document)
    {
        document.SetSampleLoop(_before);
        if (_markersBefore is not null)
        {
            document.ReplaceMarkers(_markersBefore);
        }
    }
}
