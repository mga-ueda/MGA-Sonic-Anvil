using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class ReplaceRangeCommand : IEditCommand
{
    private readonly long _startFrame;
    private readonly float[] _before;
    private readonly float[] _after;
    private readonly WaveSelection _selectionBefore;
    private readonly WaveSelection _selectionAfter;
    private readonly long _cursorBefore;
    private readonly long _cursorAfter;

    public ReplaceRangeCommand(
        string name,
        long startFrame,
        float[] before,
        float[] after,
        WaveSelection selectionBefore,
        WaveSelection selectionAfter,
        long cursorBefore,
        long cursorAfter,
        string? summary = null)
    {
        Name = name;
        Summary = string.IsNullOrWhiteSpace(summary) ? UiStrings.EditHistoryName(name) : summary;
        _startFrame = startFrame;
        _before = before;
        _after = after;
        _selectionBefore = selectionBefore;
        _selectionAfter = selectionAfter;
        _cursorBefore = cursorBefore;
        _cursorAfter = cursorAfter;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        document.ReplaceRange(_startFrame, _after);
        document.Selection = _selectionAfter;
        document.CursorFrame = _cursorAfter;
    }

    public void Revert(AudioDocument document)
    {
        document.ReplaceRange(_startFrame, _before);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}
