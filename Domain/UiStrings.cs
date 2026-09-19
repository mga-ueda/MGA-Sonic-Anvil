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

    /// <summary>フッタ権利表記。リンク文言と Wwise 商標行は IM Importer と同様に常に英語。</summary>
    public static string CopyrightText =>
        "© 2026 " + AppVersion.CompanyName + "  " + CopyrightGitHub
        + " / " + CopyrightMitLink + " / " + CopyrightLameLink
        + "\n" + CopyrightWwiseLine;

    public const string CopyrightGitHub = "GitHub";

    public const string CopyrightWwiseLine =
        "Wwise® and Audiokinetic® are trademarks of Audiokinetic Inc.";

    public const string CopyrightMitLink = "MIT License";

    public const string CopyrightLameLink = "LAME";

    public static string UntitledDocument => Get("untitled", "untitled");

    public static string LabelAlwaysOnTop => Get("Always on Top", "Always on Top");
    public static string LabelSilentSkip => Get("Silent Skip", "Silent Skip");
    public static string LabelSilentSkipThreshold => Get(
        "無音しきい値（Silent Skip / 無音削除）",
        "Silence threshold (Silent Skip / Delete Silence)");
    public static string ErrorSilentSkipThresholdRange => Get(
        "無音しきい値は -120 から 0 の dB で入力してください。",
        "Enter a silence threshold between -120 and 0 dB.");
    public static string LabelSilentSkipRecordPad => Get(
        "スキップ時の無音挿入時間",
        "Silence insert duration on skip");
    public static string LabelConfirmRecordSilentSkip => Get(
        "Silent Skip をオンにする",
        "Turn on Silent Skip");
    public static string LabelSilentSkipRecordAddRegion => Get(
        "録音部分にリージョンを付加",
        "Add regions to recorded parts");
    public static string LabelMs => Get("ms", "ms");
    public static string ErrorSilentSkipRecordPadRange => Get(
        "スキップ時の無音挿入時間は 0 から 10000 の ms で入力してください。",
        "Enter a silence insert duration on skip between 0 and 10000 ms.");
    public static string LabelClickGuardFade => Get(
        "プチノイズ防止フェード",
        "Click-prevention fade");
    public static string ErrorClickGuardFadeRange => Get(
        "プチノイズ防止フェードは 1 から 100 の ms で入力してください。",
        "Enter a click-prevention fade between 1 and 100 ms.");
    public static string LabelAudioApi => Get("Audio API", "Audio API");
    public static string LabelAudioDevice => Get("オーディオデバイス", "Audio device");
    public static string LabelSettingsTabGeneral => Get("一般", "General");
    public static string LabelSettingsTabAudio => Get("オーディオ", "Audio");
    public static string LabelSettingsTabLayouts => Get("表示項目", "Shown");
    public static string LabelSettingsTabPlayer => Get("プレイヤー", "Player");
    public static string LabelSettingsTabEditing => Get("編集", "Editing");
    public static string LabelSettingsTabExport => Get("書き出し", "Export");
    public static string LabelSettingsTabWwise => Get("Wwise", "Wwise");
    public static string LabelLibraryExplorerRoots => Get("ツリーのルート", "Tree roots");
    public static string LabelLibraryExplorerRootsHint => Get(
        "プレイヤー左のフォルダツリーに出すルート。上から順。空ならマイミュージック。登録・削除・ドラッグか上へ下へで並び替え。",
        "Roots in the player folder tree, top to bottom. Empty means Music. Add, remove, or reorder by drag or Up / Down.");
    public static string LabelLibraryListColumns => Get("プレイリストの列", "Playlist columns");
    public static string LabelLibraryListColumnsHint => Get(
        "チェックした列だけを表示します。ファイル名は外せません。",
        "Only checked columns are shown. The file name column cannot be turned off.");
    public static string ButtonLibraryExplorerRootAdd => Get("登録…", "Add…");
    public static string TitleLibraryExplorerRootAdd => Get("フォルダを追加", "Add folder");
    public static string ButtonLibraryExplorerRootRemove => Get("削除", "Remove");
    public static string ButtonLibraryExplorerRootUp => Get("上へ", "Up");
    public static string ButtonLibraryExplorerRootDown => Get("下へ", "Down");
    public static string LabelSettingsInput => Get("録音", "Recording");
    public static string LabelSettingsOutput => Get("再生", "Playback");
    public static string LabelSpeaker => Get("スピーカー", "Speakers");
    public static string LabelSpeakerIo => Get("スピーカーと入出力", "Speakers and I/O");
    public static string LabelSpeakerVisibility => Get("有効にするスピーカー定義", "Speaker definitions to enable");
    public static string LabelAutoSpeakerSelect => Get("自動スピーカー選択", "Auto speaker selection");
    public static string ButtonSineMinusTwenty => Get("Sine −20 dB", "Sine −20 dB");
    public static string ButtonChannelVoice => Get("Voice", "Voice");
    public static string LabelPortOff => Get("なし", "Off");
    public static string LabelAudioApiWaveOut => Get("WaveOut", "WaveOut");
    public static string LabelAudioApiWasapi => Get("WASAPI", "WASAPI");
    public static string LabelAudioApiAsio => Get("ASIO", "ASIO");
    public static string ButtonOk => Get("OK", "OK");
    public static string ButtonCancel => Get("Cancel", "Cancel");
    public static string ButtonClose => Get("閉じる", "Close");
    public static string ButtonYes => Get("はい", "Yes");
    public static string ButtonNo => Get("いいえ", "No");
    public static string ButtonYesNoCancel => Get("キャンセル", "Cancel");
    public static string ButtonSaveAllAndExit => Get("すべて保存して終了", "Save all and quit");
    public static string ButtonDiscardAllAndExit => Get("すべて保存せずに終了", "Quit without saving any");
    public static string ConfirmRecord => Get("録音を開始します。", "Start recording.");
    public static string ConfirmRecordSilentSkip(double thresholdDb, int padMs) => Format(
        "Silent Skip をオンにすると、ピークが {0} dB を超えたときだけ録音します。しきい値を下回るとすぐ無音を挟み、{1} ms で止めます。しきい値と無音の尺は設定から変えられます。",
        "When Silent Skip is on, recording writes only audio above {0} dB. When the level falls, silence is inserted immediately and stops at {1} ms. The threshold and silence length can be changed in Settings.",
        thresholdDb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
        padMs);
    public static string DialogSettingsTitle => Get("設定", "Settings");
    public static string LabelUiLanguage => Get("言語", "Language");
    public static string LabelUiTheme => Get("配色", "Theme");
    public static string LabelUiScale => Get("表示倍率", "UI scale");
    public static string LabelMultiFileArrange => Get("複数ファイル", "Multiple files");
    public static string LabelMultiFileArrangeTabs => Get("タブのまま", "Keep tabs");
    public static string LabelMultiFileArrangeHorizontal => Get("左右", "Side by side");
    public static string LabelMultiFileArrangeVertical => Get("上下", "Stacked");
    public static string LabelMultiFileArrangeGrid => Get("上下左右", "Grid");
    public static string LabelThemeAuto => Get("Auto", "Auto");
    public static string LabelThemeDark => Get("Dark", "Dark");
    public static string LabelThemeLight => Get("Light", "Light");
    public static string LabelDefaultAudioFormat => Get("デフォルトオーディオフォーマット", "Default audio format");
    public static string LabelDefaultSampleRate => Get("サンプルレート", "Sample rate");
    public static string LabelDefaultBitDepth => Get("ビット深度", "Bit depth");
    public static string LabelDefaultChannelLayout => Get("チャンネル", "Channels");
    public static string LabelFileAssociations => Get("関連付け", "File associations");
    public static string LabelLanguageAuto => Get("Auto", "Auto");
    public static string LabelLanguageJapanese => Get("Japanese", "Japanese");
    public static string LabelLanguageEnglish => Get("English", "English");
    public static string LabelFadeCurveDefaults => Get("フェードカーブ既定", "Default Fade Curves");
    public static string LabelDefaultFadeIn => Get("波形フェードイン", "Waveform Fade In");
    public static string LabelDefaultFadeOut => Get("波形フェードアウト", "Waveform Fade Out");
    public static string TipAudioSettings => Get(
        "設定 (Ctrl+Shift+O)\n一般／表示項目／オーディオ／編集／書き出し／Wwise のタブ。表示言語、配色（ダーク／ライト／Auto）、表示倍率（100〜200%。OS の DPI に加えて拡大）、複数ファイルの並べ方（タブのまま／左右／上下／上下左右）、関連付け、デフォルトオーディオフォーマット（オーディオタブ。既定 48kHz / 24bit / Stereo。新規ファイル。チャンネルは有効にしたスピーカーと Mono）、自動スピーカー選択（オーディオタブ。既定オフ）、スピーカー配置（モノラル〜Atmos。デバイスとポート割り当て）、有効にするスピーカー定義、プレイリストの列、確認用メーター／Sine −20 dB／Voice、ラウドネス、無音しきい値（Silent Skip / 無音削除）、プチノイズ防止フェード、フェードカーブ、MP3、同時書き出し本数、Wwise の Prefetch Length／Look-ahead Time。ダイアログのボタンは Tab で移動できます。",
        "Settings (Ctrl+Shift+O)\nGeneral / Shown / Audio / Editing / Export / Wwise tabs. Language, theme (Dark / Light / Auto), UI scale (100–200%; extra enlargement on top of the OS DPI), multiple-file arrangement (keep tabs / side by side / stacked / grid), file associations, default audio format (Audio tab; 48 kHz / 24-bit / Stereo by default; used for new files; channels are enabled speakers plus Mono), auto speaker selection (Audio tab; off by default), speaker layouts (mono through Atmos; device and port assignments), which speaker definitions are enabled, playlist columns, meters, per-port Sine −20 dB, and English channel-name Voice, loudness, silence threshold (Silent Skip / Delete Silence), click-prevention fade, fade curves, MP3, parallel export count, and Wwise Prefetch Length / Look-ahead Time. Tab also moves to dialog buttons.");
    public static string TipDefaultAudioFormat => Get(
        "新規ファイル（Ctrl+N）の初期フォーマットです。既定は 48kHz / 24bit / Stereo。チャンネルは表示項目で有効にしたスピーカーと、常に選べる Mono。",
        "Initial format for a new file (Ctrl+N). Default is 48 kHz / 24-bit / Stereo. Channels are the speakers enabled on Shown, plus Mono (always available).");
    public static string DialogNewDocumentTitle => Get("新規", "New");
    public static string TipNewDocument => Get(
        "新規 (Ctrl+N)\n空のタブを作ります。フォーマットは都度選べます。候補のチャンネルは、表示項目で有効にしたスピーカーと Mono（常に選べる）。前回の指定を覚えます。",
        "New (Ctrl+N)\nOpens an empty tab. Choose the format each time. Channels are the speakers enabled on Shown, plus Mono (always available). The last choice is remembered.");
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
    public static string FilterTabTimeCsv => Get("CSV|*.csv|すべて|*.*", "CSV|*.csv|All|*.*");
    public static string FilterTabTimePdf => Get("PDF|*.pdf|すべて|*.*", "PDF|*.pdf|All|*.*");
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
    public static string ErrorRenameFailed => Get("名前の変更に失敗しました。", "Failed to rename the file.");
    public static string ErrorDeleteFailed => Get("ファイルの削除に失敗しました。", "Failed to delete the file.");
    public static string ErrorDuplicateFailed => Get("ファイルの複製に失敗しました。", "Failed to duplicate the file.");
    public static string ErrorMergeFailed => Get(
        "タブのバウンスに失敗しました。",
        "Failed to bounce the tabs.");
    /// <summary>バウンスの保存ダイアログのタイトル。</summary>
    public static string TitleMergeTabs => Get("選択ファイルをバウンス", "Bounce Selected Files");
    public static string ErrorFileNameEmpty => Get("ファイル名を入力してください。", "Enter a file name.");
    public static string ErrorFileNameInvalid => Get("使えないファイル名です。", "That file name is not allowed.");
    public static string ErrorFileNameExists => Get("同じ名前のファイルがあります。", "A file with that name already exists.");
    public static string ErrorFileNameOpen => Get("そのファイルは別のタブで開いています。", "That file is already open in another tab.");
    public static string ConfirmDeleteFile(string name) => Format(
        "{0} をディスクから削除しますか？この操作は元に戻せません。",
        "Delete {0} from disk? This cannot be undone.",
        name);
    public static string ConfirmDeleteUntitled(string name) => Format(
        "{0} を削除しますか？タブを閉じ、未保存の変更は破棄されます。",
        "Delete {0}? The tab will close and unsaved changes will be discarded.",
        name);
    public static string ConfirmDuplicateFile(string name) => Format(
        "{0} を複製しますか？",
        "Duplicate {0}?",
        name);
    public static string ConfirmDuplicateFileAs(string name, string copyName) => Format(
        "{0} を複製しますか？{2}{1} として隣のタブで開きます。",
        "Duplicate {0}?{2}It will open in the next tab as {1}.",
        name,
        copyName,
        Environment.NewLine);
    public static string ConfirmDuplicateSelectedFiles(int count) => Format(
        "選択した {0} 個のファイルを複製しますか？それぞれ元のタブの隣で開きます。",
        "Duplicate the {0} selected files? Each copy opens next to its own tab.",
        count);
    public static string ConfirmDeleteSelectedFiles(int count) => Format(
        "選択した {0} 個のファイルをディスクから削除しますか？この操作は元に戻せません。{1}（未保存のファイルはタブを閉じ、変更を破棄します）",
        "Delete the {0} selected files from disk? This cannot be undone.{1}(Unsaved files just close and discard their changes.)",
        count,
        Environment.NewLine);
    public static string TipRenameFile => Get(
        "ダブルクリックでファイル名を変更します。",
        "Double-click to rename the file.");
    public static string ErrorAiffExport => Get(
        "AIFF の書き出しには対応していません。Wave または MP3 を選んでください。",
        "AIFF export is not supported. Choose Wave or MP3.");
    public static string ErrorNoDocument => Get("ファイルが開かれていません。", "No file is open.");
    public static string ErrorNoSelection => Get("選択範囲がありません。", "Nothing is selected.");
    public static string ErrorNoRegions => Get("リージョンがありません。", "There are no regions.");
    public static string ErrorClipboardEmpty => Get("クリップボードが空です。", "The clipboard is empty.");
    public static string ErrorClipboardBusy => Get(
        "クリップボードを開けませんでした。別のプログラムが使用中の可能性があります。",
        "Could not open the clipboard. Another program may be using it.");

    public static string ErrorEmptyAfterDelete => Get(
        "ファイル全体は削除できません。",
        "The entire file cannot be deleted.");
    public static string ErrorNoSilenceToDelete => Get(
        "削除できる無音がありません。",
        "There is no silence to delete.");
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
    public static string LabelSilentSkipFloorNote => Get("（谷の平均）", "(avg. floor)");
    public static string LabelLoudnessTarget => Get("ラウドネスターゲット", "Loudness Target");
    public static string ErrorLoudnessTargetRange => Get(
        "ラウドネスターゲットは -70 から 0 の LKFS で入力してください。",
        "Enter a loudness target between -70 and 0 LKFS.");
    public static string OverlaySampleRateConvert => Get("サンプリングレート変換", "Sample rate conversion");
    public static string OverlayPitchShift => Get("ピッチシフト", "Pitch shift");
    public static string OverlayPasteHistory => Get("編集履歴を貼り付けています", "Pasting edit history");
    public static string OverlayApplySelected => Get("選択したファイルへ適用しています", "Applying to the selected files");
    public static string OverlayMerge => Get("タブをバウンスしています", "Bouncing tabs");
    public static string OverlayOpening => Get("読み込んでいます（Esc で中断）", "Opening (Esc to stop)");
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
    public static string LabelEditGroup => Get("EDIT", "EDIT");
    public static string LabelMarkerGroup => Get("MARK", "MARK");
    public static string LabelFileGroup => Get("FILE", "FILE");
    public static string LabelViewGroup => Get("VIEW", "VIEW");
    public static string LabelHelpGroup => Get("HELP", "HELP");

    public static string TooltipRecord => Get("録音 (Ctrl+R)", "Record (Ctrl+R)");
    public static string TooltipPlay => Get("再生 / 停止 (Space)", "Play / stop (Space)");
    public static string TooltipTimeZoomIn => Get("時間拡大 (↑)", "Zoom in time (↑)");
    public static string TooltipTimeZoomOut => Get("時間縮小 (↓)", "Zoom out time (↓)");
    public static string TooltipAmpZoomIn => Get("振幅拡大 (Shift+↑)", "Zoom in amplitude (Shift+↑)");
    public static string TooltipAmpZoomOut => Get("振幅縮小 (Shift+↓)", "Zoom out amplitude (Shift+↓)");
    public static string TooltipFadeIn => Get("フェードイン (I)", "Fade in (I)");
    public static string TooltipFadeOut => Get("フェードアウト (O)", "Fade out (O)");
    public static string TooltipNormalize => Get("ノーマライズ (N)", "Normalize (N)");
    public static string TooltipDelete => Get("部分削除 (Delete)", "Ripple delete (Delete)");
    public static string TooltipSave => Get("保存 (Ctrl+S)", "Save (Ctrl+S)");
    public static string TooltipSaveMp3 => Get("MP3 として保存 (Ctrl+Shift+M)", "Save as MP3 (Ctrl+Shift+M)");
    public static string TooltipTipsToggle => Get("Tips の表示", "Show Tips");
    public static string TooltipSettings => Get("設定 (Ctrl+Shift+O)", "Settings (Ctrl+Shift+O)");
    public static string TooltipManualHelp => Get("マニュアル", "Manual");
    public static string TooltipFadeAround => Get(
        "シークバー前後をフェード (X)  見えている前だけアウト／後ろだけイン",
        "Fade around seek bar (X)  fade out visible before / fade in visible after");
    public static string TooltipVolume => Get("音量 (V)", "Volume (V)");
    public static string TooltipPitch => Get("ピッチ (P)", "Pitch (P)");
    public static string TooltipTimeStretch => Get("タイムストレッチ (T)", "Time stretch (T)");
    public static string TooltipReverse => Get("リバース (R)", "Reverse (R)");
    public static string TooltipAddMarker => Get("マーカーを追加 (M)", "Add marker (M)");
    public static string TooltipSetLoop => Get("選択をループに (Shift+L)", "Set selection as loop (Shift+L)");
    public static string TooltipSetRegion => Get("選択をリージョンに (Shift+R)", "Set selection as region (Shift+R)");
    public static string TooltipOpen => Get("開く (Ctrl+O)", "Open (Ctrl+O)");
    public static string TooltipNew => Get("新規 (Ctrl+N)", "New (Ctrl+N)");
    public static string TooltipSaveAs => Get("名前を付けて保存 (Ctrl+Shift+S)", "Save As (Ctrl+Shift+S)");
    public static string TooltipSpectrogramView => Get("スペクトログラム / 重ね (A)  解除 (Esc / Shift+A / Shift+V)", "Spectrogram / overlay (A)  leave (Esc / Shift+A / Shift+V)");
    public static string TooltipLoudnessView => Get("ラウドネス (V)  解除 (Esc / Shift+A / Shift+V)", "Loudness (V)  leave (Esc / Shift+A / Shift+V)");
    public static string TooltipCenterPlayhead => Get("中央寄せ / センターロック (Z)", "Center / center-lock (Z)");
    public static string TooltipHistory => Get("編集履歴 (U)", "Edit history (U)");
    public static string TooltipLibraryMaximize => Get("プレイヤー (F10)", "Player (F10)");
    public static string TooltipLibraryMaximizeOff => Get("エディタ (F10)", "Editor (F10)");
    public static string TooltipAnalyzerMaximize => Get("フルスクリーン（アナライザー） (F12)", "Fullscreen with analyzers (F12)");
    public static string TooltipAnalyzerMaximizeOff => Get("アナライザー最大化を解除 (F12)", "Leave analyzer fullscreen (F12)");
    public static string TooltipUiThemeToggle => Get("ダーク / ライト", "Dark / Light");
    public static string TooltipColorPanel => Get("色設定 (Ctrl+Shift+C)", "Color settings (Ctrl+Shift+C)");

    public static string TipFadeAround => Get(
        "シークバー前後をフェード (X)\nシークバーを境に、今見えている前だけアウト／後ろだけイン（リニア）",
        "Fade around seek bar (X)\nFrom the seek bar, fade out only the visible part before / fade in only the visible part after (linear)");
    public static string TipReverse => Get("リバース (R)\n選択範囲を時間方向に反転。未選択なら全体", "Reverse (R)\nReverse the selection in time. Uses the whole file if nothing is selected");
    public static string TipAddMarker => Get("マーカーを追加 (M / Ins)\n選択中は両端。同じ範囲で繰り返すと分割", "Add marker (M / Ins)\nPlaces both ends of a selection; repeat to split");
    public static string TipSetLoop => Get(
        "選択をサンプルループに (Shift+L)\n同じ範囲でもう一度で解除。EXPORT ではループ前が Intro、ループが -L。後ろに余りがあれば -E（波形は赤くしない）",
        "Set selection as sample loop (Shift+L)\nSame range again clears it. EXPORT treats the part before as Intro and the loop as -L. A remainder after the loop becomes -E (the waveform is not painted red)");
    public static string TipSetRegion => Get("選択をリージョンに (Shift+R)\n同じ範囲で繰り返すと分割", "Set selection as region (Shift+R)\nRepeat on the same range to split");
    public static string TipSaveAs => Get("名前を付けて保存 (Ctrl+Shift+S)", "Save As (Ctrl+Shift+S)");
    public static string TipSpectrogramView => Get(
        "スペクトログラム / 重ね (A)\nスペクトログラム → 波形上乗せ。抜けるのは Esc、Shift+A、Shift+V（今の表示にかかわらず波形へ）。ボタンは 3 回で波形に戻る。暗部持ち上げは左端のバーまたは Alt+↑／↓",
        "Spectrogram / overlay (A)\nSpectrogram → waveform overlay. Esc, Shift+A, or Shift+V returns to the waveform from any view. The button returns to the waveform on the third click. Lift dark energy with the left bar or Alt+↑ / ↓");
    public static string TipLoudnessView => Get(
        "ラウドネス表示 (V)\nV で曲線と音量入力を開く。抜けるのは Esc、Shift+A、Shift+V（今の表示にかかわらず波形へ）。ボタンは表示のオン／オフ",
        "Loudness view (V)\nV shows the curve and opens volume input. Esc, Shift+A, or Shift+V returns to the waveform from any view. The button toggles the view only");
    public static string TipCenterPlayhead => Get(
        "中央寄せ (Z / .)\n再生中はセンターロックの切替",
        "Center (Z / .)\nToggles center-lock while playing");
    public static string TipLibraryMaximize => Get(
        "プレイヤー (F10)\nリストと短い波形の再生モードへ。編集はできない",
        "Player (F10)\nList and short-waveform playback mode. Edits are blocked");
    public static string TipLibraryMaximizeOff => Get(
        "エディタ (F10)\n波形編集モードへ戻す",
        "Editor (F10)\nReturn to waveform editing");
    public static string TipAnalyzerMaximize => Get(
        "フルスクリーン（アナライザー） (F12)\nタイトルバーだけ隠し、メーター類を 1.5 倍にする",
        "Fullscreen with analyzers (F12)\nHides only the title bar and scales meters 1.5×");
    public static string TipAnalyzerMaximizeOff => Get(
        "アナライザー最大化を解除 (F12)\n通常のウィンドウ表示へ戻す",
        "Leave analyzer fullscreen (F12)\nRestore the normal window chrome");
    public static string TipUiThemeToggle => Get(
        "ダークとライトを切り替えます。設定の配色は明示的な Dark / Light になります（Auto は外れます）。",
        "Switch Dark and Light. Settings theme becomes an explicit Dark / Light (Auto is cleared).");
    public static string TipColorPanel => Get(
        "色設定 (Ctrl+Shift+C)\n今のモードの色を調整します。タイトルにダークモード／ライトモードを出します。",
        "Color settings (Ctrl+Shift+C)\nTune colors for the current mode. The title shows Dark mode or Light mode.");

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
        "チェックしたスピーカー定義だけ、設定とステータスバー（NOW POS の左）の一覧に出します。既定は Stereo だけです。今使っている定義は、外しても切り替えるまで残ります。全部外すことはできません。自動スピーカー選択の候補もこの一覧です。",
        "Only enabled speaker definitions appear in Settings and the status-bar menu (left of NOW POS). Stereo is on by default. The definition in use stays listed until you switch away. At least one must stay enabled. Auto speaker selection uses this list.");
    public static string TipAutoSpeakerSelect => Get(
        "オンにすると、アクティブなファイルのチャンネル数に合わせて、有効にしたスピーカー定義へ切り替えます。1〜2ch は Stereo。同じ本数が複数あるときはいちばん普通の配置（6ch なら 5.1）。今の配置がすでに同じ本数なら維持します（5.1 Side を 5.1 へ戻しません）。候補が無いか、複数あって既定が無効なら切り替えません。既定はオフ。ファイルの正体は当てません。切替は再生と録音を止め、定義ごとのデバイスとポート割り当てに載せ替えます。Stereo と 5.1 で別デバイスなら、タブを変えるたびに出力先が変わります。",
        "When on, switches to an enabled speaker definition that matches the active file's channel count. 1–2 ch uses Stereo. If several layouts share that count, the usual one is used (5.1 for 6 ch). If the current layout already has the same count, it stays (5.1 Side is not reset to 5.1). No change if nothing matches, or if several match and the usual one is not enabled. Off by default. The file itself is not labeled. Switching stops playback and recording, and applies that definition's device and port map. If Stereo and 5.1 use different devices, the output jumps when you change tabs.");
    public static string TipSpeakerSwitch => Get(
        "使うスピーカー配置を切り替えます。デバイスとポート割り当てが一緒に変わります。一覧は設定の表示項目タブで絞れます。",
        "Switch speaker layout. The device and port assignments change with it. The list is filtered in Settings → Shown.");
    public static string TipRecord => Get(
        "録音 (Ctrl+R)\n開始前に毎回、Silent Skip をオンにするか確認する。今開いているファイルのフォーマットで、シークバーの位置から録る。それ以降の波形は上書き（Undo で戻せる）。ファイルが無いときは始まらない（先に Ctrl+N で作るか、ファイルを開く）。波形はすぐ出して、あとから整える。Silent Skip がオンならしきい値を超えたときだけ録り、下回った時点から無音を挟んで挿入時間で止める。可聴の前後にはプチノイズ防止フェード（設定の編集タブ。既定 20 ms）をかける。もう一度で停止。スピーカー配置・録音ポート・Ch の割り当てを使う。Space / Enter / Esc でも停止。",
        "Record (Ctrl+R)\nAsks every time whether to turn Silent Skip on. Records in the current file format from the seek bar, overwriting audio after that point (Undo restores it). Does nothing if no file is open; create one with Ctrl+N or open a file first. The waveform appears immediately and is refined after. With Silent Skip on, only audio above the threshold is recorded, and silence is inserted when the level falls (up to the insert duration in Settings). Each recorded burst gets the click-prevention fade from the Editing tab (default 20 ms) at both ends. Press again to stop. Uses the speaker layout, record ports, and Ch map. Space / Enter / Esc also stop.");
    public static string TipRecordInputMap => Get(
        "各スピーカーがどのポートから入り、どの Ch へ書くか。なしは無音。右のバーは今のレベルです。Ch は再生と共通です。",
        "Which port feeds each speaker, and which file channel (Ch) it writes. Off is silence. The bar is the live level. Ch is shared with playback.");
    public static string TipFileChannelMap => Get(
        "このスピーカーがファイルのどの Ch か。録音と再生で同じ割り当てです。なしはそのスピーカーを使いません。一覧は今のスピーカー配置の本数まで。初期値は 1 から順です。",
        "Which file channel (Ch) this speaker uses. Shared by record and playback. Off leaves the speaker unused. The list matches the current speaker count. The default is 1, 2, 3…");
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
        "音量 (V)\nラウドネス曲線を表示して dB を入力。↑↓／ホイールで 0.1 dB（Shift 1／Ctrl 3／Ctrl+Shift 6）。数字キーで直接入力。Space で試聴、Enter で実行。未選択なら全体。波形全体の Integrated LKFS / RMS / Peak の変化を先に表示。曲線と停止中メーターも同じ dB で追従。Shift+V で曲線を閉じる。",
        "Volume (V)\nShows the loudness curve and accepts a dB value. ↑↓ / wheel by 0.1 dB (Shift 1 / Ctrl 3 / Ctrl+Shift 6). Type digits to enter a value. Space previews, Enter applies. Uses the whole file if nothing is selected. Shows how whole-file Integrated LKFS / RMS / Peak will change. The curve and the stopped meter follow the same dB. Shift+V closes the curve.");
    public static string TipPitch => Get(
        "ピッチ (P)\n↑↓／ホイールで半音（Shift または Ctrl で1オクターブ）。Space で試聴、Enter で実行。未選択なら全体。±2オクターブ。長さを保つ（既定オン。Tab でチェックへ）。オフは長さも変わる。長さだけなら T。",
        "Pitch (P)\n↑↓ / wheel by a semitone (Shift or Ctrl for an octave). Space previews, Enter applies. Uses the whole file if nothing is selected. Range is ±2 octaves. Keep length (on by default. Tab moves to the checkbox). Off also changes length. T stretches time only.");
    public static string TipDelete => Get(
        "部分削除 (Delete)\n選択範囲を詰めて削除\n選択中のマーカー / リージョンはまとめて削除\nCtrl+Del でマーカー削除\n右クリックの「無音部分を削除」は Silent Skip と同じしきい値。継ぎ目にプチノイズ防止フェード",
        "Ripple delete (Delete)\nRemove the selection and close the gap\nSelected markers / regions are deleted together\nCtrl+Del deletes markers\nDelete Silence on the right-click menu uses the Silent Skip threshold. Splices get the click-prevention fade");
    public static string TipSave => Get(
        "保存 (Ctrl+S)\nCtrl+Shift+S で別名保存\nCtrl+Shift+M で MP3 保存（今のタブは開いたまま。書き出した MP3 は読み込まない）\nCtrl+Shift+Alt+M で全タブを MP3 で保存\nMP3 は PCM 16bit から再エンコード。LAME のパスが有効ならそれを使い、空欄または無効なら Windows（既定 192 kbps）。成功時に LAME / Windows を表示。失敗はダイアログ。マーカー／リージョン／ループは MP3 に書きません",
        "Save (Ctrl+S)\nCtrl+Shift+S to save as\nCtrl+Shift+M to save as MP3 (keeps the current tab; does not open the written MP3)\nCtrl+Shift+Alt+M saves every tab as MP3\nMP3 is re-encoded from 16-bit PCM. A valid LAME path is used; empty or invalid falls back to Windows (default 192 kbps). Success shows LAME / Windows. Failures open a dialog. Markers / regions / loops are not written to MP3");
    public static string TipSaveMp3 => Get(
        "MP3 として保存 (Ctrl+Shift+M)\n別名保存と同じく書き出すだけ。今のタブは開いたまま、書き出した MP3 は読み込まない。Ctrl+Shift+Alt+M で全タブを MP3 で保存。設定の LAME があればそれを使い、空欄または無効なら Windows（既定 192 kbps）。成功時にどちらで書いたかを表示。失敗はダイアログ。マーカー／リージョン／ループは書きません",
        "Save as MP3 (Ctrl+Shift+M)\nWrites a file like Save As; keeps the current tab and does not open the written MP3. Ctrl+Shift+Alt+M saves every tab as MP3. Uses LAME when the path is valid; otherwise Windows (default 192 kbps). Success shows which encoder ran. Failures open a dialog. Markers / regions / loops are not written");
    public static string TipOpen => Get(
        "開く (Ctrl+O)\nドロップでも可。フォルダは中の Wave / AIFF / MP3 を再帰的に追加（非対応は無視）。複数ファイルはタブで追加。複数読み込み中は Esc で中断（読み込み済みは残す）。\nWave / AIFF / MP3\nCtrl+N で新規（フォーマットは都度指定）\nCtrl+W でタブを閉じる（未保存なら保存確認）\nCtrl+Shift+D でファイルを複製（隣のタブで開く）\nCtrl+Shift+B で選択ファイルをバウンス（先に保存先を指定。重ねて合成した Wave を右側のタブで開く。既定名 Bounce.wav。フォーマットは左端の選択タブ）\nCtrl+Q でアプリを終了（開いていたタブと未保存の作業コピーは次回起動時に戻す）\nCtrl+Shift+T で閉じたタブを開き直す（今の起動で閉じたもの。終了すると忘れる）\nCtrl+Tab で次のタブ\nCtrl+T でタイル表示を巡回（横並び → 縦並び → 格子 → 解除。見た目が同じ配置、波形エリアに収まらない配置は飛ばす。左右・上下が収まらなければ格子を試し、格子も無理なら動かない。今見ている表示モードで全タブを並べる）\n右クリックで、すべてのファイルの時間を表示（表。コピー／範囲コピー／CSV／PDF。複数タブ選択中は選択ファイルのみ）など\nタブが増えると先にアクティブ以外を縮める。6文字を切るまで縮めても収まらなければ左右ボタンで送る",
        "Open (Ctrl+O)\nDrop also works. A folder adds every Wave / AIFF / MP3 inside it (unsupported types are skipped). Multiple files open as extra tabs. Esc stops a multi-file load and keeps files already opened.\nWave / AIFF / MP3\nCtrl+N for a new file (choose the format each time)\nCtrl+W closes the tab (asks to save if dirty)\nCtrl+Shift+D duplicates the file (opens in the next tab)\nCtrl+Shift+B bounces selected tabs (choose where to save first; the mixed Wave opens in the tab to the right; default name Bounce.wav; format follows the leftmost selected tab)\nCtrl+Q quits (tabs left open and unsaved working copies come back on the next launch)\nCtrl+Shift+T reopens tabs closed in this launch (forgotten after quit)\nCtrl+Tab goes to the next tab\nCtrl+T cycles tile layout (side by side → stack → grid → restore; skip a layout that looks the same or would not fit the waveform area. If side-by-side or stacked would not fit, it tries grid; if grid would not fit either, it does nothing. Every tab uses the current view mode)\nRight-click for all file times (table with copy / range copy / CSV / PDF; only the selected files while multiple tabs are selected) and more\nExtra tabs shrink (inactive first) before scroll arrows. Arrows appear only if a title would fall below 6 characters");
    public static string TipCloseTab => Get(
        "タブを閉じる (Ctrl+W)。今の起動のうちなら Ctrl+Shift+T で開き直せる",
        "Close tab (Ctrl+W). Ctrl+Shift+T reopens it in this launch");
    public static string TipTabScrollLeft => Get("左のタブを表示", "Show tabs to the left");
    public static string TipTabScrollRight => Get("右のタブを表示", "Show tabs to the right");
    public static string TipOverview => Get(
        "波形全体。明るい部分が表示中の範囲。ドラッグで移動（中央をスクラブ）　ホイールで拡縮（再生ヘッド基準）",
        "Whole file. The bright area is the current view. Drag to move (scrubs the center). Wheel zooms around the playhead");
    public static string TipSpectrum => Get(
        "再生中の全チャンネルを畳んだ LED スペクトラムです。1/3oct 相当の帯域とピークホールド。Layer Music Checker と同じ検波です。",
        "LED spectrum of every playback channel mixed together. Third-octave-style bands and peak hold, same detection as Layer Music Checker.");
    public static string TipLoudness => Get(
        "ラウドネス（ITU-R BS.1770 / EBU R128）。Short Term・Integrated・Momentary Max、Loudness Range、True Peak。ターゲット LKFS は設定で変更。青＝余裕、橙＝接近、赤＝超過（LKFS はターゲット、True Peak は 0 dBTP、Loudness Range は 20/25 LU）。再生中は出力のリアルタイム計測（数値が色）。停止中は波形全体のオフライン解析（Short Term はファイル内の最大。色が塗り、文字は通常色）。音量などの編集後は測り直します。音声は変えません。",
        "Loudness (ITU-R BS.1770 / EBU R128): Short Term, Integrated, Momentary Max, Loudness Range, True Peak. Target LKFS is in Settings. Blue = headroom, orange = approaching, red = over (LKFS vs target, True Peak vs 0 dBTP, Loudness Range vs 20/25 LU). While playing, values are live from the output (colored text). While stopped, they are an offline read of the whole file (max Short Term; the color fills the value, text uses the default color). Re-measures after edits such as volume. Does not change the audio.");
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
    public static string TipMultiFileArrange => Get(
        "2つ以上のファイルを開いたあとの並べ方です。既定はタブのまま（Ctrl+T でタイル）。左右・上下・上下左右を選ぶと、読み込み後にその配置で並べます。左右・上下が波形エリアに収まらなければ格子を試し、格子も無理なら並べません。",
        "How to arrange two or more files after opening them. Default is keep tabs (Ctrl+T to tile). Side by side, stacked, or grid arranges them after a load. If side-by-side or stacked would not fit the waveform area, grid is tried; if grid would not fit either, they stay as tabs.");
    public static string TipUiTheme => Get(
        "配色。Auto は OS のアプリ配色に従います。背景と文字だけ変わり、再生ヘッドやマーカーなどのアクセント色は維持します。",
        "Theme. Auto follows the OS app theme. Only backgrounds and text change; accent colors such as the playhead and markers stay the same.");
    public static string TipUiScale => Get(
        "アプリの表示サイズ。OS の DPI に加えて拡大します。100〜200%。縮小はありません。作用するのはメインウィンドウのみで、メニューや設定などの別ウィンドウ、編集履歴は等倍のままです。コンボを変えた瞬間に反映します。キャンセルすると元に戻します。",
        "App display size. Extra enlargement on top of the OS DPI. 100–200%. No shrinking. Affects the main window only; menus, separate windows such as Settings, and the edit history stay at 100%. Applies as soon as you change the combo. Cancel restores the previous value.");
    public static string TipFileAssociations => Get(
        "チェックすると、その拡張子をこのアプリで開く（既定）。外すと関連付けを外す。すでにこのアプリが既定ならチェック済み。OK を待たず、今の exe へすぐ書き込みます。M4A はプレイヤーモード専用（ダブルクリックすると F10 プレイヤーで開く。波形編集では開けません）。",
        "Check to make this app the default for that extension. Uncheck to remove the association. Types already using this app are checked. Writes to this exe immediately, without waiting for OK. M4A is player-mode only (double-click opens the F10 player; it cannot be opened for waveform editing).");
    public static string TipFileAssociationM4a => Get(
        "M4A はプレイヤーモード専用です。関連付けるとダブルクリックで F10 プレイヤーが起動します。波形編集の Open では選べません。",
        "M4A is player-mode only. Associating it opens the F10 player on double-click. It is not available in the editor Open dialog.");
    public static string LabelFileAssociationPlayerOnly => Get(
        "プレイヤーモードのみ",
        "Player mode only");
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
        "サンプリングレート / ビット深度 / チャンネル / 形式 / 容量。MP3／M4A はビットレートも出す。S / B / C で変換。変換や範囲削除で長さが変わると容量は推測サイズになり赤。確定項目は保存まで赤。",
        "Sample rate / bit depth / channels / format / size. MP3 / M4A also show bit rate. S / B / C convert. Size turns red as an estimate after conversion or a range delete that changes length. Confirmed fields stay red until you save.");
    public static string TipLevelMeter => Get(
        "再生出力の Peak / RMS。内側 2 本が Peak（上の赤ランプは 0 dBFS を超えたサンプルピークで2秒点灯）、外側 2 本が RMS。どちらもホールド線が付きます。下の数値は Peak 行／RMS 行（超過は +）。3ch 以上はチャンネルごとの Peak バーに、Peak ホールドと緩やかな RMS ホールドを載せます。バーの色は波形左端のチャンネル名の四角と同じ登場順（Atmos 9.1.6 の 16 色）。2ch 以下はシアングラデです。メーターにチャンネル名は出しません。左端をドラッグすると列を広げられます（既定が最小。バーが最大の太さになるところで止まります。次の起動まで覚えます）。",
        "Playback Peak / RMS. Inner two bars are Peak (red lamps light for 2 seconds when a sample peak exceeds 0 dBFS), outer two are RMS. Both have hold lines. Numbers below are Peak then RMS (overs show +). Three or more channels show one Peak bar per channel, with Peak hold and a slower RMS hold. Bar colors match the colored squares on the waveform channel names (16 colors for Atmos 9.1.6). Stereo and mono use the cyan gradient. The meter does not show channel names. Drag the left edge to widen the column (the default is the minimum; it stops when the bars reach full thickness; the width is remembered).");
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
        "© MIYABI GAME AUDIO INC. MIT License で公開。GitHub はリポジトリ、MIT License は全文、LAME は公式サイト。LAME は同梱せず、設定の lame.exe だけを呼びます（LGPL。入手と遵守は利用者側）。Wwise®／Audiokinetic® は Audiokinetic Inc. の商標。非公式。WAAPI には有効な Wwise ライセンスが必要です。",
        "© MIYABI GAME AUDIO INC. Released under the MIT License. GitHub opens the repository, MIT License the full text, LAME the project site. LAME is not bundled; only a user-supplied lame.exe is run (LGPL; obtaining it and complying is the user’s responsibility). Wwise® / Audiokinetic® are trademarks of Audiokinetic Inc. This tool is unofficial. WAAPI requires a valid Wwise license.");
    public static string TipBrandLogo => Get(
        "MIYABI GAME AUDIO のウェブサイトを開きます。",
        "Open the MIYABI GAME AUDIO website.");
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
        "クリックで再生位置。←→ でシーク（再生中は押しっぱなしで 3 倍早送り／巻き戻し（推移中は-4dB））。ドラッグで選択。Ctrl+ドラッグでスクラブ（全chを L/R に畳む）。\n"
        + "Esc または Shift なし移動で解除。Shift＋ドラッグ／←→ で伸長。ダブルクリックで区間（マーカー間）。Shift＋ダブルクリックでその区間を追加。トリプルクリックで全選択。ガイドはマーカー／ループ端に吸着。\n"
        + "左端のチャンネル名：クリックでソロ（再クリックで解除。Ctrl で追加。Shift でミュート）。Tab／Shift+Tab で順にソロ。\n"
        + "ホイール＝時間ズーム（再生ヘッド基準）。Shift+ホイール＝パン。Ctrl+ホイール＝振幅。\n"
        + "フラッグ／ループ端：クリックで選択、ドラッグまたは ←→ で移動。Alt+←→ は微調整。ダブルクリックで名前。右クリックまたはメニューキー／Shift+F10 でメニュー。\n"
        + "Ctrl+T でタイル表示を巡回（横並び → 縦並び → 格子 → 解除。見た目が同じ配置、波形エリアに収まらない配置は飛ばす。左右・上下が収まらなければ格子を試し、格子も無理なら動かない）。今見ている表示モード（波形 / スペクトログラム / 重ね / ラウドネス）で全タブを並べる。並び替え中でももう一度押すと次へ進む。\n"
        + "F11 で波形エリアをフルスクリーン（タスクバーは隠す。チャンネル名と dB 目盛りも出さない。全体波形とステータスバーは残す。タイルでも可。もう一度 F11 で戻す）。\n"
        + "F12 はタイトルバーだけ隠すフルスクリーン。タブ・トランスポート・メーター・Tips・WAAPI・チャンネル名・dB 目盛りは残す。スペクトラム／ラウドネス／レベルメーター／ベクタースコープは 1.5 倍（OS の DPI と表示倍率のうえ）。タイルでも可。もう一度 F12 で戻す。\n"
        + "F10 はプレイヤーモード。全画面にはしない。タブは出さず、上段は左にフォルダツリー、中央がファイルリスト。下段は固定の低い波形。全体波形は出さない。既定はアルバムでグループし、グループごとにジャケットを出す。再生や曲送りではリストを作り直さない。入ると1曲めを選んで再生する（引数起動も同じ）。再生開始と同時に次の曲を先読みし、終わったら隙間なく次を再生する。最後の次は先頭に戻って再生を続ける。↑↓ でファイル選択。Home／End でリストの先頭／末尾。PageUp／PageDown でページ送り（再生中はキーを離すとその曲へ。停止中は選択だけ。複数選択可）。F1 でツリー、F2 でお気に入り、F3 でプレイリストにフォーカス。Tab でツリー→お気に入り→プレイリスト（Shift+Tab は逆。フォーカス中は上端に明るいグラデーション）。ツリーでは ←→ でフォルダを展開／折りたたみ（プレイヤーでは左右キーの早送り／巻き戻しはしない）。Enter で再生／先頭から再生し直し。テンキーはどのペインからでも（NumLock オン）0＝再生／一時停止、1＝早戻し、3＝早送り（推移中は-4dB）、4＝前の曲、5＝再生し直し、6＝次の曲、7／9＝5秒戻し／送り（0.75秒クロスフェード。押しっぱなしは少し待ってから繰り返す）。キーボード上段の 0–9 は表示範囲の割合ジャンプのまま。波形はシークと選択だけ（右クリックメニューは出さない。拡大しない。表示用のピークだけ作る。短いファイルは粗くしない。スペクトラム／ラウドネス曲線ビューには切り替えない）。編集コマンドは使えない。他モードのファイルはリストへ引き継ぐ。抜けるときは再生を止めてから、選択したファイルだけ残す。プレイヤーのまま終了したときは、読み込んだファイルは覚えない。ウィンドウの位置とサイズは通常モードと別々に覚え、切り替えで戻す。波形は全件走査しないが、タグと時間は登録時に読む。列の出し分けは設定の表示項目（状態は覚える）。左のツリーは既定でマイミュージック、選んだ場所と、お気に入りとの仕切り位置は覚える。フォルダを選んでもリストへは自動で載せない。ツリーの Enter はプレイリストをクリアしてそのフォルダ配下を載せる。Shift+Enter とダブルクリック、リストへのフォルダドロップは配下ごと追加する。MP3／M4A／WAVE／AIFF はフル PCM 展開せずストリーム再生する（M4A はプレイヤー専用）。ジャケットは選択時に読む。埋め込みが無ければ No Image のプレースホルダ。WAAPI は無効（入る前にオンなら、抜けたときに戻す）。引数に mp3／m4a が1つでも混ざるとプレイヤーで起動し、それらのファイルをリストへ載せる。もう一度 F10 で戻す。",
        "Click to set the playhead. ←→ seek (hold during playback for 3× shuttle (−4 dB while held)). Drag to select. Ctrl+drag scrubs (every channel downmixed to L/R).\n"
        + "Esc or a move without Shift clears the selection. Shift+drag / ←→ extends it. Double-click a span (between markers). Shift+double-click adds that span. Triple-click selects all. The guide snaps to markers / loop edges.\n"
        + "Channel names on the left: click to solo (again to clear; Ctrl adds; Shift mutes). Tab / Shift+Tab cycle solo.\n"
        + "Wheel = time zoom (around the playhead). Shift+wheel = pan. Ctrl+wheel = amplitude.\n"
        + "Flags / loop edges: click to select, drag or ←→ to move. Alt+←→ nudges. Double-click to name. Right-click or the menu key / Shift+F10 for the menu.\n"
        + "Ctrl+T cycles tile layout (side by side → stack → grid → restore; skip a layout that looks the same or would not fit the waveform area. If side-by-side or stacked would not fit, it tries grid; if grid would not fit either, it does nothing). Every tab uses the current view mode (waveform / spectrogram / overlay / loudness). Press again during arrange to skip ahead.\n"
        + "F11 makes the waveform area fullscreen like a browser (hides the taskbar; also hides channel names and the dB scale; keeps the overview and status bar; works while tiled; F11 again restores).\n"
        + "F12 is fullscreen with only the title bar hidden. Tabs, transport, meters, Tips, WAAPI, channel names, and the dB scale stay. Spectrum, loudness, level meter, and vector scope are 1.5× (on top of OS DPI and UI scale). Works while tiled; F12 again restores.\n"
        + "F10 is a player, not fullscreen: no tabs, file list on top (folder tree left, list center) and a short fixed waveform below. The overview is hidden. Groups default to album, with a jacket beside each group. Playback and skip do not rebuild the list. Entering selects and plays the first track (the same on launch with arguments). The next track is read ahead as soon as playback starts, then plays without a gap. After the last, playback returns to the first and keeps going. ↑↓ select files. Home / End jump to the first / last track. Page Up / Page Down page the list (while playing, releasing the key switches to that track; while stopped, selection only; multi-select allowed). F1 focuses the tree, F2 Favorites, F3 the playlist. Tab cycles tree → Favorites → playlist (Shift+Tab reverse; the active pane shows a brighter fade at the top). On the tree, ←→ expands or collapses folders (player mode does not shuttle with the arrow keys). Enter plays / restarts from the beginning. The numpad (NumLock on) works from any pane: 0 play / pause, 1 rewind, 3 fast-forward (−4 dB while held), 4 previous track, 5 restart, 6 next track, 7 / 9 skip 5 s back / forward with a 0.75 s crossfade (hold repeats after a short wait). Top-row 0–9 still jump by view percent. The waveform only seeks and selects (no right-click menu; no zoom; display-resolution peaks only; short files stay dense; spectrogram / loudness-curve views stay off). Edit commands are blocked. Files from other modes are listed. Leaving stops playback first, then keeps only the selected files. Quitting while still in player mode does not remember the loaded files. Window position and size are remembered separately from editor mode and restored when you switch. PCM is not scanned for every file; tags and duration are read on register. Playlist columns are chosen in Settings → Shown (remembered). The tree on the left starts at Music and remembers the last folder and the split with Favorites; selecting a folder does not add it to the list. Enter on the tree clears the playlist, then adds that folder recursively. Shift+Enter, double-click, or dropping a folder onto the list, adds them recursively. MP3 / M4A / WAVE / AIFF stream without a full PCM decode (M4A is player-only). Jacket images load on select; a No Image placeholder is used when none is embedded. WAAPI is off (if it was on, leaving player turns it back on). If any launch argument is an MP3 or M4A, the app starts in player mode with those files listed. F10 again restores.");
    public static string TipAlwaysOnTop => Get(
        "ウィンドウを常に最前面へ表示します。",
        "Keep the window always on top.");
    public static string TipSilentSkip => Get(
        "無音区間を飛ばして再生し、録音中はしきい値を超えたときだけ書き込みます (Alt+S)。再生中の ←→ 早送り／巻き戻し中は飛ばしません（プレイヤーではテンキー 1／3）。録音開始時にオンにするか毎回確認する。しきい値（無音削除にも使う）、スキップ時の無音挿入時間、プチノイズ防止フェードは設定の編集タブ。リージョン付加は録音開始時に指定。",
        "Skip silent stretches during playback, and while recording write only audio above the threshold (Alt+S). Hold ←→ during playback to shuttle without skipping (numpad 1 / 3 in player mode). Recording always asks whether to turn it on. The threshold (also used by Delete Silence), the silence insert duration on skip, and the click-prevention fade are on the Editing tab in Settings. Adding regions is chosen when you start recording.");
    public static string TipSilentSkipThreshold => Get(
        "Silent Skip で無音とみなすピーク（dBFS）。-120 から 0。既定 -60。再生ではこの値未満なら次の音まで飛ばし、録音では書き込みません。判定は約 50ms の窓内ピークです。しきい値付近の周期音の谷だけが抜けて、ピッチが微妙に変わってしまう不具合を直しています。右クリックの「無音部分を削除」も同じしきい値です。右のバーは割り当てた録音をモノラル化したピーク。赤線と数字は短区間ピークの最小の平均です。",
        "Peak level treated as silence for Silent Skip (dBFS), from −120 to 0. Default −60. Playback jumps from below this level to the next sound. Recording does not write those frames. The check uses a peak in a ~50 ms window. That fixes a bug where troughs of a near-threshold tone were stripped and the pitch drifted slightly. Delete Silence on the right-click menu uses the same threshold. The bar is the assigned record ports mixed to mono. The red line and number are the average of short-block peak minima.");
    public static string TipSilentSkipThresholdMeter => Get(
        "バーはピーク。赤線と右の数値はピークではなく、短い区間のピークのうち小さい方をならした値（谷の平均）。しきい値の目安にします。",
        "The bar is peak. The red line and number are not peak; they average the quieter short-block peaks (avg. floor). Use them as a guide for the threshold.");
    public static string TipSilentSkipRecordPad => Get(
        "録音の Silent Skip 専用。スキップ時に挿入する無音の長さ。しきい値を下回った時点から無音を書き、この長さまで書いたら止めます。音が戻ったときにまとめて挟みません。先頭の無音と、末尾の長い無音は残しません。0 なら無音は全部捨てます。0 から 10000。既定 500。",
        "Recording Silent Skip only. How much silence is inserted when skipping. Silence is written as soon as the level falls, then stops at this length. It is not dumped when sound returns. Leading silence and a long tail are discarded. 0 drops every silent frame. From 0 to 10000. Default 500.");
    public static string TipClickGuardFade => Get(
        "継ぎ目のプチノイズだけを消す短いフェード。先頭は 0 から、終端は 0 へ。リージョン毎のノーマライズ、無音部分を削除、Silent Skip 録音で使う。長い音楽用フェードにはしない。1 から 100。既定 20。",
        "A short fade that only removes a click at a splice. Starts at 0 and ends at 0. Used by normalize per region, Delete Silence, and Silent Skip recording. Not a musical fade. From 1 to 100. Default 20.");
    public static string TipConfirmRecordSilentSkip => Get(
        "この録音で Silent Skip を使うか。オンならしきい値を超えたときだけ録り、下回った時点から無音を挟んで挿入時間で止めます。オフならすべて書きます。OK するとステータスバーにも反映します。",
        "Whether to use Silent Skip for this recording. When on, only audio above the threshold is recorded, and silence is inserted when the level falls (up to the insert duration in Settings). When off, everything is written. OK also updates the status-bar switch.");
    public static string TipSilentSkipRecordAddRegion => Get(
        "Silent Skip 録音のときだけ。無音を pad まで挟んだとき、前後の録音それぞれにリージョンを付けます。無音には付けません。波形の短い谷では分けません。続きの録音は新しい区間だけ。録音のたびに指定。前回の指定を初期値にする。",
        "Only when recording with Silent Skip. A region is added to each recorded stretch separated by a pad. Silence does not get a region. Brief dips in the waveform are not split. A continued take gets regions for the new audio only. Chosen each time you record. The last choice is the default.");
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

    public static string TabMenuRenameFile => Get("ファイル名を変更(_N)", "Re_name File");
    public static string TabMenuDuplicateFile => WaveMenuDuplicateFile;
    public static string TabMenuDeleteFile => WaveMenuDeleteFile;
    public static string TabMenuCloseThis => Get("このタブを閉じる(_S)", "Close Thi_s Tab");
    public static string TabMenuCloseOthers => Get("このタブ以外を閉じる(_O)", "Close _Other Tabs");
    public static string TabMenuCloseRight => Get("このタブを含め右側を全部閉じる(_R)", "Close This and Tabs to the _Right");
    public static string TabMenuCloseLeft => Get("このタブを含め左側を全部閉じる(_L)", "Close This and Tabs to the _Left");
    public static string TabMenuSelectAll => Get("全部のタブを選択する(_A)", "Select _All Tabs");
    public static string TileSearchLabel => Get("検索", "Find");
    public static string TileSearchSyntaxHint => Get(
        "空白区切り = AND、| = OR（例: bgm loop|se）\nEnter で確定。Alt+Enter でヒット以外を閉じる（未保存は残す。確認なし。フィルターは解除）。",
        "Space = AND, | = OR (e.g. bgm loop|se)\nEnter confirms. Alt+Enter closes tiles that did not match (keeps unsaved; no prompt; clears the filter).");
    public static string TipTileSearch => Get(
        "タブ名で絞り込みます。空白区切りで AND、| で OR。"
        + "Enter で確定、Alt+Enter でヒット以外を閉じる（未保存は残す。確認は出さない。フィルターは解除）。"
        + "Esc でキャンセル、空欄にすると解除します。",
        "Filter tiles by tab name. Space-separated terms = AND, | = OR."
        + " Enter confirms. Alt+Enter closes tiles that did not match (keeps unsaved; no prompt; clears the filter)."
        + " Esc cancels, clear the text to remove the filter.");
    public static string TabMenuTileOff => WaveMenuTileOff;
    public static string TabMenuTileHorizontal => WaveMenuTileHorizontal;
    public static string TabMenuTileVertical => WaveMenuTileVertical;
    public static string TabMenuTileGrid => WaveMenuTileGrid;
    public static string TabMenuCopyAllTimes => WaveMenuCopyAllTabTimes;
    public static string TabMenuCopySelectedTimes => WaveMenuCopySelectedTabTimes;
    public static string TabTimeWindowTitle => Get("ファイルの時間", "File times");
    public static string TabTimeColumnFile => Get("ファイル", "File");
    public static string TabTimeColumnTime => Get("時間", "Time");
    public static string LibraryColumnName => Get("ファイル", "File");
    public static string LibraryColumnTitle => Get("タイトル", "Title");
    public static string LibraryColumnArtist => Get("アーティスト", "Artist");
    public static string LibraryColumnAlbumArtist => Get("アルバムアーティスト", "Album artist");
    public static string LibraryColumnAlbum => Get("アルバム", "Album");
    public static string LibraryColumnTrack => Get("トラック", "Track");
    public static string LibraryColumnDisc => Get("ディスク", "Disc");
    public static string LibraryColumnYear => Get("年", "Year");
    public static string LibraryColumnGenre => Get("ジャンル", "Genre");
    public static string LibraryColumnComposer => Get("作曲", "Composer");
    public static string LibraryColumnComment => Get("コメント", "Comment");
    public static string LibraryColumnKind => Get("種類", "Kind");
    public static string LibraryColumnDuration => Get("時間", "Time");
    public static string LibraryColumnSampleRate => Get("レート", "Rate");
    public static string LibraryColumnBitDepth => Get("ビット", "Bits");
    public static string LibraryColumnChannels => Get("Ch", "Ch");
    public static string LibraryColumnBitRate => Get("ビットレート", "Bit rate");
    public static string LibraryColumnSize => Get("サイズ", "Size");
    public static string LibraryColumnFolder => Get("フォルダ", "Folder");
    public static string LibraryColumnJacket => Get("ジャケット", "Jacket");
    public static string LibraryColumnLabel(LibraryFileColumn column) => column switch
    {
        LibraryFileColumn.Title => LibraryColumnTitle,
        LibraryFileColumn.Artist => LibraryColumnArtist,
        LibraryFileColumn.AlbumArtist => LibraryColumnAlbumArtist,
        LibraryFileColumn.Album => LibraryColumnAlbum,
        LibraryFileColumn.Track => LibraryColumnTrack,
        LibraryFileColumn.Disc => LibraryColumnDisc,
        LibraryFileColumn.Year => LibraryColumnYear,
        LibraryFileColumn.Genre => LibraryColumnGenre,
        LibraryFileColumn.Composer => LibraryColumnComposer,
        LibraryFileColumn.Comment => LibraryColumnComment,
        LibraryFileColumn.Kind => LibraryColumnKind,
        LibraryFileColumn.Duration => LibraryColumnDuration,
        LibraryFileColumn.SampleRate => LibraryColumnSampleRate,
        LibraryFileColumn.BitDepth => LibraryColumnBitDepth,
        LibraryFileColumn.Channels => LibraryColumnChannels,
        LibraryFileColumn.BitRate => LibraryColumnBitRate,
        LibraryFileColumn.Size => LibraryColumnSize,
        LibraryFileColumn.Folder => LibraryColumnFolder,
        LibraryFileColumn.Jacket => LibraryColumnJacket,
        _ => LibraryColumnName,
    };
    public static string LibraryGroupLabel => Get("グループ", "Group");
    public static string LibraryExplorerLabel => Get("ライブラリ", "Library");
    public static string LibraryPlaylistLabel => Get("プレイリスト", "Playlist");
    public static string LibraryExplorerDesktop => Get("デスクトップ", "Desktop");
    public static string LibraryExplorerDocuments => Get("ドキュメント", "Documents");
    public static string LibraryExplorerMusic => Get("ミュージック", "Music");
    public static string LibraryExplorerPictures => Get("ピクチャ", "Pictures");
    public static string LibraryExplorerVideos => Get("ビデオ", "Videos");
    public static string LibraryFavoritesLabel => Get("お気に入り", "Favorites");
    public static string LibraryMenuAddToFavorites => Get("お気に入りへ追加", "Add to Favorites");
    public static string LibraryMenuReplacePlaylist => Get("プレイリストをクリアして追加", "Clear Playlist and Add");
    public static string LibraryMenuAppendPlaylist => Get("プレイリストへ追加", "Add to Playlist");
    public static string LibraryMenuRemoveFromFavorites => Get("お気に入りから削除", "Remove from Favorites");
    public static string LibraryMenuClearFromPlaylist => Get("プレイリストからクリア", "Clear from Playlist");
    public static string LibraryGroupNone => Get("なし", "None");
    public static string LibraryGroupTitle => Get("タイトル", "Title");
    public static string LibraryGroupArtist => Get("アーティスト", "Artist");
    public static string LibraryGroupAlbum => Get("アルバム", "Album");
    public static string LibraryGroupGenre => Get("ジャンル", "Genre");
    public static string LibraryGroupYear => Get("年", "Year");
    public static string LibraryGroupKind => Get("種類", "Kind");
    public static string LibraryGroupSampleRate => Get("レート", "Sample rate");
    public static string LibraryGroupBitDepth => Get("ビット深度", "Bit depth");
    public static string LibraryGroupChannels => Get("チャンネル", "Channels");
    public static string LibraryGroupFolder => Get("フォルダ", "Folder");
    public static string LibraryGroupUntitled => Get("(未保存)", "(Untitled)");
    public static string LibraryGroupBlank => Get("(なし)", "(None)");
    public static string LibraryJacketMark => "✓";
    public static string FormatBitRate(int kbps) =>
        kbps <= 0 ? string.Empty : $"{kbps} kbps";
    public static string TipLibraryList => Get(
        "プレイヤーのファイル一覧。左のツリーでフォルダを選んでもリストへは自動で載せない。フォルダをリストへドロップすると配下ごと追加する。Delete または右クリック／メニューキー「プレイリストからクリア」で選択行をリストから外す（ファイルは消さない。Ctrl+W では閉じない）。ソート中の列は見出しに小さい ▼▲ を出し、有効な方向だけシアン。既定はアルバムでグループし、グループごとにジャケットを出す。グループでタイトル／アーティスト／アルバムなどでもまとめる。入ると1曲めを選んで再生する（引数起動も同じ）。再生開始と同時に次の曲を先読みし、終わったら隙間なく次を再生する。最後の次は先頭に戻って再生を続ける。↑↓ でファイル選択。Home／End でリストの先頭／末尾。PageUp／PageDown でページ送り（再生中はキーを離すとその曲へ。停止中は選択だけ。Shift で範囲、Ctrl+クリックで追加、Ctrl+A で全選択）。F1 でツリー、F2 でお気に入り、F3 でこのリストにフォーカス。Tab でツリー→お気に入り→プレイリスト（Shift+Tab は逆。フォーカス中は上端に明るいグラデーション）。停止中はクリックしてもカーソルで選んでも再生しない。再生中にクリックした行はその曲へ切り替える。ダブルクリック、Enter で再生／先頭から再生し直し。Space で再生／停止。テンキーはどのペインからでも（NumLock オン）0＝再生／一時停止、1＝早戻し、3＝早送り（推移中は-4dB）、4＝前の曲、5＝再生し直し、6＝次の曲、7／9＝5秒戻し／送り（0.75秒クロスフェード。押しっぱなしは少し待ってから繰り返す）。左右キーの早送り／巻き戻しはしない。キーボード上段の 0–9 は割合ジャンプのまま。編集はできない。タグと時間は登録時に読む。MP3／M4A／WAVE／AIFF はフル PCM 展開せずストリーム再生（M4A はプレイヤー専用）。ジャケットは選択時に読む。埋め込みが無ければ No Image のプレースホルダ。WAVE 等は差し替え不可。WAAPI は無効（入る前にオンなら、抜けたときに戻す）。プレイヤーを抜けると再生を止めてから、選択したファイルだけ残る。プレイヤーのまま終了したときは、読み込んだファイルは覚えない。",
        "Player file list. Selecting a folder in the tree on the left does not add it to the list. Drop a folder onto the list to add recursively. Delete, right-click, or the menu key Clear from Playlist removes selected rows from the list (files are never deleted. Ctrl+W does not close). The sorted column shows small ▼▲ in the header, and only the active direction is cyan. Groups default to album, with a jacket beside each group. Group by title, artist, album, and so on. Entering selects and plays the first track (the same on launch with arguments). The next track is read ahead as soon as playback starts, then plays without a gap. After the last, playback returns to the first and keeps going. ↑↓ select files. Home / End jump to the first / last track. Page Up / Page Down page the list (while playing, releasing the key switches to that track; while stopped, selection only. Shift for a range, Ctrl+click to add, Ctrl+A for all). F1 focuses the tree, F2 Favorites, F3 this list. Tab cycles tree → Favorites → playlist (Shift+Tab reverse; the active pane shows a brighter fade at the top). While stopped, a click or cursor selection does not start playback. While playing, a click switches to that track. Double-click or Enter plays / restarts from the beginning. Space toggles play / stop. The numpad (NumLock on) works from any pane: 0 play / pause, 1 rewind, 3 fast-forward (−4 dB while held), 4 previous track, 5 restart, 6 next track, 7 / 9 skip 5 s back / forward with a 0.75 s crossfade (hold repeats after a short wait). Arrow keys do not shuttle. Top-row 0–9 still jump by view percent. Editing is blocked. Tags and duration are read when files are listed. MP3 / M4A / WAVE / AIFF stream without a full PCM decode (M4A is player-only). Jacket images load on select; a No Image placeholder is used when none is embedded. WAVE and similar cannot replace art. WAAPI is off (if it was on, leaving player turns it back on). Leaving player mode stops playback first, then keeps only the selected files. Quitting while still in player mode does not remember the loaded files.");
    public static string TipLibraryJacket => Get(
        "本物のジャケットがあるときは、ツリー・お気に入り・プレイリストの背面に縦横比を崩して置き、大きくぼかして90度ずつ4向きをゆっくりクロスフェードしながら揺らぐ。ジャケットが変わったときは1秒でクロスフェードする。同じ画像のまま曲を変えても、フェードも向きの切り替えもやり直さない。埋め込みが無ければ既定の背景色。",
        "When a real jacket is set, it is stretched across the tree, Favorites, and playlist (aspect may change) and used as a heavily blurred background that slowly crossfades through four 90-degree turns while drifting. A new jacket crossfades in 1 second. Skipping tracks that keep the same jacket does not restart the fade or the turn cycle. Fallback color when none is embedded.");
    public static string TipLibraryColumns => Get(
        "設定の表示項目で、リストに出す列を選ぶ。チェックした列だけ表示。状態は次の起動でも覚える。ファイル名の列は外せない。",
        "Choose list columns in Settings → Shown. Only checked columns appear. The choice is remembered. The file name column stays on.");
    public static string TipLibraryExplorer => Get(
        "フォルダツリー。ルートは設定のプレイヤーで複数登録できる（既定はマイミュージック。上から順。登録・削除・並び替え）。選んだ場所・展開状況と、お気に入りとの仕切り位置は覚える。Ctrl+クリックで複数フォルダを選択。右クリックまたはメニューキーで、プレイリストをクリアして追加、プレイリストへ追加、お気に入りへ追加。フォルダを選んでもリストへは自動で載せない。フォルダをリストへドロップすると配下ごと追加する。ダブルクリックは配下を再帰的に追加する。Enter はプレイリストをクリアしてから、そのフォルダ配下を再帰的に載せる（深さ優先で最初に登録した曲から再生）。Shift+Enter はクリアせず追加する（フォルダを見つけ次第、1曲ずつ。裏で集める）。* は選択フォルダ以下をすべて展開、/ は選択フォルダ以下を閉じる（エクスプローラーと同じ）。F1 でこのツリーにフォーカス。Tab でお気に入りへ（Shift+Tab はプレイリスト。フォーカス中は上端に明るいグラデーション）。←→ はフォルダの展開と折りたたみ（早送り／巻き戻しにはならない）。テンキーの再生操作はツリーにフォーカスがあっても効く。コピー／複製／削除／リネームなどファイル操作はできない。",
        "Folder tree. Roots can be registered in Settings → Player (default Music, top to bottom; add, remove, reorder). The last place, which folders were expanded, and the split with Favorites, are remembered. Ctrl+click selects multiple folders. Right-click or the menu key can clear the playlist and add, add to the playlist, or add to Favorites. Selecting a folder does not add it to the list. Drop a folder onto the list to add recursively. Double-click appends files from that folder and its subfolders. Enter clears the playlist, then adds that folder recursively (depth-first; playback starts with the first file registered). Shift+Enter appends only (as each folder is found, tracks are added one by one in the background). * expands all under the selection, / collapses it (same as Explorer). F1 focuses this tree. Tab moves to Favorites (Shift+Tab to the playlist; the active pane shows a brighter fade at the top). ←→ expands or collapses folders (not shuttle). Numpad playback keys still work while the tree has focus. File operations such as copy, duplicate, delete, and rename are not available.");
    public static string TipLibraryFavorites => Get(
        "お気に入り。名前だけ表示（フルパスは出さない）。複数選択可。プレイリストへドラッグで追加。Enter でプレイリストをクリアして追加し即再生。Shift+Enter は追加のみ。Delete、右クリック、またはメニューキーで登録を外す（ファイルは消さない）。F2 でここへフォーカス。Tab でプレイリストへ（Shift+Tab はツリー。フォーカス中は上端に明るいグラデーション）。テンキーの再生操作はここでも効く。",
        "Favorites. Names only (no full path). Multi-select. Drag onto the playlist to add. Enter clears the playlist, adds, and plays. Shift+Enter appends only. Delete, right-click, or the menu key removes the registration (files are never deleted). F2 focuses here. Tab moves to the playlist (Shift+Tab to the tree; the active pane shows a brighter fade at the top). Numpad playback keys still work here.");
    public static string TabTimeCopy => Get("コピー", "Copy");
    public static string TabTimeSaveCsv => Get("CSV", "CSV");
    public static string TabTimeSavePdf => Get("PDF", "PDF");
    public static string TabTimeSaveCsvTitle => Get("CSV として保存", "Save CSV");
    public static string TabTimeSavePdfTitle => Get("PDF として保存", "Save PDF");
    public static string TabTimeFileNameCsv => Get("ファイルの時間.csv", "FileTimes.csv");
    public static string TabTimeFileNamePdf => Get("ファイルの時間.pdf", "FileTimes.pdf");
    public static string TipTabTimeCopy => Get(
        "選択したセルをコピーします。未選択なら表全体（見出し付き）。Ctrl+C でもコピー。ドラッグで範囲選択。",
        "Copy selected cells. Copies the whole table with headers if nothing is selected. Ctrl+C also copies. Drag to select a range.");
    public static string TipTabTimeCsv => Get(
        "表全体を CSV（UTF-8、Excel 向け BOM 付き）で保存します。",
        "Save the whole table as CSV (UTF-8 with BOM for Excel).");
    public static string TipTabTimePdf => Get(
        "表全体を PDF で保存します。",
        "Save the whole table as PDF.");
    public static string TabMenuCloseAll => Get("すべてのタブを閉じる(_A)", "Close _All Tabs");

    /// <summary>通常メニュー用。「全部のタブを選択する(_A)」とアクセスキーが重ならないよう W。</summary>
    public static string TabMenuCloseAllNormal => Get("すべてのタブを閉じる(_W)", "Close All Tabs (_W)");
    public static string TabMenuPasteToAll => Get("編集データを全てにペーストする(_V)", "Paste Copied Edits to All Tabs (_V)");
    public static string TabMenuCloseSelected => Get("選択したタブを閉じる(_A)", "Close Selected Tabs (_A)");
    public static string TabMenuPasteToSelected => Get("選択したタブにペーストする(_V)", "Paste Copied Edits to Selected Tabs (_V)");
    /// <summary>アクセスキーは B が「タブのまま(B)」と重なるため J のまま。</summary>
    public static string TabMenuMergeSelected => WaveMenuMergeTabs;
    public static string TabMenuExportWave => Get("Wave で書き出す(_E)", "Export _Wave");
    public static string TabMenuExportMp3 => Get("MP3 で書き出す(_M)", "Export _MP3");
    public static string TabMenuExportWaveSelected => Get("選択したタブを Wave で書き出す(_E)", "Export Selected Tabs as _Wave");
    public static string TabMenuExportMp3Selected => WaveMenuExportMp3Selected;
    public static string TabMenuExportWaveAll => Get("選択ファイルを Wave で保存(_E)", "Save Selected Files as _Wave");
    public static string TabMenuExportMp3All => Get("選択ファイルを MP3 で保存(_M)", "Save Selected Files as _MP3");
    public static string TabMenuExportWaveByMarkers => Get(
        "マーカーでセパレートして書き出す(_K)",
        "Export Wave Separated by Mar_kers");
    public static string TabMenuExportWaveByMarkersSelected => Get(
        "選択したタブをマーカーでセパレートして書き出す(_K)",
        "Export Selected Tabs Separated by Mar_kers");
    public static string TabMenuExportWaveByMarkersAll => Get(
        "選択ファイルをマーカーでセパレートして書き出す(_K)",
        "Export Selected Files Separated by Mar_kers");
    public static string TabMenuExportWaveByRegions => Get(
        "リージョンでセパレートして書き出す(_G)",
        "Export Wave Separated by Re_gions");
    public static string TabMenuExportWaveByRegionsSelected => Get(
        "選択したタブをリージョンでセパレートして書き出す(_G)",
        "Export Selected Tabs Separated by Re_gions");
    public static string TabMenuExportWaveByRegionsAll => Get(
        "選択ファイルをリージョンでセパレートして書き出す(_G)",
        "Export Selected Files Separated by Re_gions");
    public static string TabMenuExportByChannels => Get(
        "チャンネルごとに書き出す(_C)",
        "Export by _Channel");
    public static string TabMenuExportByChannelsSelected => WaveMenuExportByChannelsSelected;
    public static string TabMenuExportByChannelsAll => Get(
        "選択ファイルをチャンネルごとに書き出す(_C)",
        "Export Selected Files by _Channel");

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

    public static string TabBatchEditSkippedAll => Get(
        "選択したファイルには適用できませんでした",
        "The edit could not be applied to any of the selected files");

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
        "Fade Around Playhead" => Get("シークバー前後フェード", "Fade Around Seek Bar"),
        "Normalize" => Get("ノーマライズ", "Normalize"),
        "Normalize Per Region" => Get("リージョン毎にノーマライズ", "Normalize per Region"),
        "Volume" => Get("音量", "Volume"),
        "Pitch Shift" => Get("ピッチ", "Pitch Shift"),
        "Time Stretch" => Get("タイムストレッチ", "Time Stretch"),
        "Reverse" => Get("リバース", "Reverse"),
        "Delete" => Get("削除", "Delete"),
        "Delete Silence" => Get("無音部分を削除", "Delete Silence"),
        "Paste" => Get("ペースト", "Paste"),
        "Record" => Get("録音", "Record"),
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

    public static string FormatFileBytesCompact(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (bytes < 1000)
        {
            return string.Create(culture, $"{bytes} B");
        }

        if (bytes < 1_000_000)
        {
            return string.Create(culture, $"{bytes / 1000d:0.0} KB");
        }

        return string.Create(culture, $"{bytes / 1_000_000d:0.00} MB");
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
