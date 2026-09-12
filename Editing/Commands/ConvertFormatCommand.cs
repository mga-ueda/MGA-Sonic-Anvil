using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class ConvertFormatCommand : IEditCommand
{
    private readonly FormatSnapshot _before;
    private readonly FormatSnapshot _after;

    public ConvertFormatCommand(string name, string summary, FormatSnapshot before, FormatSnapshot after)
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

    public void Apply(AudioDocument document) => _after.Restore(document);

    public void Revert(AudioDocument document) => _before.Restore(document);
}
