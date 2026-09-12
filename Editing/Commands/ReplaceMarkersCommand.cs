using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class ReplaceMarkersCommand : IEditCommand
{
    private readonly MarkerSnapshot[] _before;
    private readonly MarkerSnapshot[] _after;

    public ReplaceMarkersCommand(string name, MarkerSnapshot[] before, MarkerSnapshot[] after, string summary)
    {
        Name = name;
        Summary = summary;
        _before = before;
        _after = after;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.ReplaceMarkers(_after);

    public void Revert(AudioDocument document) => document.ReplaceMarkers(_before);
}
