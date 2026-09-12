using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;


internal interface IEditCommand
{
    string Name { get; }

    string Summary { get; }

    /// <summary>
    /// 同じ操作を別ドキュメントへ再実行するファクトリ（履歴のコピー＆ペースト用）。
    /// パラメータ（範囲・カーブ等）から作り直すので、ノーマライズ等は適用先で再計算される。
    /// null は再適用不可（ドラッグ由来のタイムライン一括移動など）。
    /// 適用できない場合（範囲が空になる等）はファクトリが null を返す。
    /// </summary>
    Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    /// <summary>起動復元用。無い操作はセッションに残さない。</summary>
    HistoryRecipe? Persist { get; set; }

    void Apply(AudioDocument document);

    void Revert(AudioDocument document);
}
