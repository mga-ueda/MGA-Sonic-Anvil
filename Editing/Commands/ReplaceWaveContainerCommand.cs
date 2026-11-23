using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>WAVE ヘッダ作業コピーの差し替え（修復／チャンク削除／チャンネルマスク）。ディスク上の SourcePath は触らない。</summary>
internal sealed class ReplaceWaveContainerCommand : IEditCommand
{
    private readonly string? _beforePath;
    private readonly string? _afterPath;
    private readonly int _beforeMask;
    private readonly int _afterMask;

    public ReplaceWaveContainerCommand(
        string? beforePath,
        string? afterPath,
        string summary,
        int beforeMask,
        int afterMask)
    {
        _beforePath = beforePath;
        _afterPath = afterPath;
        _beforeMask = beforeMask;
        _afterMask = afterMask;
        Summary = summary;
    }

    public string Name => "Replace Wave Container";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        document.SetWaveContainerPath(_afterPath, markDirty: true);
        document.SetChannelMask(_afterMask);
    }

    public void Revert(AudioDocument document)
    {
        document.SetWaveContainerPath(_beforePath, markDirty: true);
        document.SetChannelMask(_beforeMask);
    }
}
