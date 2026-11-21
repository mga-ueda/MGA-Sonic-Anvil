using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>ヘッダクリーナー向けのチャンク削除判定。</summary>
internal enum WaveChunkCleanVerdict
{
    /// <summary>削除すると構造破壊。色なし。</summary>
    Forbidden,
    /// <summary>マーカー／リージョン／ループ／ch レイアウト。デフォルト保持（シアン）。</summary>
    Protected,
    /// <summary>制作メタ等。説明付きで確認（オレンジ）。</summary>
    Caution,
    /// <summary>確実に消してよい（赤）。</summary>
    Removable,
}

/// <summary>FourCC → 削除判定・説明・ツリー色。</summary>
internal static class WaveChunkCleanPolicy
{
    /// <summary>可（削除してよい）の文字色。</summary>
    public const string RemovableForeBrushKey = "DirtyAccentBrush";

    /// <summary>注意の文字色。</summary>
    public const string CautionForeBrushKey = "LoudnessCautionBrush";

    /// <summary>保護の文字色。</summary>
    public const string ProtectedForeBrushKey = "AccentCyanBrush";

    /// <summary>不可／通常の文字色。</summary>
    public const string ForbiddenForeBrushKey = "PrimaryForeBrush";

    public static WaveChunkCleanVerdict Classify(string? id)
    {
        if (string.IsNullOrEmpty(id) || WaveStructureNode.IsBlankFourCc(id))
        {
            // ****（空／不正 FourCC・切り詰め集約）は FourCC 指定削除が不安定なので不可。
            // ヘッダ修復を使う。
            return WaveChunkCleanVerdict.Forbidden;
        }

        if (IsForbidden(id))
        {
            return WaveChunkCleanVerdict.Forbidden;
        }

        if (IsProtected(id))
        {
            return WaveChunkCleanVerdict.Protected;
        }

        if (IsCaution(id))
        {
            return WaveChunkCleanVerdict.Caution;
        }

        return WaveChunkCleanVerdict.Removable;
    }

    /// <summary>ツリー行の前景ブラシキー。不正表示はエラー色を優先。</summary>
    public static string ResolveForeBrushKey(WaveStructureNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.IsInvalid || WaveStructureNode.IsBlankFourCc(node.Id))
        {
            return "StatusBarErrorDetailForeBrush";
        }

        return Classify(node.Id) switch
        {
            WaveChunkCleanVerdict.Removable => RemovableForeBrushKey,
            WaveChunkCleanVerdict.Caution => CautionForeBrushKey,
            WaveChunkCleanVerdict.Protected => ProtectedForeBrushKey,
            _ => ForbiddenForeBrushKey,
        };
    }

    public static bool CanDelete(string? id) =>
        Classify(id) != WaveChunkCleanVerdict.Forbidden;

    /// <summary>確認ダイアログ用の短い説明（言語切替対応）。</summary>
    public static string Describe(string? id)
    {
        if (string.IsNullOrEmpty(id) || WaveStructureNode.IsBlankFourCc(id))
        {
            return UiStrings.WaveChunkDescBlankFourCc;
        }

        return id switch
        {
            "cue " => UiStrings.WaveChunkDescCue,
            "labl" => UiStrings.WaveChunkDescLabl,
            "note" => UiStrings.WaveChunkDescNote,
            "ltxt" => UiStrings.WaveChunkDescLtxt,
            "r64m" => UiStrings.WaveChunkDescR64m,
            "regn" => UiStrings.WaveChunkDescRegn,
            "smpl" => UiStrings.WaveChunkDescSmpl,
            "inst" => UiStrings.WaveChunkDescInstWav,
            "acid" => UiStrings.WaveChunkDescAcid,
            "strc" => UiStrings.WaveChunkDescStrc,
            "MARK" => UiStrings.WaveChunkDescMark,
            "INST" => UiStrings.WaveChunkDescInstAiff,
            "basc" => UiStrings.WaveChunkDescBasc,
            "trns" => UiStrings.WaveChunkDescTrns,
            "chna" or "Chna" => UiStrings.WaveChunkDescChna,
            "CHAN" => UiStrings.WaveChunkDescChan,
            "plst" => UiStrings.WaveChunkDescPlst,
            "adtl" => UiStrings.WaveChunkDescAdtl,
            "bext" => UiStrings.WaveChunkDescBext,
            "iXML" => UiStrings.WaveChunkDescIxml,
            "TCOD" => UiStrings.WaveChunkDescTcod,
            "fact" => UiStrings.WaveChunkDescFact,
            "LIST" or "list" => UiStrings.WaveChunkDescList,
            "AESD" => UiStrings.WaveChunkDescAesd,
            "MIDI" => UiStrings.WaveChunkDescMidi,
            "SAXL" => UiStrings.WaveChunkDescSaxl,
            "axml" or "AXML" => UiStrings.WaveChunkDescAxml,
            "cart" or "CART" => UiStrings.WaveChunkDescCart,
            "_PMX" => UiStrings.WaveChunkDescPmx,
            "levl" => UiStrings.WaveChunkDescLevl,
            "CSET" => UiStrings.WaveChunkDescCset,
            _ => Classify(id) switch
            {
                WaveChunkCleanVerdict.Protected => UiStrings.WaveChunkDescProtectedGeneric(FormatId(id)),
                WaveChunkCleanVerdict.Caution => UiStrings.WaveChunkDescCautionGeneric(FormatId(id)),
                WaveChunkCleanVerdict.Removable => UiStrings.WaveChunkDescRemovableGeneric(FormatId(id)),
                _ => UiStrings.WaveChunkDescForbiddenGeneric(FormatId(id)),
            },
        };
    }

    public static string FormatId(string id)
    {
        if (WaveStructureNode.IsBlankFourCc(id))
        {
            return WaveStructureNode.BlankFourCcListLabel;
        }

        return id.TrimEnd();
    }

    private static bool IsForbidden(string id) =>
        id is "RIFF" or "RF64" or "BW64" or "RIFX" or "WAVE"
            or "FORM" or "AIFF" or "AIFC"
            or "fmt " or "data" or "wavl" or "ds64"
            or "COMM" or "SSND" or "FVER";

    private static bool IsProtected(string id) =>
        id is "cue " or "labl" or "note" or "ltxt" or "r64m" or "regn"
            or "smpl" or "inst" or "acid" or "strc" or "plst" or "adtl"
            or "MARK" or "INST" or "basc" or "trns"
            or "chna" or "Chna" or "CHAN";

    private static bool IsCaution(string id) =>
        id is "bext" or "iXML" or "TCOD" or "fact"
            or "LIST" or "list"
            or "AESD" or "MIDI" or "SAXL"
            or "axml" or "AXML" or "cart" or "CART" or "_PMX" or "levl" or "CSET"
            or "ubxt" or "mext" or "qlty" or "link" or "dbmd" or "bxml" or "sxml";
}
