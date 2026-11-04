namespace MgaSonicAnvil.Audio;

/// <summary>
/// 自プロセスの Windows オーディオセッション（ISimpleAudioVolume）が、過去の
/// 終了洗い流しで Mute=true / Volume=0 のまま残されたときに限って元へ戻すための判定。
/// Windows はアプリ単位の音量・ミュートを永続化するため、一度 0 にすると
/// 次回起動でもミキサがミュート・音量 0 のままになり、内部メーターは動くのに
/// 音が出ない状態になる。ユーザーが意図的に下げた音量や、音量を残したままの
/// ミュートまでは触らない。
/// </summary>
internal static class AudioSessionRestore
{
    /// <summary>これ以下の音量は実質無音（壊れた状態）とみなす。</summary>
    public const float SilenceEpsilon = 1e-4f;

    /// <summary>
    /// 実質無音（Volume≈0）のときだけ true。旧洗い流しは Mute と Volume=0 を
    /// 同時に書くためこれで拾える。ユーザーが音量を残したままミュートしただけの
    /// 状態や、意図的に下げた音量はそのまま残す。
    /// </summary>
    public static bool ShouldRestore(bool muted, float volume)
    {
        // muted は呼び出し側の文脈用。判定は Volume≈0（バグ残骸）だけ見る。
        _ = muted;
        return volume <= SilenceEpsilon;
    }
}
