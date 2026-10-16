using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class InsertSilenceCommand : IEditCommand
{
    private readonly long _insertFrame;
    private readonly float[] _samples;
    private readonly WaveSelection _selectionBefore;
    private readonly long _cursorBefore;
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly WaveSelection _sampleLoopBefore;
    private readonly WaveRegion[] _regionsBefore;
    private readonly float[]? _beforeOriginal;
    private readonly float[]? _afterOriginal;

    public InsertSilenceCommand(
        long insertFrame,
        float[] samples,
        WaveSelection selectionBefore,
        long cursorBefore,
        MarkerSnapshot[] markersBefore,
        WaveSelection sampleLoopBefore,
        WaveRegion[] regionsBefore,
        string summary,
        float[]? beforeOriginal = null,
        float[]? afterOriginal = null)
    {
        _insertFrame = insertFrame;
        _samples = samples;
        _selectionBefore = selectionBefore;
        _cursorBefore = cursorBefore;
        _markersBefore = markersBefore;
        _sampleLoopBefore = sampleLoopBefore;
        _regionsBefore = regionsBefore;
        _beforeOriginal = beforeOriginal;
        _afterOriginal = afterOriginal;
        Summary = summary;
    }

    public string Name => "Insert Silence";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        if (_beforeOriginal is { Length: > 0 })
        {
            var beforeFrames = _beforeOriginal.Length / document.Channels;
            document.ReplaceRange(_insertFrame - beforeFrames, FadeEdge(_beforeOriginal, document.Channels, fadeIn: false));
        }

        document.InsertRange(_insertFrame, _samples);
        var frames = _samples.Length / document.Channels;
        document.ApplyInsertToMarkers(_insertFrame, frames);
        document.ApplyInsertToSampleLoop(_insertFrame, frames);
        document.ApplyInsertToRegion(_insertFrame, frames);
        if (_afterOriginal is { Length: > 0 })
        {
            document.ReplaceRange(_insertFrame + frames, FadeEdge(_afterOriginal, document.Channels, fadeIn: true));
        }

        document.Selection = new WaveSelection(_insertFrame, _insertFrame + frames);
        document.CursorFrame = _insertFrame;
    }

    public void Revert(AudioDocument document)
    {
        var frames = _samples.Length / document.Channels;
        document.DeleteRange(_insertFrame, frames);
        if (_beforeOriginal is { Length: > 0 })
        {
            var beforeFrames = _beforeOriginal.Length / document.Channels;
            document.ReplaceRange(_insertFrame - beforeFrames, _beforeOriginal);
        }

        if (_afterOriginal is { Length: > 0 })
        {
            document.ReplaceRange(_insertFrame, _afterOriginal);
        }

        document.ReplaceMarkers(_markersBefore);
        document.SetSampleLoop(_sampleLoopBefore);
        document.SetRegions(_regionsBefore);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }

    private static float[] FadeEdge(float[] original, int channels, bool fadeIn)
    {
        var faded = (float[])original.Clone();
        var frames = faded.Length / channels;
        ClickGuard.FadeRangeEdges(
            faded,
            channels,
            bufferStartFrame: 0,
            new WaveSelection(0, frames),
            mask: 0,
            fadeIn,
            fadeOut: !fadeIn,
            frames);
        return faded;
    }
}
