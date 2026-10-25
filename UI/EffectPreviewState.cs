namespace MgaSonicAnvil.UI;

/// <summary>フェード／音量／ピッチ／タイムストレッチ／フォーマットの試聴状態。</summary>
internal sealed class EffectPreviewState
{
    public bool Previewing;
    public bool Toggling;
    public long ResumeFrame;
    public long StartedAt;
    public long SpaceTick;
    /// <summary>本体タイムライン上の試聴開始フレーム（ピッチ／TS／フォーマット）。</summary>
    public long Origin;
    /// <summary>フォーマット試聴: プレビュードキュメント上の再生開始フレーム。</summary>
    public long PreviewStartFrame;
    /// <summary>フォーマット試聴: プレビュードキュメントのサンプルレート。0 なら未使用。</summary>
    public int PreviewSampleRate;
    /// <summary>true のとき本体波形／トランスポートの再生表示を動かさない（モーダル試聴用）。</summary>
    public bool HideHostTransport;

    public bool IsTooSoon =>
        Previewing && Environment.TickCount64 - StartedAt < 250;
}
