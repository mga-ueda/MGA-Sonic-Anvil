namespace MgaSonicAnvil.UI;

/// <summary>
/// 本体 <see cref="MgaSonicAnvil.Audio.AudioPlayer"/> 経由のプレビュー。別インスタンスだと ASIO 等でデバイスを奪えない。
/// 旧 <c>PitchCurveEditorDialog</c>（モーダル）が使っていた間接呼び出し用の型。現在は
/// <c>MainWindow.PitchCurve.cs</c> が <c>StartPitchCurvePreviewPlayback</c> / <c>StopPitchCurvePreview</c> を
/// 直接呼ぶため本体では未使用だが、将来の再利用に備えて型だけ残す。
/// </summary>
internal sealed class PitchCurvePreviewPlayback
{
    public required Action<Audio.AudioDocument, Domain.WaveSelection, long> Play { get; init; }
    public required Action Stop { get; init; }
    public required Func<bool> IsPlaying { get; init; }
    public required Func<long> CursorFrame { get; init; }
}
