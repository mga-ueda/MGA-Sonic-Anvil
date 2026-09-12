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
    public static string LabelAudioDevice => Get("オーディオデバイス", "Audio device");
    public static string LabelSettingsTabGeneral => Get("一般", "General");
    public static string LabelSettingsTabAudio => Get("オーディオ", "Audio");
    public static string LabelSettingsTabLayouts => Get("表示項目", "Shown");
    public static string LabelSettingsTabEditing => Get("編集", "Editing");
    public static string LabelSettingsTabExport => Get("書き出し", "Export");
    public static string LabelSettingsInput => Get("録音", "Recording");
    public static string LabelSettingsOutput => Get("再生", "Playback");
    public static string LabelSpeaker => Get("スピーカー", "Speakers");
    public static string LabelSpeakerVisibility => Get("有効にするスピーカー定義", "Speaker definitions to enable");
    public static string ButtonSineMinusTwenty => Get("Sine −20 dB", "Sine −20 dB");
    public static string ButtonChannelVoice => Get("Voice", "Voice");
    public static string LabelPortOff => Get("なし", "Off");
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
    public static string LabelUiTheme => Get("配色", "Theme");
    public static string LabelThemeAuto => Get("Auto", "Auto");
    public static string LabelThemeDark => Get("Dark", "Dark");
    public static string LabelThemeLight => Get("Light", "Light");
    public static string LabelFileAssociations => Get("関連付け", "File associations");
    public static string LabelLanguageAuto => Get("Auto", "Auto");
    public static string LabelLanguageJapanese => Get("Japanese", "Japanese");
    public static string LabelLanguageEnglish => Get("English", "English");
    public static string LabelFadeCurveDefaults => Get("フェードカーブ既定", "Default Fade Curves");
    public static string LabelDefaultFadeIn => Get("波形フェードイン", "Waveform Fade In");
    public static string LabelDefaultFadeOut => Get("波形フェードアウト", "Waveform Fade Out");
    public static string AccessibleAudioSettingsButton => Get("設定", "Settings");
    public static string TipAudioSettings => Get(
        "設定 (Ctrl+Shift+O)\n一般／表示項目／オーディオ／編集／書き出しのタブ。表示言語、配色（ダーク／ライト／Auto）、関連付け、スピーカー配置（モノラル〜Atmos。デバイスとポート割り当て）、有効にするスピーカー定義、確認用メーター／Sine −20 dB／Voice、ラウドネス、フェード、MP3、同時書き出し本数。",
        "Settings (Ctrl+Shift+O)\nGeneral / Shown / Audio / Editing / Export tabs. Language, theme (Dark / Light / Auto), file associations, speaker layouts (mono through Atmos; device and port assignments), which speaker definitions are enabled, meters, per-port Sine −20 dB, and English channel-name Voice, loudness, fades, MP3, and parallel export count.");
    public static string LabelMp3Encode => Get("MP3", "MP3");
    public static string TipMp3Encode => Get(
        "MP3 保存の経路です。LAME のパスが有効なら lame.exe、空欄または無効なら Windows です。",
        "MP3 save path. A valid LAME path uses lame.exe; empty or invalid uses Windows.");
    public static string LabelWindowsMp3BitRate => Get(
        "Windows ビットレート（LAME を使わないとき）",
        "Windows Bit Rate (when not using LAME)");
    public static string LabelKbps => Get("kbps", "kbps");
    public static string LabelLamePath => Get("LAME のパス", "LAME Path");
    public static string LabelLameOptions => Get("LAME オプション", "LAME Options");
    public static string LabelExportParallel => Get("同時書き出し本数", "Parallel export count");
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
    public static string LabelPeak => Get("Peak", "Peak");
    public static string LabelRms => Get("RMS", "RMS");
    public static string LabelVolume => Get("音量", "Volume");
    public static string LabelPitch => Get("ピッチ", "Pitch");
    public static string LabelSemitone => Get("半音", "st");
    public static string LabelPitchTimeStretch => Get("長さを保つ", "Keep length");
    public static string TipPitchTimeStretch => Get(
        "オン（既定）は長さを保ちます。オフは再生速度と同じく長さも変わります。Tab で移動。",
        "On (default) keeps the length. Off also changes length, like playback speed. Tab moves here.");
    public static string ErrorPitchShiftFailed => Get(
        "ピッチシフトに失敗しました。",
        "Pitch shift failed.");
    public static string LabelTimeStretch => Get("タイムストレッチ", "Time stretch");
    public static string LabelTimeStretchSource => Get("元の時間", "Original");
    public static string LabelTimeStretchDest => Get("時間", "Time");
    public static string LabelTimeStretchPercent => Get("割合", "Ratio");
    public static string LabelPercent => "％";
    public static string ErrorTimeStretchFailed => Get(
        "タイムストレッチに失敗しました。",
        "Time stretch failed.");
    public static string OverlayTimeStretch => Get("タイムストレッチ", "Time stretch");
    public static string TipTimeStretch => Get(
        "タイムストレッチ (T)\n選択があればその時間、無ければ全体。時間を指定すると割合が、割合を指定すると時間が連動。↑↓／ホイール（時間は1秒／Shift10秒／Ctrl1分／Ctrl+Shift10分。割合は0.1／Shift 1／Ctrl 10／Ctrl+Shift 25％）。Tab で時間と割合。Space で試聴、Enter で実行。10–1000％。",
        "Time stretch (T)\nUses the selection, or the whole file. Editing time updates the ratio; editing the ratio updates time. ↑↓ / wheel (time: 1 s / Shift 10 s / Ctrl 1 min / Ctrl+Shift 10 min; ratio: 0.1 / Shift 1 / Ctrl 10 / Ctrl+Shift 25%). Tab switches fields. Space previews, Enter applies. Range is 10–1000%.");
    public static string TipTimeStretchTime => Get(
        "伸ばしたあとの時間。↑↓／ホイールで調整（1秒／Shift10秒／Ctrl1分／Ctrl+Shift10分。サンプル表示時は1／10／100／1000）。",
        "Duration after stretch. ↑↓ / wheel (1 s / Shift 10 s / Ctrl 1 min / Ctrl+Shift 10 min; samples: 1 / 10 / 100 / 1000).");
    public static string TipTimeStretchPercent => Get(
        "元の時間に対する割合。↑↓／ホイールで 0.1％（Shift 1／Ctrl 10／Ctrl+Shift 25）。",
        "Ratio versus the original duration. ↑↓ / wheel by 0.1% (Shift 1 / Ctrl 10 / Ctrl+Shift 25).");

    public static string FormatTimeStretchExtra(int sampleRate, int sourceFrames, int destFrames)
    {
        var percent = TimeStretchPercentOf(sourceFrames, destFrames)
            .ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        return FormatStatusTime(sourceFrames, sampleRate, asSamples: false)
            + "→"
            + FormatStatusTime(destFrames, sampleRate, asSamples: false)
            + "  "
            + percent
            + "%";
    }

    /// <summary>タイムストレッチ割合の表示。Audio.TimeStretch.PercentOf と同じ計算。</summary>
    private static double TimeStretchPercentOf(int sourceFrames, int destFrames)
    {
        sourceFrames = Math.Max(0, sourceFrames);
        destFrames = Math.Max(0, destFrames);
        if (sourceFrames == 0)
        {
            return 100;
        }

        return Math.Clamp(
            Math.Round(destFrames * 100d / sourceFrames, 1, MidpointRounding.AwayFromZero),
            10,
            1000);
    }

    public static string FormatSignedDb(double gainDb)
    {
        var value = Math.Clamp(Math.Round(gainDb, 1, MidpointRounding.AwayFromZero), -60, 60);
        return value.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture)
            + " "
            + LabelDb;
    }

    public static string FormatSignedSemitones(int semitones)
    {
        var value = Math.Clamp(semitones, -24, 24);
        return value.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture)
            + " "
            + LabelSemitone;
    }

    public static string FormatPitchShiftExtra(int semitones, bool timeStretch)
    {
        var pitch = FormatSignedSemitones(semitones);
        return timeStretch
            ? pitch
            : pitch + Get("（ストレッチなし）", " (no stretch)");
    }

    public static string MenuOpen => Get("開く", "Open");
    public static string MenuSaveAs => Get("名前を付けて保存", "Save As");

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

    public static string ConfirmSpeakerSettingsSave(string name) => Format(
        "{0} の設定を保存しますか？",
        "Save settings for {0}?",
        name);

    public static string ConfirmSaveFor(string name) => Format(
        "{0} に未保存の変更があります。保存しますか？",
        "{0} has unsaved changes. Save them?",
        name);
    public static string ConfirmSaveBatchHint(int remaining) => Format(
        "未保存のタブが {0} 件あります。まとめて終了することもできます。",
        "{0} tab(s) have unsaved changes. You can finish them all at once.",
        remaining);

    public static string LabelWaveformLoading => Get("Now Loading ...", "Now Loading ...");
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
    public static string OverlayPitchShift => Get("ピッチシフト", "Pitch shift");
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

    public static string LabelTransportGroup => Get("TRANS", "TRANS");
    public static string LabelNavigationGroup => Get("NAV", "NAV");
    public static string LabelTimeZoomGroup => Get("TIME", "TIME");
    public static string LabelAmpZoomGroup => Get("AMP", "AMP");
    public static string LabelWaveformHeightGroup => Get("SIZE", "SIZE");
    public static string LabelEditGroup => Get("EDIT", "EDIT");
    public static string LabelMarkerGroup => Get("MARK", "MARK");
    public static string LabelFileGroup => Get("FILE", "FILE");
    public static string LabelViewGroup => Get("VIEW", "VIEW");
    public static string LabelHelpGroup => Get("HELP", "HELP");

    public static string TooltipRecord => Get("録音 (Ctrl+R)", "Record (Ctrl+R)");
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
    public static string TooltipSettings => Get("設定 (Ctrl+Shift+O)", "Settings (Ctrl+Shift+O)");
    public static string TooltipManualHelp => Get("マニュアル", "Manual");
    public static string TooltipJumpToTime => Get("時間へ移動 (G)", "Focus time (G)");
    public static string TooltipPreviousPage => Get("表示の約 5% 戻る (PageUp)", "Back about 5% of the view (PageUp)");
    public static string TooltipNextPage => Get("表示の約 5% 進む (PageDown)", "Forward about 5% of the view (PageDown)");
    public static string TooltipPreviousMarker => Get("前のマーカー (Ctrl+←)", "Previous marker (Ctrl+←)");
    public static string TooltipNextMarker => Get("次のマーカー (Ctrl+→)", "Next marker (Ctrl+→)");
    public static string TooltipWaveformHeight => Get("波形の高さ 1×／2×／3× (H)", "Waveform height 1× / 2× / 3× (H)");
    public static string TooltipUndo => Get("元に戻す (Ctrl+Z)", "Undo (Ctrl+Z)");
    public static string TooltipRedo => Get("やり直し (Ctrl+Y)", "Redo (Ctrl+Y)");
    public static string TooltipFadeAround => Get("再生位置でフェード (X)", "Fade around playhead (X)");
    public static string TooltipVolume => Get("音量 (V)", "Volume (V)");
    public static string TooltipPitch => Get("ピッチ (P)", "Pitch (P)");
    public static string TooltipTimeStretch => Get("タイムストレッチ (T)", "Time stretch (T)");
    public static string TooltipReverse => Get("リバース (R)", "Reverse (R)");
    public static string TooltipAddMarker => Get("マーカーを追加 (M)", "Add marker (M)");
    public static string TooltipSetLoop => Get("選択をループに (Shift+L)", "Set selection as loop (Shift+L)");
    public static string TooltipSetRegion => Get("選択をリージョンに (Shift+R)", "Set selection as region (Shift+R)");
    public static string TooltipOpen => Get("開く (Ctrl+O)", "Open (Ctrl+O)");
    public static string TooltipSaveAs => Get("名前を付けて保存 (Ctrl+Shift+S)", "Save As (Ctrl+Shift+S)");
    public static string TooltipAnalysisView => Get("スペクトログラム / 重ね (A)  解除 (Shift+A)", "Spectrogram / overlay (A)  leave (Shift+A)");
    public static string TooltipSpectrogramView => Get("スペクトログラム / 重ね (A)  解除 (Shift+A)", "Spectrogram / overlay (A)  leave (Shift+A)");
    public static string TooltipLoudnessView => Get("ラウドネス (V)  解除 (Shift+V)", "Loudness (V)  leave (Shift+V)");
    public static string TooltipCenterPlayhead => Get("中央寄せ / センターロック (Z)", "Center / center-lock (Z)");
    public static string TooltipHistory => Get("編集履歴 (U)", "Edit history (U)");
    public static string TooltipUiThemeToggle => Get("ダーク / ライト", "Dark / Light");

    public static string TipJumpToTime => Get("時間へ移動 (G)", "Focus time (G)");
    public static string TipPreviousPage => Get("表示の約 5% 戻る (PageUp)", "Back about 5% of the view (PageUp)");
    public static string TipNextPage => Get("表示の約 5% 進む (PageDown)", "Forward about 5% of the view (PageDown)");
    public static string TipPreviousMarker => Get("前のマーカー (Ctrl+←)", "Previous marker (Ctrl+←)");
    public static string TipNextMarker => Get("次のマーカー (Ctrl+→)", "Next marker (Ctrl+→)");
    public static string TipWaveformHeight => Get(
        "波形の高さ (H)\n1× → 2× → 3× と循環",
        "Waveform height (H)\nCycles 1× → 2× → 3×");
    public static string TipUndo => Get("元に戻す (Ctrl+Z)", "Undo (Ctrl+Z)");
    public static string TipRedo => Get("やり直し (Ctrl+Y)", "Redo (Ctrl+Y)");
    public static string TipFadeAround => Get(
        "再生位置でフェード (X)\n表示範囲をシーク前後にリニアフェード（前=アウト / 後=イン）",
        "Fade around playhead (X)\nLinear fade in the view around the playhead (before = out / after = in)");
    public static string TipReverse => Get("リバース (R)\n選択範囲を時間方向に反転。未選択なら全体", "Reverse (R)\nReverse the selection in time. Uses the whole file if nothing is selected");
    public static string TipAddMarker => Get("マーカーを追加 (M / Ins)\n選択中は両端。同じ範囲で繰り返すと分割", "Add marker (M / Ins)\nPlaces both ends of a selection; repeat to split");
    public static string TipSetLoop => Get("選択をサンプルループに (Shift+L)\n同じ範囲でもう一度で解除", "Set selection as sample loop (Shift+L)\nSame range again clears it");
    public static string TipSetRegion => Get("選択をリージョンに (Shift+R)\n同じ範囲で繰り返すと分割", "Set selection as region (Shift+R)\nRepeat on the same range to split");
    public static string TipSaveAs => Get("名前を付けて保存 (Ctrl+Shift+S)", "Save As (Ctrl+Shift+S)");
    public static string TipAnalysisView => Get(
        "スペクトログラム / 重ね (A)\nスペクトログラム → 波形上乗せ。抜けるのは Shift+A。ボタンは 3 回で波形に戻る。暗部持ち上げは左端のバーまたは Alt+↑／↓",
        "Spectrogram / overlay (A)\nSpectrogram → waveform overlay. Shift+A returns to the waveform. The button returns to the waveform on the third click. Lift dark energy with the left bar or Alt+↑ / ↓");
    public static string TipSpectrogramView => Get(
        "スペクトログラム / 重ね (A)\nスペクトログラム → 波形上乗せ。抜けるのは Shift+A。ボタンは 3 回で波形に戻る。暗部持ち上げは左端のバーまたは Alt+↑／↓",
        "Spectrogram / overlay (A)\nSpectrogram → waveform overlay. Shift+A returns to the waveform. The button returns to the waveform on the third click. Lift dark energy with the left bar or Alt+↑ / ↓");
    public static string TipLoudnessView => Get(
        "ラウドネス表示 (V)\nV で曲線と音量入力を開く。抜けるのは Shift+V。ボタンは表示のオン／オフ",
        "Loudness view (V)\nV shows the curve and opens volume input. Shift+V returns to the waveform. The button toggles the view only");
    public static string TipCenterPlayhead => Get(
        "中央寄せ (Z / .)\n再生中はセンターロックの切替",
        "Center (Z / .)\nToggles center-lock while playing");
    public static string TipUiThemeToggle => Get(
        "ダークとライトを切り替えます。設定の配色は明示的な Dark / Light になります（Auto は外れます）。",
        "Switch Dark and Light. Settings theme becomes an explicit Dark / Light (Auto is cleared).");

    public static string LabelWaveformLane(int number) =>
        Get($"Ch{number}", $"Ch{number}");
    public static string TipSettingsInput => Get(
        "各スピーカーの録音ポートと、書き込む Ch。右のバーで信号が入っているか確認できます。",
        "Record port and file channel (Ch) for each speaker. The bars show whether a signal is arriving.");
    public static string TipSettingsOutput => Get(
        "各スピーカーの再生ポートと、読む Ch。行の Sine −20 dB と Voice で確認できます。LFE に Voice はありません。",
        "Playback port and file channel (Ch) for each speaker. Sine −20 dB and Voice on each row check the route. LFE has no Voice.");
    public static string TipSpeakerPreset => Get(
        "アプリ用意のスピーカー配置。先にこれを選び、その中でデバイスとポート割り当てを決めます。一覧は表示項目タブのチェックで絞れます。変更したあと OK せずに別の配置へ切り替えると、保存するか聞きます。",
        "Built-in speaker layout. Pick this first, then set the device and port assignments inside it. The list is filtered by the Shown tab. Switching layouts without OK asks whether to save the current one.");
    public static string TipSpeakerVisibility => Get(
        "チェックしたスピーカー定義だけ、設定とステータスバー（NOW POS の左）の一覧に出します。既定は Stereo だけです。今使っている定義は、外しても切り替えるまで残ります。全部外すことはできません。",
        "Only enabled speaker definitions appear in Settings and the status-bar menu (left of NOW POS). Stereo is on by default. The definition in use stays listed until you switch away. At least one must stay enabled.");
    public static string TipSpeakerSwitch => Get(
        "使うスピーカー配置を切り替えます。デバイスとポート割り当てが一緒に変わります。一覧は設定の表示項目タブで絞れます。",
        "Switch speaker layout. The device and port assignments change with it. The list is filtered in Settings → Shown.");
    public static string TipRecord => Get(
        "録音 (Ctrl+R)\n新規タブに録音。もう一度で停止。スピーカー配置・録音ポート・Ch の割り当てを使う。Space / Enter / Esc でも停止。",
        "Record (Ctrl+R)\nRecords into a new tab. Press again to stop. Uses the speaker layout, record ports, and Ch map. Space / Enter / Esc also stop.");
    public static string TipRecordInputMap => Get(
        "各スピーカーがどのポートから入り、どの Ch へ書くか。なしは無音。右のバーは今のレベルです。Ch は再生と共通です。",
        "Which port feeds each speaker, and which file channel (Ch) it writes. Off is silence. The bar is the live level. Ch is shared with playback.");
    public static string TipFileChannelMap => Get(
        "このスピーカーがファイルのどの Ch か。録音と再生で同じ割り当てです。なしはそのスピーカーを使いません。初期値は 1 から順です。",
        "Which file channel (Ch) this speaker uses. Shared by record and playback. Off leaves the speaker unused. The default is 1, 2, 3…");
    public static string TipInputLevel => Get(
        "割り当てた録音ポートのピーク。信号が入っているか確認できます。",
        "Peak of the assigned record port. Use it to confirm a signal is arriving.");
    public static string TipSineMinusTwenty => Get(
        "このスピーカーだけに −20 dBFS の正弦波を出します。通常は 1 kHz、LFE は 80 Hz。もう一度押すと停止。",
        "Plays a −20 dBFS sine on this speaker only. 1 kHz normally, 80 Hz for LFE. Press again to stop.");
    public static string TipChannelVoice => Get(
        "このスピーカーのチャンネル名を英語で読み上げ、同じポートへ繰り返し出します。LFE にはありません。もう一度押すと停止。",
        "Speaks this speaker's channel name in English and loops it on the same port. Not used for LFE. Press again to stop.");
    public static string StatusInputMonitorFailed => Get(
        "録音を開けません。デバイスと API を確認してください。",
        "Could not open recording. Check the device and API.");
    public static string TipPlaybackOutputMap => Get(
        "各スピーカーをどのポートへ出し、どの Ch から読むか。Ch は録音と共通。2ch 再生でポート未設定なら、スピーカー分をダウンミックス。",
        "Which port each speaker uses, and which file channel (Ch) it reads. Ch is shared with record. Stereo playback with no port map still downmixes the speakers.");
    public static string TipPlay => Get(
        "再生 / 停止 (Space)\n停止で開始位置へ戻る\nEnter でその場停止\nCtrl+Space 3秒前から\nAlt+Enter 再生開始位置からやり直し",
        "Play / stop (Space)\nStop returns to the start position\nEnter pauses in place\nCtrl+Space from 3 seconds earlier\nAlt+Enter restarts from the playback start");
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
        "音量 (V)\nラウドネス曲線を表示して dB を入力。↑↓／ホイールで 0.1 dB（Shift 1／Ctrl 3／Ctrl+Shift 6）。数字キーで直接入力。Space で試聴、Enter で実行。未選択なら全体。波形全体の Integrated LKFS / RMS / Peak の変化を先に表示。曲線も同じ dB で追従。Shift+V で曲線を閉じる。",
        "Volume (V)\nShows the loudness curve and accepts a dB value. ↑↓ / wheel by 0.1 dB (Shift 1 / Ctrl 3 / Ctrl+Shift 6). Type digits to enter a value. Space previews, Enter applies. Uses the whole file if nothing is selected. Shows how whole-file Integrated LKFS / RMS / Peak will change. The curve follows the same dB. Shift+V closes the curve.");
    public static string TipPitch => Get(
        "ピッチ (P)\n↑↓／ホイールで半音（Shift または Ctrl で1オクターブ）。Space で試聴、Enter で実行。未選択なら全体。±2オクターブ。長さを保つ（既定オン。Tab でチェックへ）。オフは長さも変わる。長さだけなら T。",
        "Pitch (P)\n↑↓ / wheel by a semitone (Shift or Ctrl for an octave). Space previews, Enter applies. Uses the whole file if nothing is selected. Range is ±2 octaves. Keep length (on by default. Tab moves to the checkbox). Off also changes length. T stretches time only.");
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
        "開く (Ctrl+O)\nドロップでも可。複数ファイルはタブで追加。\nWave / AIFF / MP3\nCtrl+W でタブを閉じる（未保存なら保存確認）\nCtrl+Q でアプリを終了（タブと未保存の作業コピーは次回起動時に戻す）\nCtrl+Shift+T で閉じたタブを開き直す（何度でも）\nCtrl+Tab で次のタブ\nタブが増えると先にアクティブ以外を縮める。6文字を切るまで縮めても収まらなければ左右ボタンで送る",
        "Open (Ctrl+O)\nDrop also works. Multiple files open as extra tabs.\nWave / AIFF / MP3\nCtrl+W closes the tab (asks to save if dirty)\nCtrl+Q quits (open tabs and unsaved working copies come back on the next launch)\nCtrl+Shift+T reopens closed tabs (more than one)\nCtrl+Tab goes to the next tab\nExtra tabs shrink (inactive first) before scroll arrows. Arrows appear only if a title would fall below 6 characters");
    public static string TipCloseTab => Get(
        "タブを閉じる (Ctrl+W)。Ctrl+Shift+T で開き直せる",
        "Close tab (Ctrl+W). Ctrl+Shift+T reopens it");
    public static string TipTabScrollLeft => Get("左のタブを表示", "Show tabs to the left");
    public static string TipTabScrollRight => Get("右のタブを表示", "Show tabs to the right");
    public static string TipOverview => Get(
        "波形全体。明るい部分が表示中の範囲。ドラッグで移動（中央をスクラブ）　ホイールで拡縮（再生ヘッド基準）",
        "Whole file. The bright area is the current view. Drag to move (scrubs the center). Wheel zooms around the playhead");
    public static string TipSpectrum => Get(
        "再生中の全チャンネルを畳んだ LED スペクトラムです。1/3oct 相当の帯域とピークホールド。Layer Music Checker と同じ検波です。",
        "LED spectrum of every playback channel mixed together. Third-octave-style bands and peak hold, same detection as Layer Music Checker.");
    public static string TipLoudness => Get(
        "再生出力のラウドネス（ITU-R BS.1770 / EBU R128）。Short Term・Integrated・Momentary Max、Loudness Range、True Peak。ターゲット LKFS は設定で変更。数値は青＝余裕、橙＝接近、赤＝超過（LKFS はターゲット、True Peak は 0 dBTP、Loudness Range は 20/25 LU）。停止後も最後の値を残し、再生し直すと測り直します。音声は変えません。",
        "Playback loudness (ITU-R BS.1770 / EBU R128): Short Term, Integrated, Momentary Max, Loudness Range, True Peak. Target LKFS is in Settings. Values: blue = headroom, orange = approaching, red = over (LKFS vs target, True Peak vs 0 dBTP, Loudness Range vs 20/25 LU). Holds the last reading after stop; a new play measures again. Does not change the audio.");
    public static string TipVectorScope => Get(
        "再生出力の位相相関とベクターオーディオスコープです。正方形は縦が Mid、横が Side。下の数値は 1/2 の相関（+1 同相 / 0 無相関 / -1 逆相）です。3ch 以上ではサラウンドビューに切り替わり、今のスピーカー配置の位置を使います（ファイルのチャンネル名は当てません）。エネルギーは波形と同じチャンネル色で、大きさはチャンネルのレベル、輪郭の凹凸はそのチャンネルの直近の波形です（先端が今、裾が少し前）。全体の広がりをひとつのダークグレーの細線で囲みます。枠はピークを少し持ってからゆっくり戻ります。停止後は表示がゆっくり消えます。",
        "Phase correlation and a vector audio scope of the playback output. The square is Mid (vertical) and Side (horizontal). The number below is channel 1/2 correlation (+1 in phase / 0 uncorrelated / -1 inverted). Three or more channels switch to a surround view using the active speaker layout (file channel names are not inferred). Energy uses the same channel colors as the waveform. Size is channel level; the outline texture is that channel's recent waveform (the tip is now, the flanks are a few milliseconds earlier). One thin dark-gray outline wraps the overall spread. The outline holds peaks briefly, then falls slowly. After stop, the display slowly fades.");
    public static string TipAudioApi => Get(
        "再生／録音 API（WaveOut / WASAPI / ASIO）",
        "Playback / record API (WaveOut / WASAPI / ASIO)");
    public static string TipAudioDevice => Get(
        "録音と再生に使うデバイス。WaveOut / WASAPI は名前の近い録音側を使います。ASIO は同じドライバです。",
        "Device for record and playback. WaveOut / WASAPI pick a matching capture name. ASIO uses the same driver.");
    public static string TipUiLanguage => Get(
        "表示言語。Auto は OS が日本語なら Japanese、それ以外は English。",
        "UI language. Auto is Japanese if the OS is Japanese, otherwise English.");
    public static string TipUiTheme => Get(
        "配色。Auto は OS のアプリ配色に従います。背景と文字だけ変わり、再生ヘッドやマーカーなどのアクセント色は維持します。",
        "Theme. Auto follows the OS app theme. Only backgrounds and text change; accent colors such as the playhead and markers stay the same.");
    public static string TipFileAssociations => Get(
        "チェックすると、その拡張子をこのアプリで開く（既定）。外すと関連付けを外す。すでにこのアプリが既定ならチェック済み。OK を待たず、今の exe へすぐ書き込みます。",
        "Check to make this app the default for that extension. Uncheck to remove the association. Types already using this app are checked. Writes to this exe immediately, without waiting for OK.");
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
        "再生出力の Peak / RMS。内側 2 本が Peak（上の赤ランプがクリップ）、外側 2 本が RMS。どちらもホールド線が付きます。下の数値は Peak 行／RMS 行。3ch 以上はチャンネルごとの Peak バーに、Peak ホールドと緩やかな RMS ホールドを載せます。バーの色は波形左端のチャンネル名の四角と同じ登場順（Atmos 9.1.6 の 16 色）。2ch 以下はシアングラデです。メーターにチャンネル名は出しません。左端をドラッグすると列を広げられます（既定が最小。バーが最大の太さになるところで止まります。次の起動まで覚えます）。",
        "Playback Peak / RMS. Inner two bars are Peak (red lamps clip), outer two are RMS. Both have hold lines. Numbers below are Peak then RMS. Three or more channels show one Peak bar per channel, with Peak hold and a slower RMS hold. Bar colors match the colored squares on the waveform channel names (16 colors for Atmos 9.1.6). Stereo and mono use the cyan gradient. The meter does not show channel names. Drag the left edge to widen the column (the default is the minimum; it stops when the bars reach full thickness; the width is remembered).");
    public static string TipSpectrogramBoost => Get(
        "小さい音を目立たせます。つまみを上へ動かすほど暗い成分が明るくなります（既定は下端でオフ）。スペクトログラム／重ねでは Alt+↑／↓（押しっぱなしで連続）。",
        "Lift quiet energy. Drag the thumb up to brighten darker bins (default is off at the bottom). In spectrogram / overlay, Alt+↑ / ↓ (hold to repeat).");
    public static string TipTimeScroll => Get(
        "表示範囲を左右に動かします。つまみ中央をドラッグ、またはトラックをクリック。両端をドラッグすると時間拡縮。",
        "Pan the view. Drag the thumb, or click the track. Drag either end to zoom time.");
    public static string TipTimeScrollLeft => Get(
        "表示を左へ動かします。",
        "Pan the view left.");
    public static string TipTimeScrollRight => Get(
        "表示を右へ動かします。",
        "Pan the view right.");
    public static string TipTimeScrollAmpZoomIn => Get(
        "振幅拡大 (Shift+↑)",
        "Zoom in amplitude (Shift+↑)");
    public static string TipTimeScrollAmpZoomOut => Get(
        "振幅縮小 (Shift+↓)",
        "Zoom out amplitude (Shift+↓)");
    public static string TipTimeScrollTimeZoomIn => Get(
        "時間拡大 (↑)",
        "Zoom in time (↑)");
    public static string TipTimeScrollTimeZoomOut => Get(
        "時間縮小 (↓)",
        "Zoom out time (↓)");
    public static string TipCopyright => Get(
        "© MIYABI GAME AUDIO INC. MIT License。",
        "© MIYABI GAME AUDIO INC. MIT License.");
    public static string TipEditHistory => Get(
        "編集履歴 (U)。↑↓ で移動、Enter／X／外側クリックで確定、Esc でキャンセル。履歴エリアのクリックでも閉じる。Ctrl+クリック／Shift+↑↓ で選択、Ctrl+C でコピー、別ファイルで Ctrl+V。セーブせず終了しても、戻せる操作は次回起動時に履歴へ戻す。",
        "Edit history (U). ↑↓ move, Enter / X / click outside apply, Esc cancel. Click the history strip to close. Ctrl+click / Shift+↑↓ select, Ctrl+C copy, Ctrl+V in another file. Replayable edits also come back after a restart without saving.");
    public static string TipHistoryStrip => Get(
        "編集履歴の一覧（収まる分だけ。古いものは切れる）。クリックで開く／閉じる。ここからは選べません。",
        "Edit-history list (as many as fit; older rows clip). Click to open or close. This strip is display-only.");
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
        "任意 Hz。↑↓／ホイールで 1（Shift 10／Ctrl 100／Ctrl+Shift 1000）。Enter で確定。Tab で抜けて 1–9 で項目。範囲 1000–384000。",
        "Custom Hz. ↑↓ / wheel by 1 (Shift 10 / Ctrl 100 / Ctrl+Shift 1000). Enter applies. Tab leaves the box so 1–9 pick a row. Range 1000–384000.");
    public static string TipWaveform => Get(
        "クリックで再生位置。ドラッグで選択。Ctrl+ドラッグでスクラブ（全chを L/R に畳む）。\n"
        + "Esc または Shift なし移動で解除。Shift＋ドラッグ／←→ で伸長。ダブルクリックで区間（マーカー間）。トリプルクリックで全選択。ガイドはマーカー／ループ端に吸着。\n"
        + "左端のチャンネル名：クリックでソロ（再クリックで解除。Ctrl で追加。Shift でミュート）。Tab／Shift+Tab で順にソロ。\n"
        + "ホイール＝時間ズーム（再生ヘッド基準）。Shift+ホイール＝パン。Ctrl+ホイール＝振幅。\n"
        + "フラッグ／ループ端：クリックで選択、ドラッグまたは ←→ で移動。Alt+←→ は微調整。ダブルクリックで名前。右クリックでメニュー。",
        "Click to set the playhead. Drag to select. Ctrl+drag scrubs (every channel downmixed to L/R).\n"
        + "Esc or a move without Shift clears the selection. Shift+drag / ←→ extends it. Double-click a span (between markers). Triple-click selects all. The guide snaps to markers / loop edges.\n"
        + "Channel names on the left: click to solo (again to clear; Ctrl adds; Shift mutes). Tab / Shift+Tab cycle solo.\n"
        + "Wheel = time zoom (around the playhead). Shift+wheel = pan. Ctrl+wheel = amplitude.\n"
        + "Flags / loop edges: click to select, drag or ←→ to move. Alt+←→ nudges. Double-click to name. Right-click for the menu.");
    public static string TipAlwaysOnTop => Get(
        "ウィンドウを常に最前面へ表示します。",
        "Keep the window always on top.");
    public static string TipGitHub => Get(
        "GitHub リポジトリを開きます。",
        "Open the GitHub repository.");
    public static string TipTimecode => Get(
        "現在時間 (G)。入力、ホイール／↑↓で調整（時間は1秒／Shift10秒／Ctrl1分／Ctrl+Shift10分、サンプルは1／Shift10／Ctrl100／Ctrl+Shift1000）。Enter で移動。右クリックで時間／サンプル数",
        "Current time (G). Type, or wheel / ↑↓ (time: 1 s / Shift 10 s / Ctrl 1 min / Ctrl+Shift 10 min; samples: 1 / Shift 10 / Ctrl 100 / Ctrl+Shift 1000). Enter jumps. Right-click switches time / samples");
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
    public static string LabelStatusSpeaker => "SPEAKER";
    public static string LabelStatusNowPos => "NOW\nPOS";
    public static string LabelStatusSelStart => "SEL\nST";
    public static string LabelStatusSelWidth => "SEL\nWID";
    public static string LabelStatusSelEnd => "SEL\nEND";
    public static string LabelStatusEndPos => "END\nPOS";
    public static string MenuShowTime => Get("時間(_T)", "_Time");
    public static string MenuShowSamples => Get("サンプル数(_S)", "_Samples");
    public static string MenuCopy => Get("コピー(_C)", "_Copy");
    public static string MenuPaste => Get("貼り付け(_P)", "_Paste");
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
    public static string OverlayClose => Get("閉じる", "Close");
    public static string EditHistoryHint => Get(
        "↑↓ 移動　Enter／X／外側クリック 確定　Esc キャンセル\nCtrl+クリック／Shift+↑↓ 選択　Ctrl+C コピー　Ctrl+V 別ファイルへ適用",
        "↑↓ move   Enter / X / click outside apply   Esc cancel\nCtrl+click / Shift+↑↓ select   Ctrl+C copy   Ctrl+V apply to file");

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
        "Pitch Shift" => Get("ピッチ", "Pitch Shift"),
        "Time Stretch" => Get("タイムストレッチ", "Time Stretch"),
        "Reverse" => Get("リバース", "Reverse"),
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
