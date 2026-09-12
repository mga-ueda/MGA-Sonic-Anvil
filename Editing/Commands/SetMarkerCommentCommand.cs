using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class SetMarkerCommentCommand : IEditCommand
{
    private readonly long _frame;
    private readonly string _before;
    private readonly string _after;

    public SetMarkerCommentCommand(long frame, string before, string after, string summary)
    {
        _frame = frame;
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Marker Comment";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.TrySetMarkerComment(_frame, _after);

    public void Revert(AudioDocument document) => document.TrySetMarkerComment(_frame, _before);
}
