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
    /// <summary>true のとき本体波形／トランスポートの再生表示を動かさない（モーダル試聴用）。</summary>
    public bool HideHostTransport;

    public bool IsTooSoon =>
        Previewing && Environment.TickCount64 - StartedAt < 250;
}
