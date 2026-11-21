using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>WAVE ヘッダ作業コピーの差し替え（修復／チャンク削除）。ディスク上の SourcePath は触らない。</summary>
internal sealed class ReplaceWaveContainerCommand : IEditCommand
{
    private readonly string? _beforePath;
    private readonly string? _afterPath;

    public ReplaceWaveContainerCommand(string? beforePath, string? afterPath, string summary)
    {
        _beforePath = beforePath;
        _afterPath = afterPath;
        Summary = summary;
    }

    public string Name => "Replace Wave Container";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) =>
        document.SetWaveContainerPath(_afterPath, markDirty: true);

    public void Revert(AudioDocument document) =>
        document.SetWaveContainerPath(_beforePath, markDirty: true);
}
