namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string CrossfadeEditTitle => Get("フェード調整ツール", "Fade Adjust Tool");
    public static string CrossfadeWindowHint => Get(
        "本体でプレビューしながら調整。OK で焼き込み、Cancel で破棄して補助表示を消します。",
        "Preview on the main transport. OK to bake, Cancel to discard and clear overlays.");
    public static string CrossfadeSectionParams => Get("ループ", "Loop");
    public static string CrossfadeLabelStart => Get("始点 (S)", "Start (S)");
    public static string CrossfadeLabelEnd => Get("終点 (E)", "End (E)");
    public static string CrossfadeLabelPostFade => Get("ポストクロスフェード (T)", "Post-crossfade (T)");
    public static string CrossfadeLabelFadeIn => Get("プリクロスフェード (P)", "Pre-crossfade (P)");
    public static string CrossfadeMarkerPre => "Pre";
    /// <summary>アクセスキー無し（ショートカットは付けない）。</summary>
    public static string CrossfadeLabelTruncate => Get("ループ前後を削除(_T)", "Delete before/after loop (_T)");
    /// <summary>アクセスキー無し（ショートカットは付けない）。</summary>
    public static string CrossfadeLabelDeleteAfterLoop => Get("ループ後を削除(_A)", "Delete _after loop");
    public static string CrossfadeLabelCurve => Get("フェード曲線", "Fade curve");
    public static string CrossfadeLabelCurveIn => Get("プリクロスフェード", "Pre-crossfade");
    public static string CrossfadeLabelCurveOut => Get("ポストクロスフェード", "Post-crossfade");
    public static string CrossfadeLabelCurveLegOut => Get("元波形", "Original");
    public static string CrossfadeLabelCurveLegIn => Get("クロス波形", "Cross");
    public static string CrossfadeAutoSearch => Get("自動探索", "Auto-search");
    public static string CrossfadeAdjustSE => Get("始点と終点をずらす", "Shift Start and End");
    public static string CrossfadeAdjustStart => Get("始点を探す", "Find Start");
    public static string CrossfadeAdjustEnd => Get("終点を探す", "Find End");
    /// <summary>継ぎ目中央の縦線ラベル（左＝End／右＝Start）。Pre/Post と同様、常に英語。</summary>
    public static string CrossfadeMarkerStart => "Start";
    public static string CrossfadeMarkerEnd => "End";
    public static string CrossfadeMarkerPost => "Post";

    public static string CrossfadeTipStart => Get(
        "ループの開始位置。",
        "Loop start position.");
    public static string CrossfadeTipEnd => Get(
        "ループの終了位置（この位置の手前までがループ）。",
        "Loop end position (exclusive).");
    public static string CrossfadeTipPostFade => Get(
        "ポストクロスフェード長（赤旗・バーはループスタート側）。描画は終点後ろへスタート波形を重ねます。旗と描画位置は別です。",
        "Post-crossfade (red flag/bar at loop Start). Drawing overlays Start onto after-End — flag and draw are separate.");
    public static string CrossfadeTipFadeIn => Get(
        "プリクロスフェード長（緑旗は始点の手前）。描画は終点手前へスタート手前波形を重ねます。旗と描画位置は別です。",
        "Pre-crossfade (green flag before Start). Drawing overlays before-Start onto before-End — flag and draw are separate.");
    public static string CrossfadeTipTruncate => Get(
        "オンにすると、ループミックス時に始点より前と終点以降を削除します。",
        "When on, Loop Mix deletes audio before Start and after End.");
    public static string CrossfadeTipDeleteAfterLoop => Get(
        "オンにすると、ループミックス時に終点より後ろの音声だけを削除します（始点より前は残します）。",
        "When on, Loop Mix deletes only audio after End (keeps audio before Start).");
    public static string CrossfadeTipCurve => Get(
        "上段＝プリ、下段＝ポスト。各行は左＝元波形、右＝クロス波形です。",
        "Top=Pre, Bottom=Post. In each row: left=original, right=cross.");
    public static string CrossfadeTipCurveIn => Get(
        "プリクロスフェード用。左＝元波形（減衰）、右＝クロス波形（立ち上がり）です。",
        "Pre-crossfade: left=original (decay), right=cross (rise).");
    public static string CrossfadeTipCurveOut => Get(
        "ポストクロスフェード用。左＝クロス波形（ループスタートのフェードアウト）、右＝元波形（終点後ろのフェードイン）です。",
        "Post-crossfade: left=cross (loop Start fade-out), right=original (after-End fade-in).");
    public static string CrossfadeTipSnapZero => Get(
        "この値を近いゼロクロスへ寄せます。",
        "Snap this value to a nearby zero-crossing.");
    public static string CrossfadeLabelAlignTwentyEight => Get("28の倍数でループを取る(_M)", "Take loop on _multiples of 28");
    public static string CrossfadeTipAlignTwentyEight => Get(
        "オンのとき、ループの始点と終点は 28 サンプルの倍数にしか動きません。",
        "When on, loop Start and End move only on multiples of 28 samples.");
    public static string CrossfadeTipAutoSearch => Get(
        "継ぎ目の波形が似る位置を自動で探します。",
        "Search nearby for a better matching seam.");
    public static string CrossfadeTipAdjustSE => Get(
        "長さを保ったまま始点と終点をずらして最適位置を探します。窓の端まで進み続けます。",
        "Keep length; slide Start and End together. Continues to the usable edge.");
    public static string CrossfadeTipAdjustStart => Get(
        "終点固定で始点だけ動かして最適位置を探します。窓の端まで進み続けます。",
        "Keep End fixed; move Start to find a better seam. Continues to the usable edge.");
    public static string CrossfadeTipAdjustEnd => Get(
        "始点固定で終点だけ動かして最適位置を探します。窓の端まで進み続けます。",
        "Keep Start fixed; move End to find a better seam. Continues to the usable edge.");
    public static string CrossfadeLabelXfade => Get("クロスフェード表示(_X)", "Show crossfade (_X)");
    public static string CrossfadeTipXfade => Get(
        "継ぎ目プレビューでクロスフェード合成を表示します。",
        "Show baked crossfade in the seam preview.");
    public static string CrossfadeLabelSingleLoop => Get(
        "サンプルループでループする",
        "Loop with sample loop");
    public static string CrossfadeTipSingleLoop => Get(
        "オンでサンプルループ終端から始点へ無限ループ。オフにすると終点を越えて再生し、ポストクロスフェードを確認できます。ステータスバーの Single Loop と連動します。",
        "On: loop forever from End back to Start. Off: play past End so you can hear the post-crossfade. Synced with the status-bar Single Loop.");
    public static string CrossfadeTipLoopMix => Get(
        "フェードを波形に焼き込み、サンプルループを確定してツールを終了します。取り消しは Ctrl+Z。",
        "Bake fades, commit the sample loop, and close the tool. Undo with Ctrl+Z.");
    public static string ConfirmCrossfadeClip(string overDb) => Format(
        "クロスフェードの合成結果が 0 dBFS を {0} dB 超えています。このままでは歪む可能性があります。\n\nOK: 波形全体を下げて 0 dBFS 以内に収める\nCancel: 下げずにこのまま適用する",
        "The crossfade mix is {0} dB over 0 dBFS and may distort.\n\nOK: lower the entire waveform to stay within 0 dBFS\nCancel: apply as-is without lowering",
        overDb);
    public static string CrossfadeTipExit => Get(
        "変更を捨てて終了します。波形の補助表示も消えます。",
        "Discard changes and close. Waveform overlays are cleared.");
    public static string CrossfadeHintValueNudge => Get(
        "ホイールまたは上下キーで増減",
        "Wheel or ↑↓ to nudge");
    public static string CrossfadeTipValueNudge => Get(
        "時間表示: ↑↓／ホイール＝1秒、Shift＝10秒、Ctrl＝1分、Ctrl+Shift＝10分。サンプル表示: 1／Shift 10／Ctrl 100／Ctrl+Shift 1000。見出しの時間／サンプル数、または数値欄の右クリックで切替（このウィンドウ内のみ。ステータスバーとは別）。",
        "Time: ↑↓/wheel=1 s, Shift=10 s, Ctrl=1 min, Ctrl+Shift=10 min. Samples: 1 / Shift 10 / Ctrl 100 / Ctrl+Shift 1000. Switch via the Time/Samples header or right-click on a value box (this window only; independent of the status bar).");
    public static string CrossfadeUnitTime => Get("時間", "Time");
    public static string CrossfadeUnitSamples => Get("サンプル数", "Samples");
    public static string CrossfadeTipUnitToggle => Get(
        "表示を時間とサンプル数で切り替えます（このウィンドウ内のみ。ステータスバーとは別）。数値欄の右クリックでも同じです。",
        "Switch display between time and samples (this window only; independent of the status bar). Same via right-click on a value box.");
}
