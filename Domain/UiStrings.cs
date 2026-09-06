using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public const string AppName = AppVersion.ProductName;

    public const string CopyrightText = "© 2026 " + AppVersion.CompanyName + "  ";

    public const string CopyrightGitHub = "GitHub";

    public const string DropHint = "Wave / AIFF / MP3 をドロップ、または Ctrl+O";

    public const string UntitledDocument = "untitled";

    public const string LabelAlwaysOnTop = "Always on Top";
    public const string LabelAudioApi = "Audio API";
    public const string LabelAudioDevice = "Device";
    public const string LabelAudioApiWaveOut = "WaveOut";
    public const string LabelAudioApiWasapi = "WASAPI";
    public const string LabelAudioApiAsio = "ASIO";
    public const string ButtonOk = "OK";
    public const string ButtonCancel = "Cancel";
    public const string DialogSettingsTitle = "Audio Settings";

    public const string ButtonFadeIn = "FADE IN";
    public const string ButtonFadeOut = "FADE OUT";
    public const string ButtonNormalize = "NORMALIZE";
    public const string ButtonDelete = "DELETE";
    public const string ButtonSave = "SAVE";
    public const string ButtonOpen = "OPEN";
    public const string LabelMono = "Mono";
    public const string LabelStereo = "Stereo";
    public const string LabelConvertCustomRate = "任意";

    public const string MenuOpen = "開く";
    public const string MenuSave = "上書き保存";
    public const string MenuSaveAs = "名前を付けて保存";
    public const string MenuSettings = "音声設定";

    public const string DialogExitTitle = "終了確認";
    public const string DialogExitBody = "アプリケーションを終了しますか？";

    public const string ConfirmSave =
        "未保存の変更があります。保存しますか？";

    public static string ConfirmSaveFor(string name) =>
        $"{name} に未保存の変更があります。保存しますか？";

    public const string ConfirmOverwrite =
        "既存ファイルを上書きしますか？";

    public const string ErrorOpenFailed = "読み込みに失敗しました。";
    public const string ErrorSaveFailed = "書き出しに失敗しました。";
    public const string ErrorAiffExport = "AIFF の書き出しには対応していません。Wave または MP3 を選んでください。";
    public const string ErrorNoDocument = "ファイルが開かれていません。";
    public const string ErrorNoSelection = "選択範囲がありません。";
    public const string ErrorClipboardEmpty = "クリップボードが空です。";
    public const string ErrorEmptyAfterDelete = "ファイル全体は削除できません。";

    public const string TipPlay = "再生 / 停止 (Space)\n停止で開始位置へ戻る\nEnter でその場停止\nCtrl+ドラッグでスクラブ\nCtrl+Space 3秒前から\nAlt+Enter 再生開始位置からやり直し";
    public const string TipStop = "停止（開始位置へ戻る）";
    public const string TipGoToStart = "先頭 (Ctrl+Home)";
    public const string TipGoToEnd = "末尾 (Ctrl+End)";
    public const string TipTimeZoomIn = "時間拡大 (↑)\nホイールでも拡大";
    public const string TipTimeZoomOut = "時間縮小 (↓)";
    public const string TipTimeZoomMax = "時間 32倍 / 最大 (Ctrl+↑)";
    public const string TipTimeZoomReset = "全体表示 (Ctrl+↓)";
    public const string TipAmpZoomIn = "振幅拡大 (Shift+↑)\nCtrl+ホイールでも拡大";
    public const string TipAmpZoomOut = "振幅縮小 (Shift+↓)";
    public const string TipAmpZoomMax = "振幅最大 (Ctrl+Shift+↑)";
    public const string TipAmpZoomReset = "振幅リセット (Ctrl+Shift+↓)";
    public const string TipFadeIn = "フェードイン (I)\nカーブを選び Space で試聴、Enter で実行。1–9 でカーブを選択。未選択なら全体";
    public const string TipFadeOut = "フェードアウト (O)\nカーブを選び Space で試聴、Enter で実行。1–9 でカーブを選択。未選択なら全体";

    public static string LabelFadeCurve(int shapeId) => shapeId switch
    {
        0 => "Logarithmic (Base 3)",
        1 => "Sine (Constant Power Fade In)",
        2 => "Logarithmic (Base 1.41)",
        3 => "Inverted S-Curve",
        4 => "Linear",
        5 => "Constant",
        6 => "S-Curve",
        7 => "Exponential (Base 1.41)",
        8 => "Sine (Constant Power Fade Out)",
        9 => "Exponential (Base 3)",
        _ => "S-Curve",
    };

    public static string TipFadeShape(int shapeId) => shapeId switch
    {
        0 => "対数（Base 3）。立ち上がりが早く、終わりがなだらかです。",
        1 => "定電力フェードイン（Sine）。",
        2 => "対数（Base 1.41）。",
        3 => "逆 S 字。",
        4 => "直線。",
        5 => "一定（終端まで値を保ち、最後で切り替わります）。",
        6 => "S 字。",
        7 => "指数（Base 1.41）。立ち上がりが遅く、終わりが急です。",
        8 => "定電力フェードアウト（Sine）。",
        9 => "指数（Base 3）。立ち上がりが遅く、終わりが急です。",
        _ => "S 字。",
    };
    public const string TipNormalize = "ノーマライズ (N)\nピークを -0.1 dB に合わせる";
    public const string TipDelete = "部分削除 (Delete)\n選択範囲を詰めて削除\n選択中のマーカー / リージョンはまとめて削除\nCtrl+Del でマーカー削除";
    public const string TipSave = "保存 (Ctrl+S)\nCtrl+Shift+S で別名保存";
    public const string TipOpen = "開く (Ctrl+O)\n複数ファイル可。追加で開く。\nWave / AIFF / MP3\nCtrl+W でタブを閉じる（未保存なら保存確認）\nCtrl+Tab で次のタブ";
    public const string TipCloseTab = "タブを閉じる (Ctrl+W)";
    public const string TipOverview = "波形全体。明るい部分が表示中の範囲。ドラッグで移動　ホイールで拡縮";
    public const string TipSpectrum = "再生出力の簡易スペクトラム表示です。";
    public const string TipAudioApi = "再生 API（WaveOut / WASAPI / ASIO）";
    public const string TipAudioDevice = "再生デバイス";
    public const string TipWaveform =
        "ドラッグで選択　Ctrl+ドラッグでスクラブ　Esc または Shiftなし移動で解除　Shift＋移動は選択　Shift+←→ で伸長（点表示時は1サンプル）　Home/End で画面端　Shift+PgUp/PgDn で5%　Ctrl+Shift+Home/End で前後すべて　Ctrl+A で全選択　ダブルクリックで区間（マーカー間）　ガイドはマーカー / ループ端に吸着\n"
        + "ホイール=時間ズーム　Shift+ホイール=パン　Ctrl+ホイール=振幅\n"
        + "←→ シーク（選択中のマーカー / リージョン端 / ループ端は移動。点表示時は1サンプル）　Ctrl+←→ 前後のマーカー / サンプルループ端　テンキーで番号（無ければ表示位置）　Z / . 中央寄せ（再生中はセンターロックの切替、停止で解除）　0-9 表示位置　L で選択（無ければサンプルループ / -L）の末尾3秒前からループ再生　Shift+L で選択をサンプルループに設定（同じ範囲でもう一度で解除）　Shift+R で選択をリージョンに設定（同じ範囲でもう一度で解除）　マーカー / リージョンフラッグ / ループバーを右クリックで削除　S サンプリングレート　B ビット深度　C チャンネル数　M マーカー　フラッグをクリックで端を選択 / Shift+クリックで範囲 / Ctrl+クリックで追加 / ドラッグまたは ←→ で移動 / Delete または Ctrl+Del で削除　Ctrl+Shift+R でリネーム　ダブルクリックでコメント（-A ライム / -L ブルー / -E 赤 / -R グレー）\n"
        + "マーカー上で Alt+←→ は1px（点表示時は1サンプル）、Shift で3倍、Ctrl で手前のマーカーとセット　X で表示範囲をシーク前後にリニアフェード（前=アウト / 後=イン）　Ctrl+X / C / V でカット・コピー・ペースト（範囲内マーカー含む）　T で現在時間　U で編集履歴";
    public const string TipAlwaysOnTop = "ウィンドウを常に最前面へ表示します。";
    public const string TipGitHub = "GitHub リポジトリを開きます。";
    public const string TipTimecode = "再生位置 (T)。クリックまたは T で入力、Enter で移動。コピー／貼り付け可";
    public const string MenuCopy = "コピー";
    public const string MenuPaste = "貼り付け";
    public const string MenuClearSampleLoop = "ループを削除";
    public const string MenuClearRegion = "リージョンを削除";
    public const string MenuClearMarker = "マーカーを削除";
    public const string MenuClearMarkers = "選択したマーカーを削除";
    public const string StatusEmpty = "ファイルなし";

    public const string EditHistoryTitle = "編集履歴";
    public const string EditHistoryOrigin = "初期状態";
    public const string EditHistoryHint = "↑↓ 移動　Enter 確定　Esc キャンセル";

    public static string EditHistoryName(string name) => name switch
    {
        EditHistoryOrigin => EditHistoryOrigin,
        "Fade In" => "フェードイン",
        "Fade Out" => "フェードアウト",
        "Fade Around Playhead" => "再生ヘッド前後フェード",
        "Normalize" => "ノーマライズ",
        "Delete" => "削除",
        "Paste" => "ペースト",
        "Add Marker" => "マーカー追加",
        "Marker Comment" => "マーカーコメント",
        "Delete Markers" => "マーカー削除",
        "Move Marker" => "マーカー移動",
        "Move Markers" => "マーカー移動",
        "Set Sample Loop" => "サンプルループ",
        "Set Region" => "リージョン",
        "Move Timeline" => "移動",
        "Convert Sample Rate" => "サンプリングレート",
        "Convert Bit Depth" => "ビット深度",
        "Convert Channels" => "チャンネル数",
        _ => name,
    };

    public static string LabelFadeCurveShort(int shapeId) => shapeId switch
    {
        0 => "対数3",
        1 => "Sine",
        2 => "対数1.41",
        3 => "逆S字",
        4 => "直線",
        5 => "一定",
        6 => "S字",
        7 => "指数1.41",
        8 => "Sine",
        9 => "指数3",
        _ => "S字",
    };

    public static string FormatTimecode(long frame, int sampleRate)
    {
        var rate = Math.Max(1, sampleRate);
        return FormatDuration(frame / (double)rate);
    }

    public static string EditHistoryRange(string verb, int sampleRate, long startFrame, long endFrame, string? extra = null)
    {
        var range = $"{FormatTimecode(startFrame, sampleRate)}–{FormatTimecode(endFrame, sampleRate)}";
        return string.IsNullOrEmpty(extra) ? $"{verb}  {range}" : $"{verb}  {range}  {extra}";
    }

    public static string EditHistoryPoint(string verb, int sampleRate, long frame, string? extra = null)
    {
        var at = FormatTimecode(frame, sampleRate);
        return string.IsNullOrEmpty(extra) ? $"{verb}  {at}" : $"{verb}  {at}  {extra}";
    }

    public static string EditHistoryQuote(string? text)
    {
        text = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0)
        {
            return "（空）";
        }

        const int max = 18;
        return text.Length > max ? text[..max] + "…" : text;
    }

    public static string EditHistoryMarkers(
        string name,
        IReadOnlyList<MarkerSnapshot> before,
        IReadOnlyList<MarkerSnapshot> after,
        int sampleRate)
    {
        var verb = EditHistoryName(name);
        var beforeFrames = before.Select(marker => marker.Frame).ToHashSet();
        var afterFrames = after.Select(marker => marker.Frame).ToHashSet();
        var removed = before.Where(marker => !afterFrames.Contains(marker.Frame)).ToArray();
        var added = after.Where(marker => !beforeFrames.Contains(marker.Frame)).ToArray();
        if (removed.Length > 0 && added.Length == 0)
        {
            return FormatMarkerTimes(verb, removed, sampleRate);
        }

        if (removed.Length == 1 && added.Length == 1)
        {
            return $"{verb}  {FormatTimecode(removed[0].Frame, sampleRate)}→{FormatTimecode(added[0].Frame, sampleRate)}";
        }

        if (removed.Length > 0)
        {
            return $"{verb}  {removed.Length}個";
        }

        return verb;
    }

    private static string FormatMarkerTimes(string verb, IReadOnlyList<MarkerSnapshot> markers, int sampleRate)
    {
        if (markers.Count == 0)
        {
            return verb;
        }

        if (markers.Count <= 3)
        {
            var times = string.Join(", ", markers.Select(marker => FormatTimecode(marker.Frame, sampleRate)));
            return $"{verb}  {times}";
        }

        return $"{verb}  {markers.Count}個";
    }

    public static string FormatFileBytes(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        var mega = bytes / (1024d * 1024d);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mega:0.00} MB");
    }

    public static string FormatDuration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            return "00:00.000";
        }

        var minutes = (int)(seconds / 60d);
        var rest = seconds - minutes * 60d;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{minutes:00}:{rest:00.000}");
    }

    public static bool TryParseDuration(string? text, out double seconds)
    {
        seconds = 0;
        text = (text ?? string.Empty).Trim().Replace(',', '.');
        if (text.Length == 0)
        {
            return false;
        }

        var parts = text.Split(':');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (parts.Length == 1)
        {
            return double.TryParse(parts[0], System.Globalization.NumberStyles.Float, culture, out seconds)
                && seconds >= 0
                && !double.IsInfinity(seconds);
        }

        if (!int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, culture, out var major)
            || major < 0)
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, culture, out var rest)
                || rest < 0
                || rest >= 60)
            {
                return false;
            }

            seconds = major * 60d + rest;
            return !double.IsInfinity(seconds);
        }

        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, culture, out var minutes)
            || minutes < 0
            || minutes >= 60
            || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, culture, out var frac)
            || frac < 0
            || frac >= 60)
        {
            return false;
        }

        seconds = major * 3600d + minutes * 60d + frac;
        return !double.IsInfinity(seconds);
    }
}
