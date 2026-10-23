using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>複数の編集を 1 履歴エントリとしてまとめる。</summary>
internal sealed class CompositeEditCommand : IEditCommand
{
    private readonly IEditCommand[] _steps;

    public CompositeEditCommand(string name, string summary, params IEditCommand[] steps)
    {
        Name = name;
        Summary = summary;
        _steps = steps;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        foreach (var step in _steps)
        {
            step.Apply(document);
        }
    }

    public void Revert(AudioDocument document)
    {
        for (var i = _steps.Length - 1; i >= 0; i--)
        {
            _steps[i].Revert(document);
        }
    }
}
