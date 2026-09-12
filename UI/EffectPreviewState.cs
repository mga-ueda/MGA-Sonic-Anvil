namespace MgaSonicAnvil.UI;

/// <summary>フェード／音量／ピッチ／タイムストレッチ／フォーマットの試聴状態。</summary>
internal sealed class EffectPreviewState
{
    public bool Previewing;
    public bool Toggling;
    public long ResumeFrame;
    public long StartedAt;
    public long SpaceTick;
    public long Origin;

    public bool IsTooSoon =>
        Previewing && Environment.TickCount64 - StartedAt < 250;
}
