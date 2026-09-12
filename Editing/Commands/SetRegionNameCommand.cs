using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class SetRegionNameCommand : IEditCommand
{
    private readonly WaveSelection _range;
    private readonly string _before;
    private readonly string _after;

    public SetRegionNameCommand(WaveSelection range, string before, string after, string summary)
    {
        _range = range;
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Region Name";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.TrySetRegionName(_range, _after);

    public void Revert(AudioDocument document) => document.TrySetRegionName(_range, _before);
}
