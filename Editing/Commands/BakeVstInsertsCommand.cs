using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>
/// 波形置換に加え、バイパス副作用を Apply／Revert に載せる（アンドゥで自動バイパスだけ戻す）。
/// Include Tail で after が長いときは文書末尾を伸ばす。
/// </summary>
internal sealed class BakeVstInsertsCommand : IEditCommand
{
    private readonly long _startFrame;
    private readonly float[] _before;
    private readonly float[] _after;
    private readonly WaveSelection _selectionBefore;
    private readonly WaveSelection _selectionAfter;
    private readonly long _cursorBefore;
    private readonly long _cursorAfter;
    private readonly Action _applyBypass;
    private readonly Action _revertBypass;
    private readonly bool _samplesChanged;

    public BakeVstInsertsCommand(
        float[] before,
        float[] after,
        WaveSelection selectionBefore,
        WaveSelection selectionAfter,
        long cursorBefore,
        long cursorAfter,
        string summary,
        Action applyBypass,
        Action revertBypass)
    {
        _startFrame = 0;
        _before = before;
        _after = after;
        _selectionBefore = selectionBefore;
        _selectionAfter = selectionAfter;
        _cursorBefore = cursorBefore;
        _cursorAfter = cursorAfter;
        _applyBypass = applyBypass;
        _revertBypass = revertBypass;
        _samplesChanged = before.Length != after.Length || !before.AsSpan().SequenceEqual(after);
        Name = "Bounce Inserts";
        Summary = summary;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        if (_samplesChanged)
        {
            WriteSamples(document, _before, _after);
            document.Selection = _selectionAfter;
            document.CursorFrame = _cursorAfter;
        }

        _applyBypass();
    }

    public void Revert(AudioDocument document)
    {
        if (_samplesChanged)
        {
            WriteSamples(document, _after, _before);
            document.Selection = _selectionBefore;
            document.CursorFrame = _cursorBefore;
        }

        _revertBypass();
    }

    private void WriteSamples(AudioDocument document, float[] current, float[] next)
    {
        var channels = Math.Max(1, document.Channels);
        var currentFrames = current.Length / channels;
        if (current.Length == next.Length)
        {
            document.ReplaceRange(_startFrame, next);
            return;
        }

        document.SpliceRange(_startFrame, currentFrames, next);
    }
}
