using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class SetSampleLoopCommand : IEditCommand
{
    private readonly WaveSelection _before;
    private readonly WaveSelection _after;

    public SetSampleLoopCommand(WaveSelection before, WaveSelection after, string summary)
    {
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Set Sample Loop";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.SetSampleLoop(_after);

    public void Revert(AudioDocument document) => document.SetSampleLoop(_before);
}
