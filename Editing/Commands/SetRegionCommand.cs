using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class SetRegionCommand : IEditCommand
{
    private readonly WaveRegion[] _before;
    private readonly WaveRegion[] _after;

    public SetRegionCommand(WaveRegion[] before, WaveRegion[] after, string summary)
    {
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Set Region";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.SetRegions(_after);

    public void Revert(AudioDocument document) => document.SetRegions(_before);
}
