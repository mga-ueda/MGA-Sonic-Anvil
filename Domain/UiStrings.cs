using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// ユーザーに見えるすべての表示テキストを一箇所に集約する。
/// 日本語版は現状の文言（英語のままの UI も含む）を維持し、英語版だけ日本語を訳す。
/// </summary>
internal static partial class UiStrings
{
    public static UiLanguage Language { get; private set; } = UiLanguage.Japanese;

    public static event EventHandler? LanguageChanged;

    public static bool IsJapanese => Language == UiLanguage.Japanese;

    public static void SetLanguage(UiLanguage language)
    {
        if (Language == language)
        {
            return;
        }

        Language = language;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static UiLanguageChoice ParseLanguageChoice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UiLanguageChoice.Auto;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("os", StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguageChoice.Auto;
        }

        if (trimmed.Equals("en", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("english", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(nameof(UiLanguage.English), StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguageChoice.English;
        }

        if (trimmed.Equals("ja", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("jp", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("japanese", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(nameof(UiLanguage.Japanese), StringComparison.OrdinalIgnoreCase))
        {
            return UiLanguageChoice.Japanese;
        }

        return UiLanguageChoice.Auto;
    }

    public static UiLanguage ParseLanguage(string? value) =>
        ResolveLanguage(ParseLanguageChoice(value));

    public static UiLanguage ResolveLanguage(UiLanguageChoice choice, string? osTwoLetterIso = null)
    {
        if (choice == UiLanguageChoice.English)
        {
            return UiLanguage.English;
        }

        if (choice == UiLanguageChoice.Japanese)
        {
            return UiLanguage.Japanese;
        }

        var os = osTwoLetterIso
            ?? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return os.Equals("ja", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Japanese
            : UiLanguage.English;
    }

    public static string ToStoredValue(UiLanguageChoice choice) =>
        choice switch
        {
            UiLanguageChoice.English => "en",
            UiLanguageChoice.Japanese => "ja",
            _ => "auto",
        };

    public static string Get(string japanese, string english) =>
        IsJapanese ? japanese : english;

    public static string Format(string japaneseFormat, string englishFormat, params object[] args) =>
        string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Get(japaneseFormat, englishFormat),
            args);

    public const string AppName = AppVersion.ProductName;

    public static string CopyrightText => Get(
        "© 2026 " + AppVersion.CompanyName + "  ",
        "© 2026 " + AppVersion.CompanyName + "  ");

    public static string CopyrightGitHub => Get("GitHub", "GitHub");

    public static string UntitledDocument => Get("untitled", "untitled");

    public static string LabelAlwaysOnTop => Get("Always on Top", "Always on Top");
    public static string LabelAudioApi => Get("Audio API", "Audio API");
    public static string LabelAudioDevice => Get("Device", "Device");
    public static string LabelAudioApiWaveOut => Get("WaveOut", "WaveOut");
    public static string LabelAudioApiWasapi => Get("WASAPI", "WASAPI");
    public static string LabelAudioApiAsio => Get("ASIO", "ASIO");
    public static string ButtonOk => Get("OK", "OK");
    public static string ButtonCancel => Get("Cancel", "Cancel");
    public static string ButtonYes => Get("はい", "Yes");
    public static string ButtonNo => Get("いいえ", "No");
    public static string ButtonSaveAllAndExit => Get("すべて保存して終了", "Save all and quit");
    public static string ButtonDiscardAllAndExit => Get("すべて保存せずに終了", "Quit without saving any");
    public static string DialogSettingsTitle => Get("設定", "Settings");
    public static string LabelUiLanguage => Get("言語", "Language");
    public static string LabelLanguageAuto => Get("Auto", "Auto");
    public static string LabelLanguageJapanese => Get("Japanese", "Japanese");
    public static string LabelLanguageEnglish => Get("English", "English");
    public static string LabelFadeCurveDefaults => Get("フェードカーブ既定", "Default Fade Curves");
    public static string LabelDefaultFadeIn => Get("波形フェードイン", "Waveform Fade In");
    public static string LabelDefaultFadeOut => Get("波形フェードアウト", "Waveform Fade Out");
    public static string AccessibleAudioSettingsButton => Get("設定", "Settings");
    public static string TipAudioSettings => Get(
        "設定 (Ctrl+Shift+O)\n表示言語、音声出力、ラウドネスターゲット、フェードカーブ既定、MP3（Windows / LAME）、同時書き出しを設定します。",
        "Settings (Ctrl+Shift+O)\nConfigure UI language, audio output, loudness target, default fade curves, MP3 (Windows / LAME), and parallel exports.");
    public static string LabelMp3Encode => Get("MP3", "MP3");
    public static string TipMp3Encode => Get(
        "MP3 保存の経路です。LAME のパスが有効なら lame.exe、空欄または無効なら Windows です。",
        "MP3 save path. A valid LAME path uses lame.exe; empty or invalid uses Windows.");
    public static string LabelWindowsMp3BitRate => Get("Windows ビットレート", "Windows Bit Rate");
    public static string LabelKbps => Get("kbps", "kbps");
    public static string LabelLamePath => Get("LAME のパス", "LAME Path");
    public static string LabelLameOptions => Get("LAME オプション", "LAME Options");
    public static string LabelExportParallel => Get("同時書き出し", "Parallel exports");
    public static string LabelExportParallelAuto(int workers) => Format(
        "Auto（{0}）",
        "Auto ({0})",
        workers);
    public static string ButtonBrowse => Get("参照", "Browse");
    public static string FilterLameExe => Get(
        "LAME|lame.exe|実行ファイル|*.exe|すべて|*.*",
        "LAME|lame.exe|Executable|*.exe|All|*.*");
    public static string FilterSaveMp3 => Get("MP3|*.mp3", "MP3|*.mp3");
    public static string MenuSaveMp3 => Get("MP3 として保存", "Save as MP3");
    public static string ErrorLameFailed => Get(
        "LAME での MP3 変換に失敗しました。",
        "LAME failed to encode the MP3.");
    public static string ErrorWindowsMp3Failed => Get(
        "Windows での MP3 変換に失敗しました。",
        "Windows failed to encode the MP3.");
    public static string LabelMp3EncoderLame => Get("LAME", "LAME");
    public static string LabelMp3EncoderWindows => Get("Windows", "Windows");
    public static string InfoMp3Wrote(Mp3EncoderKind encoder) => encoder == Mp3EncoderKind.Lame
        ? Get("LAME で MP3 を書き出しました。", "Wrote the MP3 with LAME.")
        : Get("Windows で MP3 を書き出しました。", "Wrote the MP3 with Windows.");
    public static string FilterSaveWave => Get("Wave|*.wav", "Wave|*.wav");
    public static string MenuExportWave => Get("Wave で書き出す", "Export Wave");
    public static string MenuExportMp3 => Get("MP3 で書き出す", "Export MP3");
    public static string ExportFolderTitle => Get("書き出し先フォルダ", "Export folder");
    public static string ConfirmOverwriteFiles(int count, string list) => Format(
        "既にあるファイルが {0} 件あります。上書きしますか？{1}{1}{2}{1}{1}はい＝上書き、いいえ＝それらをスキップ、キャンセル＝中止",
        "{0} existing file(s). Overwrite them?{1}{1}{2}{1}{1}Yes = overwrite, No = skip them, Cancel = stop",
        count,
        Environment.NewLine,
        list);
    public static string OverwriteMoreFiles(int hidden, int total) => Format(
        "ほか {0} 件（合計 {1} 件）",
        "and {0} more ({1} total)",
        hidden,
        total);
    public static string ExportNone => Get(
        "書き出すファイルがありません。",
        "There is nothing to export.");
    public static string ExportWaveDone(int count) => Format(
        "Wave を {0} 件書き出しました。",
        "Exported {0} Wave file(s).",
        count);
    public static string ExportMp3Done(int count, Mp3EncoderKind encoder) => Format(
        "{1} で MP3 を {0} 件書き出しました。",
        "Exported {0} MP3 file(s) with {1}.",
        count,
        encoder == Mp3EncoderKind.Lame ? LabelMp3EncoderLame : LabelMp3EncoderWindows);
    public static string ExportPartial(
        int ok,
        int skipped,
        int failed,
        string? encoder,
        string errors) => Format(
        "成功 {0} 件{1}{2}スキップ {3} 件{2}失敗 {4} 件{5}",
        "Succeeded: {0}{1}{2}Skipped: {3}{2}Failed: {4}{5}",
        ok,
        string.IsNullOrEmpty(encoder) ? string.Empty : Format("（{0}）", " ({0})", encoder),
        Environment.NewLine,
        skipped,
        failed,
        string.IsNullOrWhiteSpace(errors) ? string.Empty : Environment.NewLine + errors);
    public static string TipWindowsMp3BitRate => Get(
        "LAME のパスが空欄、または無効なときの Windows（Media Foundation）出力ビットレート。既定 192 kbps。CBR。",
        "Windows (Media Foundation) CBR bit rate when the LAME path is empty or invalid. Default 192 kbps.");
    public static string TipLamePath => Get(
        "lame.exe の場所。空欄または無効なら Windows で出力します。アプリには同梱しません。",
        "Path to lame.exe. Empty or invalid uses Windows output. Not bundled with the app.");
    public static string TipLameBrowse => Get("lame.exe を選びます。", "Choose lame.exe.");
    public static string TipLameOptions => Get(
        "lame に渡すオプション。入出力ファイルは自動で末尾に付けます。空欄は既定の -V2 --noreplaygain です。",
        "Flags passed to lame. Input and output files are appended automatically. Empty falls back to -V2 --noreplaygain.");
    public static string TipExportParallel => Get(
        "全タブの Wave / MP3 を同時に書く本数。Auto は CPU コア数の 1/4（最低 1）。プルダウンの上限はコア数の 1/2。Windows の MP3 は常に 1 本。",
        "How many Wave / MP3 tab exports run at once. Auto is a quarter of the CPU cores (at least 1). The dropdown max is half the cores. Windows MP3 is always one at a time.");

    public static string LabelMono => Get("Mono", "Mono");
    public static string LabelStereo => Get("Stereo", "Stereo");
    public static string LabelHertz => Get("Hz", "Hz");
    public static string LabelConvertCustomRate => Get("任意", "Custom");
    public static string LabelPeak => Get("Peak", "Peak");
    public static string LabelRms => Get("RMS", "RMS");
    public static string LabelVolume => Get("音量", "Volume");

    public static string FormatSignedDb(double gainDb)
    {
        var value = Math.Clamp(Math.Round(gainDb, 1, MidpointRounding.AwayFromZero), -60, 60);
        return value.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture)
            + " "
            + LabelDb;
    }

    public static string MenuOpen => Get("開く", "Open");
    public static string MenuSaveAs => Get("名前を付けて保存", "Save As");

    public static string DialogExitTitle => Get("終了確認", "Quit");
    public static string DialogExitBody => Get(
        "アプリケーションを終了しますか？",
        "Quit the application?");

    public static string DialogUpdateAvailableTitle => Get(
        "アップデートのお知らせ",
        "Update available");

    public static string DialogUpdateAvailableBody(
        string localVersion,
        string remoteVersion,
        bool isPrerelease) => Format(
        "新しいバージョンがあります。{0}{0}"
        + "現在: {1}{0}"
        + "最新: {2}{3}{0}{0}"
        + "GitHub のリリースページを開きますか？{0}"
        + "（自動ダウンロードは行いません）",
        "A newer version is available.{0}{0}"
        + "Current: {1}{0}"
        + "Latest: {2}{3}{0}{0}"
        + "Open the GitHub release page?{0}"
        + "(This app does not download updates automatically.)",
        Environment.NewLine,
        localVersion,
        remoteVersion,
        isPrerelease
            ? Get("（プレリリース）", " (pre-release)")
            : string.Empty);

    public static string DialogOpenGithubFailed => Get(
        "GitHub を開けませんでした。",
        "Unable to open GitHub.");

    public static string ConfirmSaveFor(string name) => Format(
        "{0} に未保存の変更があります。保存しますか？",
        "{0} has unsaved changes. Save them?",
        name);
    public static string ConfirmSaveBatchHint(int remaining) => Format(
        "未保存のタブが {0} 件あります。まとめて終了することもできます。",
        "{0} tab(s) have unsaved changes. You can finish them all at once.",
        remaining);

    public static string ErrorOpenFailed => Get("読み込みに失敗しました。", "Failed to open the file.");
    public static string StatusOpeningFiles(int current, int total, string name) => Format(
        "開いています {0} / {1}  {2}",
        "Opening {0} / {1}  {2}",
        current,
        total,
        name);
    public static string ErrorSaveFailed => Get("書き出しに失敗しました。", "Failed to save the file.");
    public static string ErrorAiffExport => Get(
        "AIFF の書き出しには対応していません。Wave または MP3 を選んでください。",
        "AIFF export is not supported. Choose Wave or MP3.");
    public static string ErrorNoDocument => Get("ファイルが開かれていません。", "No file is open.");
    public static string ErrorNoSelection => Get("選択範囲がありません。", "Nothing is selected.");
    public static string ErrorClipboardEmpty => Get("クリップボードが空です。", "The clipboard is empty.");
    public static string ErrorEmptyAfterDelete => Get(
        "ファイル全体は削除できません。",
        "The entire file cannot be deleted.");
    public static string ErrorMp3NoRegionLoop => Get(
        "MP3 にはリージョンとサンプルループを付けられません。マーカーは置けますが、MP3 保存では書き出しません。",
        "MP3 cannot take regions or a sample loop. Markers are allowed, but they are not written when you save as MP3.");
    public static string LabelLoudness => Get("Loudness", "Loudness");
    public static string LabelLoudnessShortTerm => Get("Short Term", "Short Term");
    public static string LabelLoudnessIntegrated => Get("Integrated", "Integrated");
    public static string LabelLoudnessMomentary => Get("Momentary", "Momentary");
    public static string LabelMaxLufs => Get("LKFS Max", "LKFS Max");
    public static string LabelLufs => Get("LKFS", "LKFS");
    public static string LabelLra => Get("Loudness Range", "Loudness Range");
    public static string LabelLu => Get("LU", "LU");
    public static string LabelTruePeak => Get("True Peak", "True Peak");
    public static string LabelDb => Get("dB", "dB");
    public static string LabelLoudnessTarget => Get("ラウドネスターゲット", "Loudness Target");
    public static string ErrorLoudnessTargetRange => Get(
        "ラウドネスターゲットは -70 から 0 の LKFS で入力してください。",
        "Enter a loudness target between -70 and 0 LKFS.");
    public static string OverlaySampleRateConvert => Get("サンプリングレート変換", "Sample rate conversion");
    public static string OverlayExportWave => Get("Wave を書き出しています", "Exporting Wave");
    public static string OverlayExportMp3 => Get("MP3 を書き出しています", "Exporting MP3");
    public static string OverlayExportCount(int finished, int total) => Format(
        "{0} / {1}",
        "{0} / {1}",
        finished,
        total);

    public static string FilterOpenAudio => Get(
        "Audio|*.wav;*.wave;*.aif;*.aiff;*.mp3|Wave|*.wav;*.wave|AIFF|*.aif;*.aiff|MP3|*.mp3|All|*.*",
        "Audio|*.wav;*.wave;*.aif;*.aiff;*.mp3|Wave|*.wav;*.wave|AIFF|*.aif;*.aiff|MP3|*.mp3|All|*.*");

    public static string FilterSaveAudio => Get(
        "Wave|*.wav|MP3|*.mp3",
        "Wave|*.wav|MP3|*.mp3");

    public static string TooltipPlay => Get("再生 / 停止 (Space)", "Play / stop (Space)");
    public static string TooltipStop => Get("停止 (Space)", "Stop (Space)");
    public static string TooltipGoToStart => Get("先頭 (Ctrl+Home)", "Go to start (Ctrl+Home)");
    public static string TooltipGoToEnd => Get("末尾 (Ctrl+End)", "Go to end (Ctrl+End)");
    public static string TooltipTimeZoomIn => Get("時間拡大 (↑)", "Zoom in time (↑)");
    public static string TooltipTimeZoomOut => Get("時間縮小 (↓)", "Zoom out time (↓)");
    public static string TooltipTimeZoomMax => Get("時間最大 (Ctrl+↑)", "Time zoom max (Ctrl+↑)");
    public static string TooltipTimeZoomReset => Get("全体表示 (Ctrl+↓)", "Fit all (Ctrl+↓)");
    public static string TooltipAmpZoomIn => Get("振幅拡大 (Shift+↑)", "Zoom in amplitude (Shift+↑)");
    public static string TooltipAmpZoomOut => Get("振幅縮小 (Shift+↓)", "Zoom out amplitude (Shift+↓)");
    public static string TooltipAmpZoomMax => Get("振幅最大 (Ctrl+Shift+↑)", "Amplitude zoom max (Ctrl+Shift+↑)");
    public static string TooltipAmpZoomReset => Get("振幅リセット (Ctrl+Shift+↓)", "Reset amplitude zoom (Ctrl+Shift+↓)");
    public static string TooltipFadeIn => Get("フェードイン (I)", "Fade in (I)");
    public static string TooltipFadeOut => Get("フェードアウト (O)", "Fade out (O)");
    public static string TooltipNormalize => Get("ノーマライズ (N)", "Normalize (N)");
    public static string TooltipDelete => Get("部分削除 (Delete)", "Ripple delete (Delete)");
    public static string TooltipSave => Get("保存 (Ctrl+S)", "Save (Ctrl+S)");
    public static string TooltipSaveMp3 => Get("MP3 として保存 (Ctrl+Shift+M)", "Save as MP3 (Ctrl+Shift+M)");
    public static string TooltipTipsToggle => Get("Tips の表示", "Show Tips");
    public static string TooltipManualHelp => Get("マニュアル", "Manual");

    public static string TipPlay => Get(
        "再生 / 停止 (Space)\n停止で開始位置へ戻る\nEnter でその場停止\nCtrl+ドラッグでスクラブ\nCtrl+Space 3秒前から\nAlt+Enter 再生開始位置からやり直し",
        "Play / stop (Space)\nStop returns to the start position\nEnter pauses in place\nCtrl+drag to scrub\nCtrl+Space from 3 seconds earlier\nAlt+Enter restarts from the playback start");
    public static string TipStop => Get("停止（開始位置へ戻る）", "Stop (return to start)");
    public static string TipGoToStart => Get("先頭 (Ctrl+Home)", "Go to start (Ctrl+Home)");
    public static string TipGoToEnd => Get("末尾 (Ctrl+End)", "Go to end (Ctrl+End)");
    public static string TipTimeZoomIn => Get("時間拡大 (↑)\nホイールでも拡大", "Zoom in time (↑)\nMouse wheel also zooms");
    public static string TipTimeZoomOut => Get("時間縮小 (↓)\nホイールでも縮小", "Zoom out time (↓)\nMouse wheel also zooms");
    public static string TipTimeZoomMax => Get("時間 32倍 / 最大 (Ctrl+↑)", "Time zoom 32× / max (Ctrl+↑)");
    public static string TipTimeZoomReset => Get("全体表示 (Ctrl+↓)", "Fit all (Ctrl+↓)");
    public static string TipAmpZoomIn => Get("振幅拡大 (Shift+↑)\nCtrl+ホイールでも拡大", "Zoom in amplitude (Shift+↑)\nCtrl+wheel also zooms");
    public static string TipAmpZoomOut => Get("振幅縮小 (Shift+↓)\nCtrl+ホイールでも縮小", "Zoom out amplitude (Shift+↓)\nCtrl+wheel also zooms");
    public static string TipAmpZoomMax => Get("振幅最大 (Ctrl+Shift+↑)", "Amplitude zoom max (Ctrl+Shift+↑)");
    public static string TipAmpZoomReset => Get("振幅リセット (Ctrl+Shift+↓)", "Reset amplitude zoom (Ctrl+Shift+↓)");
    public static string TipFadeIn => Get(
        "フェードイン (I)\nカーブを選び Space で試聴、Enter で実行。1–9 でカーブを選択。未選択なら全体",
        "Fade in (I)\nPick a curve, Space to preview, Enter to apply. 1–9 select a curve. Uses the whole file if nothing is selected");
    public static string TipFadeOut => Get(
        "フェードアウト (O)\nカーブを選び Space で試聴、Enter で実行。1–9 でカーブを選択。未選択なら全体",
        "Fade out (O)\nPick a curve, Space to preview, Enter to apply. 1–9 select a curve. Uses the whole file if nothing is selected");

    public static string LabelFadeCurve(int shapeId) => shapeId switch
    {
        0 => Get("Logarithmic (Base 3)", "Logarithmic (Base 3)"),
        1 => Get("Sine (Constant Power Fade In)", "Sine (Constant Power Fade In)"),
        2 => Get("Logarithmic (Base 1.41)", "Logarithmic (Base 1.41)"),
        3 => Get("Inverted S-Curve", "Inverted S-Curve"),
        4 => Get("Linear", "Linear"),
        5 => Get("Constant", "Constant"),
        6 => Get("S-Curve", "S-Curve"),
        7 => Get("Exponential (Base 1.41)", "Exponential (Base 1.41)"),
        8 => Get("Sine (Constant Power Fade Out)", "Sine (Constant Power Fade Out)"),
        9 => Get("Exponential (Base 3)", "Exponential (Base 3)"),
        _ => Get("S-Curve", "S-Curve"),
    };

    public static string TipFadeShape(int shapeId) => shapeId switch
    {
        0 => Get("対数（Base 3）。立ち上がりが早く、終わりがなだらかです。", "Logarithmic (Base 3). Rises quickly and eases out."),
        1 => Get("定電力フェードイン（Sine）。", "Constant-power fade in (Sine)."),
        2 => Get("対数（Base 1.41）。", "Logarithmic (Base 1.41)."),
        3 => Get("逆 S 字。", "Inverted S-curve."),
        4 => Get("直線。", "Linear."),
        5 => Get("一定（終端まで値を保ち、最後で切り替わります）。", "Constant (holds the value, then switches at the end)."),
        6 => Get("S 字。", "S-curve."),
        7 => Get("指数（Base 1.41）。立ち上がりが遅く、終わりが急です。", "Exponential (Base 1.41). Rises slowly and finishes steeply."),
        8 => Get("定電力フェードアウト（Sine）。", "Constant-power fade out (Sine)."),
        9 => Get("指数（Base 3）。立ち上がりが遅く、終わりが急です。", "Exponential (Base 3). Rises slowly and finishes steeply."),
        _ => Get("S 字。", "S-curve."),
    };

    public static string TipNormalize => Get(
        "ノーマライズ (N)\nピークを -0.1 dB に合わせる",
        "Normalize (N)\nFit the peak to -0.1 dB");
    public static string TipVolume => Get(
        "音量 (V)\n↑↓／ホイールで 0.1 dB（Shift 1／Ctrl 3／Ctrl+Shift 6）。Space で試聴、Enter で実行。未選択なら全体。波形全体の Integrated LKFS / RMS / Peak の変化を先に表示。ラウドネス表示中は曲線も同じ dB で追従。",
        "Volume (V)\n↑↓ / wheel by 0.1 dB (Shift 1 / Ctrl 3 / Ctrl+Shift 6). Space previews, Enter applies. Uses the whole file if nothing is selected. Shows how whole-file Integrated LKFS / RMS / Peak will change. In loudness view the curve follows the same dB.");
    public static string TipDelete => Get(
        "部分削除 (Delete)\n選択範囲を詰めて削除\n選択中のマーカー / リージョンはまとめて削除\nCtrl+Del でマーカー削除",
        "Ripple delete (Delete)\nRemove the selection and close the gap\nSelected markers / regions are deleted together\nCtrl+Del deletes markers");
    public static string TipSave => Get(
        "保存 (Ctrl+S)\nCtrl+Shift+S で別名保存\nCtrl+Shift+M で MP3 保存（今のタブは開いたまま。書き出した MP3 は読み込まない）\nCtrl+Shift+Alt+M で全タブを MP3 書き出し\nMP3 は PCM 16bit から再エンコード。LAME のパスが有効ならそれを使い、空欄または無効なら Windows（既定 192 kbps）。成功時に LAME / Windows を表示。失敗はダイアログ。マーカー／リージョン／ループは MP3 に書きません",
        "Save (Ctrl+S)\nCtrl+Shift+S to save as\nCtrl+Shift+M to save as MP3 (keeps the current tab; does not open the written MP3)\nCtrl+Shift+Alt+M exports every tab as MP3\nMP3 is re-encoded from 16-bit PCM. A valid LAME path is used; empty or invalid falls back to Windows (default 192 kbps). Success shows LAME / Windows. Failures open a dialog. Markers / regions / loops are not written to MP3");
    public static string TipSaveMp3 => Get(
        "MP3 として保存 (Ctrl+Shift+M)\n別名保存と同じく書き出すだけ。今のタブは開いたまま、書き出した MP3 は読み込まない。Ctrl+Shift+Alt+M で全タブを MP3 書き出し。設定の LAME があればそれを使い、空欄または無効なら Windows（既定 192 kbps）。成功時にどちらで書いたかを表示。失敗はダイアログ。マーカー／リージョン／ループは書きません",
        "Save as MP3 (Ctrl+Shift+M)\nWrites a file like Save As; keeps the current tab and does not open the written MP3. Ctrl+Shift+Alt+M exports every tab as MP3. Uses LAME when the path is valid; otherwise Windows (default 192 kbps). Success shows which encoder ran. Failures open a dialog. Markers / regions / loops are not written");
    public static string TipOpen => Get(
        "開く (Ctrl+O)\nドロップでも可。複数ファイルはタブで追加。\nWave / AIFF / MP3\nCtrl+W でタブを閉じる（未保存なら保存確認）\nCtrl+Shift+T で閉じたタブを開き直す（何度でも）\nCtrl+Tab で次のタブ\nタブが増えると先にアクティブ以外を縮める。6文字を切るまで縮めても収まらなければ左右ボタンで送る",
        "Open (Ctrl+O)\nDrop also works. Multiple files open as extra tabs.\nWave / AIFF / MP3\nCtrl+W closes the tab (asks to save if dirty)\nCtrl+Shift+T reopens closed tabs (more than one)\nCtrl+Tab goes to the next tab\nExtra tabs shrink (inactive first) before scroll arrows. Arrows appear only if a title would fall below 6 characters");
    public static string TipCloseTab => Get(
        "タブを閉じる (Ctrl+W)。Ctrl+Shift+T で開き直せる",
        "Close tab (Ctrl+W). Ctrl+Shift+T reopens it");
    public static string TipTabScrollLeft => Get("左のタブを表示", "Show tabs to the left");
    public static string TipTabScrollRight => Get("右のタブを表示", "Show tabs to the right");
    public static string TipOverview => Get(
        "波形全体。明るい部分が表示中の範囲。ドラッグで移動（中央をスクラブ）　ホイールで拡縮（再生ヘッド基準）",
        "Whole file. The bright area is the current view. Drag to move (scrubs the center). Wheel zooms around the playhead");
    public static string TipSpectrum => Get(
        "再生出力の LED スペクトラムです。1/3oct 相当の帯域とピークホールド。Layer Music Checker と同じ検波です。",
        "LED spectrum of the playback output. Third-octave-style bands and peak hold, same detection as Layer Music Checker.");
    public static string TipLoudness => Get(
        "再生出力のラウドネス（ITU-R BS.1770 / EBU R128）。Short Term・Integrated・Momentary Max、Loudness Range、True Peak。ターゲット LKFS は設定で変更。数値は青＝余裕、橙＝接近、赤＝超過（LKFS はターゲット、True Peak は 0 dBTP、Loudness Range は 20/25 LU）。停止後も最後の値を残し、再生し直すと測り直します。音声は変えません。",
        "Playback loudness (ITU-R BS.1770 / EBU R128): Short Term, Integrated, Momentary Max, Loudness Range, True Peak. Target LKFS is in Settings. Values: blue = headroom, orange = approaching, red = over (LKFS vs target, True Peak vs 0 dBTP, Loudness Range vs 20/25 LU). Holds the last reading after stop; a new play measures again. Does not change the audio.");
    public static string TipVectorScope => Get(
        "再生出力の位相相関とベクターオーディオスコープです。正方形は縦が Mid、横が Side。下の数値は L/R の相関（+1 同相 / 0 無相関 / -1 逆相）です。停止後は点が中心へゆっくり戻ります。",
        "Phase correlation and a vector audio scope of the playback output. The square is Mid (vertical) and Side (horizontal). The number below is L/R correlation (+1 in phase / 0 uncorrelated / -1 inverted). After stop, the point slowly returns to the center.");
    public static string TipAudioApi => Get(
        "再生 API（WaveOut / WASAPI / ASIO）",
        "Playback API (WaveOut / WASAPI / ASIO)");
    public static string TipAudioDevice => Get("再生デバイス", "Playback device");
    public static string TipUiLanguage => Get(
        "表示言語。Auto は OS が日本語なら Japanese、それ以外は English。",
        "UI language. Auto is Japanese if the OS is Japanese, otherwise English.");
    public static string TipLoudnessTarget => Get(
        "ラウドネスメーターのターゲット（LKFS）。-70 から 0。色分けの基準です。音声は変えません。",
        "Loudness meter target (LKFS), from -70 to 0. Used for the color scale. Does not change the audio.");
    public static string TipFadeCurveDefaults => Get(
        "I / O で開くフェードカーブの初期選択です。",
        "Initial curve shown when you open fade in / fade out (I / O).");
    public static string TipSettingsOk => Get(
        "設定を保存して閉じます。",
        "Save settings and close.");
    public static string TipSettingsCancel => Get(
        "変更を破棄して閉じます。",
        "Discard changes and close.");
    public static string TipStatusFormat => Get(
        "サンプリングレート / ビット深度 / チャンネル / 形式 / 容量。S / B / C で変換。変換や範囲削除で長さが変わると容量は推測サイズになり赤。確定項目は保存まで赤。",
        "Sample rate / bit depth / channels / format / size. S / B / C convert. Size turns red as an estimate after conversion or a range delete that changes length. Confirmed fields stay red until you save.");
    public static string TipLevelMeter => Get(
        "再生出力の Peak / RMS。内側 2 本が Peak（上の赤ランプがクリップ）、外側 2 本が RMS。下の数値は Peak 行／RMS 行。",
        "Playback Peak / RMS. Inner two bars are Peak (red lamps clip), outer two are RMS. Numbers below are Peak then RMS.");
    public static string TipTimeScroll => Get(
        "表示範囲を左右に動かします。つまみをドラッグ、またはトラックをクリック。",
        "Pan the view. Drag the thumb, or click the track.");
    public static string TipCopyright => Get(
        "© MIYABI GAME AUDIO INC. MIT License。",
        "© MIYABI GAME AUDIO INC. MIT License.");
    public static string TipEditHistory => Get(
        "編集履歴 (U)。↑↓ で移動、Enter で確定、Esc でキャンセル。Ctrl+クリック／Shift+↑↓ で選択、Ctrl+C でコピー、別ファイルで Ctrl+V。セーブせず終了しても、戻せる操作は次回起動時に履歴へ戻す。",
        "Edit history (U). ↑↓ move, Enter apply, Esc cancel. Ctrl+click / Shift+↑↓ select, Ctrl+C copy, Ctrl+V in another file. Replayable edits also come back after a restart without saving.");
    public static string TipFormatSampleRate => Get(
        "サンプリングレートを変換します (S)。Space で試聴、Enter で確定。1–9 で項目。",
        "Convert sample rate (S). Space previews, Enter applies. 1–9 pick a row.");
    public static string TipFormatBitDepth => Get(
        "ビット深度を変換します (B)。Space で試聴、Enter で確定。1–9 で項目。",
        "Convert bit depth (B). Space previews, Enter applies. 1–9 pick a row.");
    public static string TipFormatChannels => Get(
        "チャンネル数を変換します (C)。Enter で確定。1–2 で項目。",
        "Convert channel count (C). Enter applies. 1–2 pick a row.");
    public static string TipFormatCustomRate => Get(
        "任意 Hz。↑↓／ホイールで 1（Shift 10／Ctrl 100／Ctrl+Shift 1000）。Enter で確定。範囲 1000–384000。",
        "Custom Hz. ↑↓ / wheel by 1 (Shift 10 / Ctrl 100 / Ctrl+Shift 1000). Enter applies. Range 1000–384000.");
    public static string TipWaveform => Get(
        "ドラッグで選択　Ctrl+ドラッグでスクラブ　Esc または Shiftなし移動で解除　Shift＋移動は選択　Shift+←→ で伸長（点表示時は1サンプル）　Home/End で画面端　Shift+PgUp/PgDn で5%　Ctrl+Shift+Home/End で前後すべて　Ctrl+A で全選択　ダブルクリックで区間（マーカー間）　ガイドはマーカー / ループ端に吸着\n"
        + "ホイール=時間ズーム（再生ヘッド基準）　Shift+ホイール=パン　Ctrl+ホイール=振幅\n"
        + "←→ シーク（選択中のマーカー / リージョン端 / ループ端は移動。点表示時は1サンプル、Shift で3倍）　Ctrl+←→ 前後のマーカー / リージョン端 / サンプルループ端　テンキーで番号（無ければ表示位置）　Z / . 中央寄せ（再生中はセンターロックの切替、停止で解除）　0-9 表示位置　L（G でも可）で選択（無ければサンプルループ / -L）の末尾3秒前からループ再生　Shift+L で選択をサンプルループに設定（同じ範囲でもう一度で解除）　Shift+R で選択をリージョンに設定（同じ範囲で繰り返すと2等分、3等分…と打ち直し。解除は右クリック／Delete）　マーカー / リージョンフラッグ / ループバーを右クリックで削除　S サンプリングレート　B ビット深度　C チャンネル数　M / Ins マーカー（選択中は両端。続けて打つと中央→3等分…と打ち直し。同じ位置には重ならない）　フラッグをクリックで端を選択 / Shift+クリックで範囲 / Ctrl+クリックで追加 / ドラッグまたは ←→ で移動（Shift で3倍） / Delete または Ctrl+Del で削除　Ctrl+Shift+R でリネーム　ダブルクリックでコメント / リージョン名（-A ライム / -L ブルー / -E 赤 / -R グレー）\n"
        + "マーカー / リージョン端 / ループ端で Alt+←→ は1px（点表示時は1サンプル）、Shift で3倍、Ctrl で手前のマーカーとセット（リージョン / ループは両端）　X で表示範囲をシーク前後にリニアフェード（前=アウト / 後=イン）　V で音量（dB。↑↓／ホイール、Space 試聴、Enter 実行。波形全体の LKFS / RMS / Peak を先に表示）　Ctrl+X / C / V でカット・コピー・ペースト（範囲内マーカー含む。選択がリージョンと一致すればリージョンも）　T で現在時間　U で編集履歴　A で波形 / スペクトログラム / 重ね表示 / ラウドネス解析（Short Term LKFS。白い幅は ±1 LU、半透明。波形は上半分のピーク dBFS を線で同じ目盛へ。曲線は原色のシアン／橙／赤。LKFS は左目盛。スペクトログラム・重ね・ラウドネスでは -A/-L/-E/-R とループ／リージョンの下塗りなし）　Ctrl+Shift+E で Wwise EXPORT（Wave 単体）　Ctrl+Shift+M で MP3 保存　Ctrl+Shift+Alt+M で全タブを MP3 書き出し　MP3 ではリージョン／ループ不可。マーカーは置けるが MP3 保存では残らない",
        "Drag to select. Ctrl+drag to scrub. Esc or a move without Shift clears the selection. Shift+move extends. Shift+←/→ grows it (1 sample when dots are shown). Home/End jump to the view edge. Shift+PgUp/PgDn by 5%. Ctrl+Shift+Home/End selects all before/after. Ctrl+A selects all. Double-click selects a span (between markers). The guide snaps to markers / loop edges.\n"
        + "Wheel = time zoom (around the playhead). Shift+wheel = pan. Ctrl+wheel = amplitude.\n"
        + "←/→ seek (moves a selected marker / region edge / loop edge; 1 sample when dots are shown, Shift ×3). Ctrl+←/→ previous/next marker / region edge / sample-loop edge. Numpad jumps to a number (or a view position). Z / . centers (toggles center-lock while playing, clears it when stopped). 0–9 jump in the view. L (or G) loops from 3 seconds before the end of the selection (or the sample loop / -L). Shift+L sets the selection as the sample loop (same range again clears it). Shift+R sets the selection as a region (repeat on the same range to split 2, 3, … ways; right-click / Delete clears). Right-click a marker / region flag / loop bar to delete. S sample rate, B bit depth, C channels, M / Ins marker (selection places both ends; repeat for center then 3, 4, … equal parts; same frame is rejected). Click a flag to select an edge / Shift+click for a range / Ctrl+click to add / drag or ←/→ to move (Shift ×3) / Delete or Ctrl+Del to delete. Ctrl+Shift+R to rename. Double-click a comment / region name (-A lime / -L blue / -E red / -R gray).\n"
        + "On a marker / region edge / loop edge, Alt+←/→ is 1 px (1 sample when dots are shown), Shift ×3, Ctrl pairs with the previous marker (both edges for a region / loop). X applies a linear fade around the playhead in the view (before = out / after = in). V opens volume (dB; ↑↓ / wheel, Space preview, Enter apply; shows whole-file LKFS / RMS / Peak first). Ctrl+X / C / V cut / copy / paste (markers in range included; a matching region is included too). T edits the current time. U opens edit history. A cycles waveform / spectrogram / overlay / loudness analysis (Short Term LKFS; the white width is a translucent ±1 LU band; the waveform is an upper-half peak dBFS line on the same scale; the curve is primary cyan / orange / red; LKFS numbers sit on the left scale). Spectrogram, overlay, and loudness skip -A/-L/-E/-R, loop, and region fills. Ctrl+Shift+E exports Wave-only to Wwise. Ctrl+Shift+M saves as MP3. Ctrl+Shift+Alt+M exports every tab as MP3. MP3 cannot take regions / loops; markers are allowed but not saved to MP3.");
    public static string TipAlwaysOnTop => Get(
        "ウィンドウを常に最前面へ表示します。",
        "Keep the window always on top.");
    public static string TipGitHub => Get(
        "GitHub リポジトリを開きます。",
        "Open the GitHub repository.");
    public static string TipTimecode => Get(
        "現在時間 (T)。入力、ホイール／↑↓で調整（時間は1秒／Shift10秒／Ctrl1分／Ctrl+Shift10分、サンプルは1／Shift10／Ctrl100／Ctrl+Shift1000）。Enter で移動。右クリックで時間／サンプル数",
        "Current time (T). Type, or wheel / ↑↓ (time: 1 s / Shift 10 s / Ctrl 1 min / Ctrl+Shift 10 min; samples: 1 / Shift 10 / Ctrl 100 / Ctrl+Shift 1000). Enter jumps. Right-click switches time / samples");
    public static string TipSelectionStartTime => Get(
        "選択開始。入力、ホイール／↑↓で調整（時間は1秒／Shift10秒／Ctrl1分／Ctrl+Shift10分、サンプルは1／Shift10／Ctrl100／Ctrl+Shift1000）。Enter で反映。右クリックで時間／サンプル数",
        "Selection start. Type, or wheel / ↑↓ (time: 1 s / Shift 10 s / Ctrl 1 min / Ctrl+Shift 10 min; samples: 1 / Shift 10 / Ctrl 100 / Ctrl+Shift 1000). Enter applies. Right-click switches time / samples");
    public static string TipSelectionLengthTime => Get(
        "選択範囲の長さ。入力、ホイール／↑↓で調整（時間は1秒／Shift10秒／Ctrl1分／Ctrl+Shift10分、サンプルは1／Shift10／Ctrl100／Ctrl+Shift1000）。Enter で反映。右クリックで時間／サンプル数",
        "Selection length. Type, or wheel / ↑↓ (time: 1 s / Shift 10 s / Ctrl 1 min / Ctrl+Shift 10 min; samples: 1 / Shift 10 / Ctrl 100 / Ctrl+Shift 1000). Enter applies. Right-click switches time / samples");
    public static string TipSelectionEndTime => Get(
        "選択終了。入力、ホイール／↑↓で調整（時間は1秒／Shift10秒／Ctrl1分／Ctrl+Shift10分、サンプルは1／Shift10／Ctrl100／Ctrl+Shift1000）。Enter で反映。右クリックで時間／サンプル数",
        "Selection end. Type, or wheel / ↑↓ (time: 1 s / Shift 10 s / Ctrl 1 min / Ctrl+Shift 10 min; samples: 1 / Shift 10 / Ctrl 100 / Ctrl+Shift 1000). Enter applies. Right-click switches time / samples");
    public static string TipTotalTime => Get(
        "トータル時間。右クリックで時間／サンプル数を切り替えます。",
        "Total time. Right-click to switch time / samples.");
    public static string LabelStatusNowPos => "NOW\nPOS";
    public static string LabelStatusSelStart => "SEL\nST";
    public static string LabelStatusSelWidth => "SEL\nWID";
    public static string LabelStatusSelEnd => "SEL\nEND";
    public static string LabelStatusEndPos => "END\nPOS";
    public static string MenuShowTime => Get("時間", "Time");
    public static string MenuShowSamples => Get("サンプル数", "Samples");
    public static string MenuCopy => Get("コピー", "Copy");
    public static string MenuPaste => Get("貼り付け", "Paste");
    public static string MenuClearSampleLoop => Get("ループを削除", "Clear loop");
    public static string MenuClearRegion => Get("リージョンを削除", "Clear region");
    public static string MenuClearMarker => Get("マーカーを削除", "Clear marker");
    public static string MenuClearMarkers => Get("選択したマーカーを削除", "Clear selected markers");

    public static string TabMenuCloseOthers => Get("このタブ以外を閉じる(_O)", "Close _Other Tabs");
    public static string TabMenuCloseRight => Get("このタブを含め右側を全部閉じる(_R)", "Close This and Tabs to the _Right");
    public static string TabMenuCloseLeft => Get("このタブを含め左側を全部閉じる(_L)", "Close This and Tabs to the _Left");
    public static string TabMenuSelectAll => Get("全部のタブを選択する(_A)", "Select _All Tabs");
    public static string TabMenuCloseAll => Get("すべてのタブを閉じる(_A)", "Close _All Tabs");

    /// <summary>通常メニュー用。「全部のタブを選択する(_A)」とアクセスキーが重ならないよう W。</summary>
    public static string TabMenuCloseAllNormal => Get("すべてのタブを閉じる(_W)", "Close All Tabs (_W)");
    public static string TabMenuPasteToAll => Get("編集データを全てにペーストする(_V)", "Paste Copied Edits to All Tabs (_V)");
    public static string TabMenuCloseSelected => Get("選択したタブを閉じる(_A)", "Close Selected Tabs (_A)");
    public static string TabMenuPasteToSelected => Get("選択したタブにペーストする(_V)", "Paste Copied Edits to Selected Tabs (_V)");
    public static string TabMenuExportWave => Get("Wave で書き出す(_E)", "Export _Wave");
    public static string TabMenuExportMp3 => Get("MP3 で書き出す(_M)", "Export _MP3");
    public static string TabMenuExportWaveSelected => Get("選択したタブを Wave で書き出す(_E)", "Export Selected Tabs as _Wave");
    public static string TabMenuExportMp3Selected => Get("選択したタブを MP3 で書き出す(_M)", "Export Selected Tabs as _MP3");
    public static string TabMenuExportWaveAll => Get("すべてのタブを Wave で書き出す(_E)", "Export All Tabs as _Wave");
    public static string TabMenuExportMp3All => Get("すべてのタブを MP3 で書き出す(_M)", "Export All Tabs as _MP3");
    public static string TabMenuExportWaveByMarkers => Get(
        "マーカーでセパレートして書き出す(_K)",
        "Export Wave Separated by Mar_kers");
    public static string TabMenuExportWaveByMarkersSelected => Get(
        "選択したタブをマーカーでセパレートして書き出す(_K)",
        "Export Selected Tabs Separated by Mar_kers");
    public static string TabMenuExportWaveByMarkersAll => Get(
        "すべてのタブをマーカーでセパレートして書き出す(_K)",
        "Export All Tabs Separated by Mar_kers");
    public static string TabMenuExportWaveByRegions => Get(
        "リージョンでセパレートして書き出す(_G)",
        "Export Wave Separated by Re_gions");
    public static string TabMenuExportWaveByRegionsSelected => Get(
        "選択したタブをリージョンでセパレートして書き出す(_G)",
        "Export Selected Tabs Separated by Re_gions");
    public static string TabMenuExportWaveByRegionsAll => Get(
        "すべてのタブをリージョンでセパレートして書き出す(_G)",
        "Export All Tabs Separated by Re_gions");

    public static string EditHistoryTitle => Get("編集履歴", "Edit history");
    public static string EditHistoryOrigin => Get("初期状態", "Original");
    public static string EditHistoryHint => Get(
        "↑↓ 移動　Enter 確定　Esc キャンセル\nCtrl+クリック／Shift+↑↓ 選択　Ctrl+C コピー　Ctrl+V 別ファイルへ適用",
        "↑↓ move   Enter apply   Esc cancel\nCtrl+click / Shift+↑↓ select   Ctrl+C copy   Ctrl+V apply to file");

    public static string EditHistoryNotCopyable => Get(
        "この操作は別ファイルへ適用できません",
        "This step cannot be applied to another file");

    public static string EditHistoryCopyEmpty => Get(
        "コピーできる操作がありません",
        "Nothing to copy");

    public static string EditHistoryPasteEmpty => Get(
        "コピーされた履歴がありません",
        "No copied history");

    public static string EditHistoryCopied(int count) => Get(
        $"{count} 件コピーしました（別ファイルで Ctrl+V。履歴を開かなくても可）",
        $"Copied {count} step(s). Press Ctrl+V in another file (no need to open its history)");

    public static string EditHistoryPasteSkippedAll => Get(
        "コピーした履歴はこのファイルに適用できませんでした",
        "None of the copied history steps could be applied to this file");

    public static string EditHistoryPasted(int applied, int total) => applied == total
        ? Get($"{applied} 件適用しました", $"Applied {applied} step(s)")
        : Get(
            $"{applied} / {total} 件適用しました（適用できないものはスキップ）",
            $"Applied {applied} of {total} step(s); inapplicable ones skipped");

    public static string EditHistoryName(string name) => name switch
    {
        _ when name == EditHistoryOrigin || name == "初期状態" || name == "Original" => EditHistoryOrigin,
        "Fade In" => Get("フェードイン", "Fade In"),
        "Fade Out" => Get("フェードアウト", "Fade Out"),
        "Fade Around Playhead" => Get("再生ヘッド前後フェード", "Fade Around Playhead"),
        "Normalize" => Get("ノーマライズ", "Normalize"),
        "Volume" => Get("音量", "Volume"),
        "Delete" => Get("削除", "Delete"),
        "Paste" => Get("ペースト", "Paste"),
        "Add Marker" => Get("マーカー追加", "Add Marker"),
        "Marker Comment" => Get("マーカーコメント", "Marker Comment"),
        "Region Name" => Get("リージョン名", "Region Name"),
        "Delete Markers" => Get("マーカー削除", "Delete Markers"),
        "Move Marker" => Get("マーカー移動", "Move Marker"),
        "Move Markers" => Get("マーカー移動", "Move Markers"),
        "Set Sample Loop" => Get("サンプルループ", "Set Sample Loop"),
        "Set Region" => Get("リージョン", "Set Region"),
        "Move Timeline" => Get("移動", "Move Timeline"),
        "Convert Sample Rate" => Get("サンプリングレート", "Convert Sample Rate"),
        "Convert Bit Depth" => Get("ビット深度", "Convert Bit Depth"),
        "Convert Channels" => Get("チャンネル数", "Convert Channels"),
        _ => name,
    };

    public static string LabelFadeCurveShort(int shapeId) => shapeId switch
    {
        0 => Get("対数3", "Log 3"),
        1 => Get("Sine", "Sine"),
        2 => Get("対数1.41", "Log 1.41"),
        3 => Get("逆S字", "Inv S"),
        4 => Get("直線", "Linear"),
        5 => Get("一定", "Constant"),
        6 => Get("S字", "S-curve"),
        7 => Get("指数1.41", "Exp 1.41"),
        8 => Get("Sine", "Sine"),
        9 => Get("指数3", "Exp 3"),
        _ => Get("S字", "S-curve"),
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
            return Get("（空）", "(empty)");
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

        if (removed.Length == added.Length && removed.Length > 0)
        {
            Array.Sort(removed, static (left, right) => left.Frame.CompareTo(right.Frame));
            Array.Sort(added, static (left, right) => left.Frame.CompareTo(right.Frame));
            if (removed.Length <= 3)
            {
                var pairs = new string[removed.Length];
                for (var i = 0; i < removed.Length; i++)
                {
                    pairs[i] = FormatShift(removed[i].Frame, added[i].Frame, sampleRate);
                }

                var text = $"{verb}  {string.Join(", ", pairs)}";
                if (removed.Length == 1 && removed[0].Comment.Length > 0)
                {
                    return $"{text}  {EditHistoryQuote(removed[0].Comment)}";
                }

                return text;
            }

            return $"{verb}  {CountLabel(removed.Length)}";
        }

        if (removed.Length > 0)
        {
            return $"{verb}  {CountLabel(removed.Length)}";
        }

        return verb;
    }

    public static string EditHistoryTimelineMove(
        IReadOnlyList<MarkerSnapshot> markersBefore,
        IReadOnlyList<MarkerSnapshot> markersAfter,
        IReadOnlyList<WaveRegion> regionsBefore,
        IReadOnlyList<WaveRegion> regionsAfter,
        WaveSelection loopBefore,
        WaveSelection loopAfter,
        int sampleRate)
    {
        var parts = new List<string>(3);
        if (!markersBefore.Select(marker => marker.Frame).SequenceEqual(markersAfter.Select(marker => marker.Frame)))
        {
            var name = markersBefore.Count == 1 && markersAfter.Count == 1 ? "Move Marker" : "Move Markers";
            parts.Add(EditHistoryMarkers(name, markersBefore, markersAfter, sampleRate));
        }

        if (TryFormatLoopMove(loopBefore, loopAfter, sampleRate, out var loop))
        {
            parts.Add(loop);
        }

        if (TryFormatRegionMoves(
            regionsBefore.Select(region => region.Range).ToArray(),
            regionsAfter.Select(region => region.Range).ToArray(),
            sampleRate,
            out var regions))
        {
            parts.Add(regions);
        }

        return parts.Count == 0 ? EditHistoryName("Move Timeline") : string.Join("  ", parts);
    }

    private static bool TryFormatLoopMove(
        WaveSelection before,
        WaveSelection after,
        int sampleRate,
        out string text)
    {
        text = string.Empty;
        if (before == after)
        {
            return false;
        }

        if (before.IsEmpty || after.IsEmpty)
        {
            text = after.IsEmpty
                ? EditHistoryName("Set Sample Loop") + Get("  解除", "  cleared")
                : EditHistoryRange(EditHistoryName("Set Sample Loop"), sampleRate, after.StartFrame, after.EndFrame);
            return true;
        }

        var startChanged = before.StartFrame != after.StartFrame;
        var endChanged = before.EndFrame != after.EndFrame;
        if (startChanged && endChanged)
        {
            text = Get("ループ移動  ", "Loop move  ") + $"{FormatRange(before, sampleRate)}→{FormatRange(after, sampleRate)}";
            return true;
        }

        text = startChanged
            ? Get("ループ開始  ", "Loop start  ") + FormatShift(before.StartFrame, after.StartFrame, sampleRate)
            : Get("ループ終了  ", "Loop end  ") + FormatShift(before.EndFrame, after.EndFrame, sampleRate);
        return true;
    }

    private static bool TryFormatRegionMoves(
        IReadOnlyList<WaveSelection> before,
        IReadOnlyList<WaveSelection> after,
        int sampleRate,
        out string text)
    {
        text = string.Empty;
        var gone = before.Where(range => !after.Contains(range)).ToArray();
        var come = after.Where(range => !before.Contains(range)).ToArray();
        if (gone.Length == 0 && come.Length == 0)
        {
            return false;
        }

        if (gone.Length == 1 && come.Length == 1)
        {
            text = FormatOneRegionMove(gone[0], come[0], sampleRate);
            return true;
        }

        if (gone.Length == come.Length && gone.Length is > 0 and <= 2)
        {
            Array.Sort(gone, CompareRanges);
            Array.Sort(come, CompareRanges);
            var pairs = new string[gone.Length];
            for (var i = 0; i < gone.Length; i++)
            {
                pairs[i] = $"{FormatRange(gone[i], sampleRate)}→{FormatRange(come[i], sampleRate)}";
            }

            text = Get("リージョン移動  ", "Region move  ") + string.Join(", ", pairs);
            return true;
        }

        text = Get("リージョン移動  ", "Region move  ") + CountLabel(Math.Max(gone.Length, come.Length));
        return true;
    }

    private static string FormatOneRegionMove(WaveSelection before, WaveSelection after, int sampleRate)
    {
        var startChanged = before.StartFrame != after.StartFrame;
        var endChanged = before.EndFrame != after.EndFrame;
        if (startChanged && !endChanged)
        {
            return Get("リージョン開始  ", "Region start  ") + FormatShift(before.StartFrame, after.StartFrame, sampleRate);
        }

        if (endChanged && !startChanged)
        {
            return Get("リージョン終了  ", "Region end  ") + FormatShift(before.EndFrame, after.EndFrame, sampleRate);
        }

        return Get("リージョン移動  ", "Region move  ") + $"{FormatRange(before, sampleRate)}→{FormatRange(after, sampleRate)}";
    }

    private static int CompareRanges(WaveSelection left, WaveSelection right)
    {
        var byStart = left.StartFrame.CompareTo(right.StartFrame);
        return byStart != 0 ? byStart : left.EndFrame.CompareTo(right.EndFrame);
    }

    private static string FormatRange(WaveSelection range, int sampleRate) =>
        $"{FormatTimecode(range.StartFrame, sampleRate)}–{FormatTimecode(range.EndFrame, sampleRate)}";

    private static string FormatShift(long fromFrame, long toFrame, int sampleRate) =>
        $"{FormatTimecode(fromFrame, sampleRate)}→{FormatTimecode(toFrame, sampleRate)}";

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

        return $"{verb}  {CountLabel(markers.Count)}";
    }

    private static string CountLabel(int count) => Format("{0}個", "{0}", count);

    public static string FormatSampleRate(int hertz)
    {
        var kilo = hertz / 1000d;
        var rounded = Math.Round(kilo, 3);
        return Math.Abs(rounded - Math.Round(rounded)) < 1e-6
            ? $"{(int)Math.Round(rounded)}kHz"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{rounded:0.###}kHz");
    }

    public static string FormatBitDepth(int bits) => $"{Math.Max(0, bits)}bit";

    public static string FormatChannels(int channels) => $"{Math.Max(0, channels)}ch";

    public static string FormatFileBytes(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        var mega = bytes / 1_000_000d;
        var mebi = bytes / (1024d * 1024d);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{mega:0.00} MB ({mebi:0.00} MiB / {bytes:N0} B)");
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

    public static string FormatStatusTime(long frame, int sampleRate, bool asSamples)
    {
        frame = Math.Max(0, frame);
        if (asSamples)
        {
            return frame.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return FormatTimecode(frame, sampleRate);
    }

    public static bool TryParseSampleCount(string? text, out long samples)
    {
        samples = 0;
        var compact = (text ?? string.Empty).Trim().Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Length == 0)
        {
            return false;
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (long.TryParse(compact, System.Globalization.NumberStyles.Integer, culture, out samples)
            && samples >= 0)
        {
            return true;
        }

        var parts = compact.Split(',');
        if (parts.Length < 2
            || parts[0].Length is < 1 or > 3
            || !parts[0].All(char.IsDigit))
        {
            return false;
        }

        for (var i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length != 3 || !parts[i].All(char.IsDigit))
            {
                return false;
            }
        }

        return long.TryParse(string.Concat(parts), System.Globalization.NumberStyles.Integer, culture, out samples)
            && samples >= 0;
    }

    public static bool TryParseStatusTime(string? text, int sampleRate, bool preferSamples, out long frame)
    {
        frame = 0;
        var rate = Math.Max(1, sampleRate);
        if (preferSamples)
        {
            if (TryParseSampleCount(text, out frame))
            {
                return true;
            }

            if (TryParseDuration(text, out var sampleSeconds))
            {
                frame = (long)Math.Round(sampleSeconds * rate);
                return frame >= 0;
            }

            return false;
        }

        if (TryParseDuration(text, out var seconds))
        {
            frame = (long)Math.Round(seconds * rate);
            return frame >= 0;
        }

        return TryParseSampleCount(text, out frame);
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
