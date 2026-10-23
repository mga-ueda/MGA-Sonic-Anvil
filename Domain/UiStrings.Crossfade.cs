namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string CrossfadeEditTitle => Get("フェード調整ツール", "Fade Adjust Tool");
    public static string CrossfadeWindowHint => Get(
        "本体でプレビューしながら調整。OK で焼き込み、Cancel で破棄して補助表示を消します。",
        "Preview on the main transport. OK to bake, Cancel to discard and clear overlays.");
    public static string CrossfadeSectionParams => Get("ループ", "Loop");
    public static string CrossfadeSectionJump => Get("位置へ移動", "Jump to");
    public static string CrossfadeLabelStart => Get("始点 (S)", "Start (S)");
    public static string CrossfadeLabelEnd => Get("終点 (E)", "End (E)");
    public static string CrossfadeLabelLength => Get("長さ", "Length");
    public static string CrossfadeLabelCrossfade => Get("クロスフェード (X)", "Crossfade (X)");
    public static string CrossfadeLabelPreCrossfade => Get("プリクロスフェード", "Pre-crossfade");
    public static string CrossfadeLabelPostCrossfade => Get("ポストクロスフェード", "Post-crossfade");
    public static string CrossfadeLabelPostFade => Get("ポストクロスフェード", "Post-crossfade");
    public static string CrossfadeLabelFadeIn => Get("プリクロスフェード", "Pre-crossfade");
    public static string CrossfadeMarkerPre => "Pre";
    public static string CrossfadeLabelTruncate => Get("範囲外を切り落とし (C)", "Truncate outside (C)");
    public static string CrossfadeLabelCurve => Get("フェード曲線", "Fade curve");
    public static string CrossfadeLabelCurveIn => Get("イン", "In");
    public static string CrossfadeLabelCurveOut => Get("ポスト", "Post");
    public static string CrossfadeLabelCurveLegOut => Get("アウト脚", "Out leg");
    public static string CrossfadeLabelCurveLegIn => Get("イン脚", "In leg");
    public static string CrossfadePercent => Get("%", "%");
    public static string CrossfadeSamples => Get("サンプル", "Samples");
    public static string CrossfadeSectionTransport => Get("再生", "Transport");
    public static string CrossfadeJumpS => Get("始点へ", "To Start");
    public static string CrossfadeJumpE => Get("終点へ", "To End");
    public static string CrossfadeJumpSX => Get("X開始へ", "To X start");
    public static string CrossfadeJumpEX => Get("E手前Xへ", "To E−X");
    public static string CrossfadeJumpSE => Get("始点+終点", "Start+End");
    public static string CrossfadeJumpFC => Get("先頭へ", "To head");
    public static string CrossfadeAutoSearch => Get("自動探索", "Auto-search");
    public static string CrossfadeAdjustSE => Get("始点と終点をずらす", "Shift Start and End");
    public static string CrossfadeAdjustStart => Get("始点を探す", "Find Start");
    public static string CrossfadeAdjustEnd => Get("終点を探す", "Find End");
    public static string CrossfadeLoopMix => Get("OK", "OK");
    public static string CrossfadeUndoMix => Get("ミックスを戻す (Ctrl+Z)", "Undo Mix (Ctrl+Z)");
    public static string CrossfadeExit => Get("Cancel", "Cancel");
    public static string CrossfadeSnapZero => Get("ゼロクロスへ吸着", "Snap to zero-cross");
    public static string CrossfadePlayOnce => Get("1回再生", "Play once");
    public static string CrossfadePlayLoop => Get("ループ再生", "Play loop");
    public static string CrossfadeStop => Get("停止", "Stop");
    public static string CrossfadeLabelLoopOn => Get("ループ再生オン", "Loop playback on");
    /// <summary>継ぎ目中央の縦線ラベル（左＝End／右＝Start）。Pre/Post と同様、常に英語。</summary>
    public static string CrossfadeMarkerStart => "Start";
    public static string CrossfadeMarkerEnd => "End";
    public static string CrossfadeMarkerX => Get("X", "X");
    public static string CrossfadeMarkerPost => "Post";
    public static string CrossfadeSpliceTitle => Get(
        "継ぎ目プレビュー（左＝終点側 / 右＝始点側）",
        "Seam preview (left=End / right=Start)");
    public static string CrossfadeNudgeLabel => Get("微調整", "Nudge");
    public static string CrossfadeNudgeMinus => Get("−", "−");
    public static string CrossfadeNudgePlus => Get("+", "+");
    public static string CrossfadeNudgeX4 => Get("×4", "×4");
    public static string CrossfadeZoomTimeIn => Get("時間＋", "Time +");
    public static string CrossfadeZoomTimeOut => Get("時間−", "Time −");
    public static string CrossfadeZoomReset => Get("等倍", "1:1");
    public static string CrossfadeZoomAmpIn => Get("振幅＋", "Amp +");
    public static string CrossfadeZoomAmpOut => Get("振幅−", "Amp −");
    public static string TooltipCrossfadeEdit => Get(
        "フェード調整ツール (Shift+X)",
        "Fade Adjust Tool (Shift+X)");

    public static string CrossfadeTipSplice => Get(
        "左ドラッグ＝終点、右ドラッグ＝始点。Shift+縦ドラッグ／Shift・Ctrl+ホイール＝振幅。Shift+横ドラッグ＝左右独立パン。ホイール＝時間拡縮。",
        "Drag left=End, right=Start. Shift+vertical drag / Shift·Ctrl+wheel=amp. Shift+horizontal drag=pan each side. Wheel=time zoom.");
    public static string CrossfadeTipStart => Get(
        "ループの開始サンプル位置。",
        "Loop start sample frame.");
    public static string CrossfadeTipEnd => Get(
        "ループの終了サンプル位置（この位置の手前までがループ）。",
        "Loop end sample frame (exclusive).");
    public static string CrossfadeTipLength => Get(
        "終点 − 始点。自動計算です。",
        "End − Start (read-only).");
    public static string CrossfadeTipCrossfade => Get(
        "エンド手前とスタート先頭を重ねる長さです。",
        "Overlap length between End-side and Start-side audio.");
    public static string CrossfadeTipCrossfadeReadonly => Get(
        "継ぎ目の重ね。本体波形の緑／赤旗で調整します。",
        "Seam overlap. Adjust with green/red flags on the main waveform.");
    public static string CrossfadeTipPostFade => Get(
        "ポストクロスフェード長（赤旗・バーは終点の後ろへ）。波形の重ねと焼き込みはループスタート側だけです。",
        "Post-crossfade (red flag/bar after End). Waveform overlay and bake are only at loop Start.");
    public static string CrossfadeTipFadeIn => Get(
        "プリクロスフェード長（緑旗は始点の手前）。描画は終点手前へスタート波形を重ねます。旗と描画位置は別です。",
        "Pre-crossfade (green flag before Start). Drawing overlays Start onto before-End — flag and draw are separate.");
    public static string CrossfadeTipTruncate => Get(
        "オンにすると、ループミックス時に始点より前と終点以降を削除します（エンド区間は材料として焼いたあと切り落します）。",
        "When on, Loop Mix deletes audio before Start and after End (End-side is used as material, then trimmed).");
    public static string CrossfadeTipPercentToggle => Get(
        "クロスフェード表示を％とサンプル数で切り替えます。",
        "Toggle crossfade display between percent and samples.");
    public static string CrossfadeTipCurve => Get(
        "上段＝イン（プリ）、下段＝ポスト。各行は左＝アウト脚、右＝イン脚です。",
        "Top=In (Pre), Bottom=Post. In each row: left=out leg, right=in leg.");
    public static string CrossfadeTipCurveIn => Get(
        "プリクロスフェード用。左が減衰側（アウト脚）、右が立ち上がり側（イン脚）です。",
        "Pre-crossfade: left=out leg (decay), right=in leg (rise).");
    public static string CrossfadeTipCurveOut => Get(
        "ポストクロスフェード用。左が減衰側（アウト脚）、右が立ち上がり側（イン脚）です。",
        "Post-crossfade: left=out leg (decay), right=in leg (rise).");
    public static string CrossfadeTipJump => Get(
        "メイン波形の表示位置を各マーカーへ動かします。",
        "Move the main waveform view to each marker.");
    public static string CrossfadeTipJumpS => Get("始点へ移動", "Jump to Start");
    public static string CrossfadeTipJumpE => Get("終点へ移動", "Jump to End");
    public static string CrossfadeTipJumpSX => Get("クロスフェード開始（始点−X）へ移動", "Jump to crossfade start (Start−X)");
    public static string CrossfadeTipJumpEX => Get("終点手前のクロスフェード区間へ移動", "Jump to End−X");
    public static string CrossfadeTipJumpSE => Get("始点と終点の範囲を選択して表示", "Select and show Start–End range");
    public static string CrossfadeTipJumpFC => Get("ファイル先頭（切り落とし後の先頭）へ移動", "Jump to file head");
    public static string CrossfadeTipSnapZero => Get(
        "この値を近いゼロクロスへ寄せます。",
        "Snap this value to a nearby zero-crossing.");
    public static string CrossfadeLabelAlignTwentyEight => Get("28の倍数", "Multiples of 28");
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
    public static string CrossfadeTipPlayOnce => Get(
        "始点〜終点を1回だけ再生します。",
        "Play Start–End once.");
    public static string CrossfadeTipPlayLoop => Get(
        "現在の始点〜終点をループ再生します。",
        "Play the current Start–End range as a loop.");
    public static string CrossfadeTipStop => Get("再生を停止します。", "Stop playback.");
    public static string CrossfadeTipLoopOn => Get(
        "ループ再生ボタンの既定動作です。",
        "Default for the loop play button.");
    public static string CrossfadeTipDb => Get(
        "クロスフェード表示を dB 目安に切り替えます。",
        "Show a dB hint for the crossfade.");
    public static string CrossfadeLabelXfade => Get("クロスフェード表示", "Show crossfade");
    public static string CrossfadeTipXfade => Get(
        "継ぎ目プレビューでクロスフェード合成を表示します。",
        "Show baked crossfade in the seam preview.");
    public static string CrossfadeTipLoopMix => Get(
        "フェードを波形に焼き込み、サンプルループを確定してツールを終了します。取り消しは Ctrl+Z。",
        "Bake fades, commit the sample loop, and close the tool. Undo with Ctrl+Z.");
    public static string CrossfadeTipUndoMix => Get(
        "直前の編集を取り消します（履歴の Undo）。",
        "Undo the last edit (history Undo).");
    public static string CrossfadeTipExit => Get(
        "変更を捨てて終了します。波形の補助表示も消えます。",
        "Discard changes and close. Waveform overlays are cleared.");
    public static string CrossfadeHintValueNudge => Get(
        "ホイールまたは上下キーで増減",
        "Wheel or ↑↓ to nudge");
    public static string CrossfadeTipValueNudge => Get(
        "↑↓／ホイール＝1サンプル、Shift＝10、Ctrl+Shift＝100。",
        "↑↓/wheel=1 sample, Shift=10, Ctrl+Shift=100.");
    public static string CrossfadeTipZoomTimeIn => Get("継ぎ目表示を時間方向に拡大", "Zoom seam view in time");
    public static string CrossfadeTipZoomTimeOut => Get("継ぎ目表示を時間方向に縮小", "Zoom seam view out in time");
    public static string CrossfadeTipZoomReset => Get("継ぎ目表示の拡大をリセット", "Reset seam view zoom");
    public static string CrossfadeTipZoomAmpIn => Get("継ぎ目表示の振幅を拡大", "Increase seam amplitude zoom");
    public static string CrossfadeTipZoomAmpOut => Get("継ぎ目表示の振幅を縮小", "Decrease seam amplitude zoom");
}
