using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal sealed class DocumentSession
{
    public DocumentSession(AudioDocument document)
    {
        Document = document;
        PlayheadFrame = document.CursorFrame;
    }

    public AudioDocument Document { get; }

    public EditHistory History { get; } = new();

    public double TimeZoom { get; set; } = 1;

    public double AmpZoom { get; set; } = 1;

    public double ViewStart { get; set; }

    public long PlayheadFrame { get; set; }

    public List<long> SelectedMarkerFrames { get; } = [];

    public bool LoopEnabled { get; set; } = true;

    public string DisplayName =>
        Document.SourcePath is { } path
            ? System.IO.Path.GetFileName(path)
            : UiStrings.UntitledDocument;

    public string TabTitle => Document.IsDirty ? "* " + DisplayName : DisplayName;
}
