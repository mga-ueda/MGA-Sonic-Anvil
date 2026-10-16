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
        + " / " + CopyrightMitLink + " / " + CopyrightLameLink + " / " + CopyrightFfmpegLink
        + "\n" + CopyrightWwiseLine;

    public const string CopyrightGitHub = "GitHub";

    public const string CopyrightWwiseLine =
        "Wwise® and Audiokinetic® are trademarks of Audiokinetic Inc.";

    public const string CopyrightMitLink = "MIT License";

    public const string CopyrightLameLink = "LAME";

    public const string CopyrightFfmpegLink = "FFmpeg";

    public static string UntitledDocument => Get("untitled", "untitled");

    public static string LabelAlwaysOnTop => Get("Always on Top", "Always on Top");
    public static string LabelSilentSkip => Get("Silent Skip", "Silent Skip");
    public static string LabelSilentSkipThreshold => Get(
        "無音しきい値（Silent Skip）",
        "Silence threshold (Silent Skip)");
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
    public static string LabelSettingsTabExport => Get("書き出し・パス", "Export · Paths");
    public static string LabelSettingsTabWwise => Get("Wwise", "Wwise");
    public static string LabelLibraryExplorerRoots => Get("ツリーのルート", "Tree roots");
    public static string LabelLibraryListColumns => Get("プレイリストの列", "Playlist columns");
    public static string LabelLibraryListColumnsWave => Get("WAVE / AIFF のとき", "For WAVE / AIFF");
    public static string LabelLibraryListColumnsMp3 => Get("MP3 のとき", "For MP3");
    public static string LabelLibraryListColumnsVideo => Get("動画のとき", "For video");
    public static string LabelLibraryListColumnsReset => Get("既定に戻す", "Reset to defaults");
    public static string LabelLibraryPlaylistWaveformSize => Get("波形表示の幅", "Waveform width");
    public static string LabelLibraryPlaylistWaveformSizeS => Get("S（短）", "S (short)");
    public static string LabelLibraryPlaylistWaveformSizeM => Get("M（中）", "M (medium)");
    public static string LabelLibraryPlaylistWaveformSizeL => Get("L（長）", "L (long)");
    public static string LabelGaplessPlayback => Get("ギャップレス再生", "Gapless playback");
    public static string LabelLibraryPlaylistDocuments => Get(
        "プレイリストに載せる書類・動画",
        "Documents and video in the playlist");
    public static string LabelLibraryShowPlaylistPdf => Get("PDF", "PDF");
    public static string LabelLibraryShowPlaylistMov => Get("MOV", "MOV");
    public static string LabelLibraryShowPlaylistMp4 => Get("MP4", "MP4");
    public static string LabelLibraryShowPlaylistAvi => Get("AVI", "AVI");
    public static string LabelLibraryShowPlaylistMkv => Get("MKV", "MKV");
    public static string LabelLibraryShowPlaylistWebm => Get("WebM", "WebM");
    public static string LabelLibraryShowPlaylistMpg => Get("MPG", "MPG");
    public static string LabelVideoProxyRetention => Get(
        "未使用プロキシの保存日数",
        "Unused proxy retention");
    public static string LabelVideoProxyRetentionDays(int days) =>
        days == 1
            ? Get("1 日", "1 day")
            : Format("{0} 日", "{0} days", days);
    public static string LabelFfmpegPath => Get("FFmpeg のパス", "FFmpeg Path");
    public static string FilterFfmpegExe => Get(
        "FFmpeg|ffmpeg.exe|実行ファイル|*.exe|すべて|*.*",
        "FFmpeg|ffmpeg.exe|Executable|*.exe|All|*.*");
    public static string LabelVideoProxyDisableAutoEncode => Get(
        "プロキシを自動生成しない",
        "Do not auto-build proxies");
    public static string LabelVideoProxyUsage(long bytes) =>
        Format("現在の容量: {0}", "Current size: {0}", FormatFileBytesCompact(bytes));
    public static string ButtonVideoProxyClearAll => Get("すべて削除", "Delete all");
    public static string ConfirmVideoProxyClearAll => Get(
        "保存中の動画プロキシをすべて削除しますか？ 期限は問いません。",
        "Delete every stored video proxy now, ignoring retention?");
    public static string InfoVideoProxyCleared(int count) => count <= 0
        ? Get("削除するプロキシはありません。", "There are no proxies to delete.")
        : Format("{0} 件削除しました。", "Deleted {0} file(s).", count);
    public static string ButtonLibraryExplorerRootAdd => Get("登録…", "Add…");
    public static string TitleLibraryExplorerRootAdd => Get("フォルダを追加", "Add folder");
    public static string ButtonLibraryExplorerRootRemove => Get("削除", "Remove");
    public static string ButtonLibraryExplorerRootUp => Get("上へ", "Up");
    public static string ButtonLibraryExplorerRootDown => Get("下へ", "Down");
    public static string LabelLibraryCatalog => Get("ライブラリ索引", "Library catalog");
    public static string LabelLibraryCatalogUsage(long bytes) =>
        Format("現在の容量: {0}", "Current size: {0}", FormatFileBytesCompact(bytes));
    public static string ButtonLibraryCatalogClear => Get("Clear", "Clear");
    public static string ConfirmLibraryCatalogClear => Get(
        "ライブラリ索引を消去しますか？ タグやビットレートなどの検索は、次に Ctrl+F5 で構築するまで当たりません。",
        "Clear the library catalog? Searches for tags, bit rate, and the other playlist columns will miss files until the next Ctrl+F5 build.");
    public static string InfoLibraryCatalogCleared => Get(
        "ライブラリ索引を消去しました。",
        "Cleared the library catalog.");
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
    public static string ButtonClose => Get("Close", "Close");
    public static string ButtonYes => Get("Yes", "Yes");
    public static string ButtonNo => Get("No", "No");
    public static string ButtonYesNoCancel => Get("Cancel", "Cancel");
    public static string ButtonSaveAllAndClose => Get("すべて保存して閉じる", "Save all and close");
    public static string ButtonDiscardAllAndClose => Get("すべて保存せずに閉じる", "Close without saving any");
    public static string ConfirmRecord => Get("録音を開始します。", "Start recording.");
    public static string ConfirmRecordSilentSkip(double thresholdDb, int padMs) => Format(
        "Silent Skip をオンにすると、ピークが {0} dB を超えたときだけ録音します。しきい値を下回るとすぐ無音を挟み、{1} ms で止めます。しきい値と無音の尺は設定から変えられます。",
        "When Silent Skip is on, recording writes only audio above {0} dB. When the level falls, silence is inserted immediately and stops at {1} ms. The threshold and silence length can be changed in Settings.",
        thresholdDb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
        padMs);
    public static string DialogSettingsTitle => Get("設定", "Settings");
    public static string LabelUiLanguage => Get("言語", "Language");
    public static string LabelUiScale => Get("表示倍率", "UI scale");
    public static string LabelMultiFileArrange => Get("複数ファイル", "Multiple files");
    public static string LabelMultiFileArrangeTabs => Get("タブのまま", "Keep tabs");
    public static string LabelMultiFileArrangeHorizontal => Get("左右", "Side by side");
    public static string LabelMultiFileArrangeVertical => Get("上下", "Stacked");
    public static string LabelMultiFileArrangeGrid => Get("上下左右", "Grid");
    public static string LabelDefaultAudioFormat => Get("既定 Wave フォーマット", "Default Wave format");
    public static string LabelDefaultSampleRate => Get("サンプルレート", "Sample rate");
    public static string LabelDefaultBitDepth => Get("ビット深度", "Bit depth");
    public static string LabelDefaultChannelLayout => Get("チャンネル", "Channels");
    public static string LabelFileAssociations => Get("関連付け", "File associations");
    public static string LabelFileAssociationsAudio => Get("オーディオ", "Audio");
    public static string LabelFileAssociationsVideo => Get("ビデオ", "Video");
    public static string LabelLanguageAuto => Get("Auto", "Auto");
    public static string LabelLanguageJapanese => Get("Japanese", "Japanese");
    public static string LabelLanguageEnglish => Get("English", "English");
    public static string LabelFadeCurveDefaults => Get("フェードカーブ既定", "Default Fade Curves");
    public static string LabelDefaultFadeIn => Get("波形フェードイン", "Waveform Fade In");
    public static string LabelDefaultFadeOut => Get("波形フェードアウト", "Waveform Fade Out");
    public static string DialogNewDocumentTitle => Get("新規", "New");
    public static string DialogSilenceDetectTitle => Get("無音検出", "Detect Silence");
    public static string LabelSilenceDetectOpenThreshold => Get("オープンスレッショルド", "Open Threshold");
    public static string LabelSilenceDetectCloseThreshold => Get("クローズスレッショルド", "Close Threshold");
    public static string LabelSilenceDetectLink => Get("リンク", "Link");
    public static string LabelSilenceDetectMinGate => Get("次のゲートまでの最小時間", "Minimum Time to Next Gate");
    public static string LabelSilenceDetectMinClose => Get("クローズまでの最小時間", "Minimum Time to Close");
    public static string LabelSilenceDetectPreRoll => Get("プリロール", "Pre-Roll");
    public static string LabelSilenceDetectPostRoll => Get("ポストロール", "Post-Roll");
    public static string LabelSilenceDetectFadeIn => Get("カット時フェードイン", "Cut Fade In");
    public static string LabelSilenceDetectFadeOut => Get("カット時フェードアウト", "Cut Fade Out");
    public static string LabelSilenceDetectTargetSelection => Get("対象: 選択範囲", "Target: selection");
    public static string LabelSilenceDetectTargetEntire => Get("対象: ファイル全体", "Target: entire file");
    public static string LabelSilenceDetectEdgesOnly => Get("冒頭＆末尾のみ", "Start & end only");
    public static string LabelSilenceDetectEntire => Get("全体", "Entire");
    public static string LabelSilenceDetectDelete => Get("無音を削除", "Delete silence");
    public static string LabelSilenceDetectCreateRegions => Get("音があるところへリージョン付加", "Add regions to audio");
    public static string LabelSilenceDetectCreateMarkers => Get("音があるところへマーカー付与", "Add markers to audio");
    public static string ButtonSilenceDetectDelete => Get("削除", "Delete");
    public static string ButtonSilenceDetectCreateRegions => Get("付加", "Add");
    public static string ButtonSilenceDetectCreateMarkers => Get("付与", "Add");
    public static string ConfirmSilenceDetectSelectedFiles(int count) => Format(
        "選択した {0} 個のファイルに、現在の無音検出設定を適用しますか？",
        "Apply the current Detect Silence settings to the {0} selected files?",
        count);
    public static string SilenceDetectDetectedSpans(int count) => Format(
        "{0}のリージョンを検出",
        "{0} region(s) detected",
        count);
    public static string ErrorNoSilenceDetectSpans => Get(
        "検出できる区間がありません。",
        "No spans were detected.");
    public static string LabelSettingsExportAudio => Get("オーディオ", "Audio");
    public static string LabelSettingsExportMovie => Get("ムービー", "Movie");
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
    public static string SaveFolderTitle => Get("保存先フォルダ", "Save folder");
    public static string ConfirmOverwriteFiles(int count, string list) => Format(
        "既にあるファイルが {0} 件あります。上書きしますか？{1}{1}{2}{1}{1}Yes = 上書き、No = それらをスキップ、Cancel = 中止",
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

    public static string LabelMono => Get("Mono", "Mono");
    public static string LabelStereo => Get("Stereo", "Stereo");
    public static string LabelHertz => Get("Hz", "Hz");
    public static string LabelPeak => Get("Peak", "Peak");
    public static string LabelRms => Get("RMS", "RMS");
    public static string LabelVolume => Get("音量", "Volume");
    public static string LabelPitch => Get("ピッチ", "Pitch");
    public static string LabelSemitone => Get("半音", "st");
    public static string LabelPitchTimeStretch => Get("長さを保つ", "Keep length");
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
        "未保存のタブが {0} 件あります。まとめて閉じることもできます。",
        "{0} tab(s) have unsaved changes. You can close them all at once.",
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
    public static string ConfirmMatchLoudnessTarget(int count, bool saveAfter = false)
    {
        if (saveAfter)
        {
            return Format(
                "選択した {0} 個の Wave / AIFF を、次のラウドネスターゲットに揃えて保存しますか？{1}この編集は元に戻せません。",
                "Match the {0} selected Wave / AIFF file(s) to the loudness target below and save?{1}This edit cannot be undone.",
                count,
                Environment.NewLine);
        }

        return Format(
            "選択した {0} 個の Wave / AIFF を、次のラウドネスターゲットに揃えますか？",
            "Match the {0} selected Wave / AIFF file(s) to the loudness target below?",
            count);
    }
    public static string ErrorNoWaveAiffForLoudnessMatch => Get(
        "ラウドネス揃えは Wave / AIFF のみ対応です。",
        "Loudness matching is only available for Wave / AIFF.");
    public static string ErrorNoAudioForMp3Export => Get(
        "MP3 に変換できる音声ファイルがありません。",
        "No audio files are available to convert to MP3.");
    public static string ErrorAllSelectedAlreadyMp3 => Get(
        "選択中のファイルはすべて MP3 です。",
        "All selected files are already MP3.");
    public static string OverlaySampleRateConvert => Get("サンプリングレート変換", "Sample rate conversion");
    public static string OverlayPitchShift => Get("ピッチシフト", "Pitch shift");
    public static string OverlayPasteHistory => Get("編集履歴を貼り付けています", "Pasting edit history");
    public static string OverlayApplySelected => Get("選択したファイルへ適用しています", "Applying to the selected files");
    public static string OverlayMerge => Get("タブをバウンスしています", "Bouncing tabs");
    public static string OverlayOpening => Get("読み込んでいます（Esc で中断）", "Opening (Esc to stop)");
    public static string OverlayExportWave => Get("Wave を書き出しています", "Exporting Wave");
    public static string OverlayExportMp3 => Get("MP3 を書き出しています", "Exporting MP3");
    public static string OverlaySave => Get("保存しています", "Saving");
    public static string OverlayLibraryCatalog => Get(
        "ライブラリ索引を更新しています（Esc で中断）",
        "Updating library cache (Esc to stop)");
    public static string OverlayVideoProxy => Get(
        "動画プロキシを生成しています（Esc で中断）",
        "Creating video proxy (Esc to stop)");
    public static string ErrorVideoProxyFailed => Get(
        "動画プロキシを生成できませんでした。",
        "Could not create the video proxy.");
    public static string ErrorVideoProxyFailedDetail(string detail)
    {
        var text = (detail ?? string.Empty).Trim();
        return string.IsNullOrEmpty(text)
            ? ErrorVideoProxyFailed
            : Format(
                "動画プロキシを生成できませんでした。\n{0}",
                "Could not create the video proxy.\n{0}",
                text);
    }
    public static string OverlayExportCount(int finished, int total) => Format(
        "{0} / {1}",
        "{0} / {1}",
        finished,
        total);

    public static string FilterOpenAudio => Get(
        "Audio|*.wav;*.wave;*.aif;*.aiff;*.mp3|Wave|*.wav;*.wave|AIFF|*.aif;*.aiff|MP3|*.mp3|All|*.*",
        "Audio|*.wav;*.wave;*.aif;*.aiff;*.mp3|Wave|*.wav;*.wave|AIFF|*.aif;*.aiff|MP3|*.mp3|All|*.*");

    public static string FilterOpenPlayer => Get(
        "Player|*.wav;*.wave;*.aif;*.aiff;*.mp3;*.m4a;*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mpg;*.mpeg;*.pdf|Audio|*.wav;*.wave;*.aif;*.aiff;*.mp3;*.m4a|Video|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mpg;*.mpeg|PDF|*.pdf|All|*.*",
        "Player|*.wav;*.wave;*.aif;*.aiff;*.mp3;*.m4a;*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mpg;*.mpeg;*.pdf|Audio|*.wav;*.wave;*.aif;*.aiff;*.mp3;*.m4a|Video|*.mp4;*.mov;*.avi;*.mkv;*.webm;*.mpg;*.mpeg|PDF|*.pdf|All|*.*");

    public static string FilterSaveAudio => Get(
        "Wave|*.wav|MP3|*.mp3",
        "Wave|*.wav|MP3|*.mp3");

    public static string LabelTransportGroup => Get("TRANS", "TRANS");
    public static string LabelEditGroup => Get("EDIT", "EDIT");
    public static string LabelMarkerGroup => Get("MARK", "MARK");
    public static string LabelFileGroup => Get("FILE", "FILE");
    public static string LabelViewGroup => Get("VIEW", "VIEW");
    public static string LabelOtherGroup => Get("OTHER", "OTHER");

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
    public static string TooltipSave => Get("保存 (Ctrl+S)  複数選択時は選択ファイル全部", "Save (Ctrl+S)  all selected files when several tabs are selected");
    public static string TooltipSaveMp3 => Get("MP3 として保存 (Ctrl+Shift+M)", "Save as MP3 (Ctrl+Shift+M)");
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
    public static string TooltipRangeClick => Get("範囲メトロノーム (Q)", "Range metronome (Q)");
    public static string TooltipOpen => Get("開く (Ctrl+O)", "Open (Ctrl+O)");
    public static string TooltipNew => Get("新規 (Ctrl+N)", "New (Ctrl+N)");
    public static string TooltipSaveAs => Get("名前を付けて保存 (Ctrl+Shift+S)  複数選択時は保存先フォルダ", "Save As (Ctrl+Shift+S)  pick a folder when several tabs are selected");
    public static string TooltipSpectrogramView => Get("スペクトログラム / 重ね (A)  解除 (Esc / Shift+A / Shift+V)", "Spectrogram / overlay (A)  leave (Esc / Shift+A / Shift+V)");
    public static string TooltipLoudnessView => Get("ラウドネス (V)  解除 (Esc / Shift+A / Shift+V)", "Loudness (V)  leave (Esc / Shift+A / Shift+V)");
    public static string TooltipCenterPlayhead => Get("中央寄せ / センターロック (Z)", "Center / center-lock (Z)");
    public static string TooltipHistory => Get("編集履歴 (U)", "Edit history (U)");
    public static string TooltipLibraryMaximize => Get("プレイヤー (F10)", "Player (F10)");
    public static string TooltipLibraryMaximizeOff => Get("エディタ (F10)", "Editor (F10)");
    public static string TooltipAnalyzerMaximize => Get("フルスクリーン（アナライザー） (F12)", "Fullscreen with analyzers (F12)");
    public static string TooltipAnalyzerMaximizeOff => Get("アナライザー最大化を解除 (F12)", "Leave analyzer fullscreen (F12)");
    public static string TooltipColorPanel => Get("色設定 (Ctrl+Shift+C)", "Color settings (Ctrl+Shift+C)");


    public static string LabelWaveformLane(int number) =>
        Get($"Ch{number}", $"Ch{number}");
    public static string StatusInputMonitorFailed => Get(
        "録音を開けません。デバイスと API を確認してください。",
        "Could not open recording. Check the device and API.");

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


    public static string LabelFileAssociationPlayerOnly => Get(
        "プレイヤーモードのみ",
        "Player mode only");
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
    public static string MenuCopySelectionTime => WaveMenuCopySelectionTime;
    public static string MenuCopySelectionTimeSelected => WaveMenuCopySelectionTimeSelected;
    public static string MenuCopySelectionTimePath => WaveMenuCopySelectionTimePath;
    public static string MenuCopySelectionTimePathSelected => WaveMenuCopySelectionTimePathSelected;

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
        "空白区切り = AND、| = OR（例: bgm loop|se）\nEnter で確定。Alt+Enter でヒット以外を閉じる（未保存は残す。確認なし。フィルターは解除）。 Esc で取消、クリアで解除。",
        "Space = AND, | = OR (e.g. bgm loop|se)\nEnter confirms. Alt+Enter closes tiles that did not match (keeps unsaved; no prompt; clears the filter). Esc cancels, clear the text to remove the filter.");
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
    public static string LibraryColumnDate => Get("日付", "Date");
    public static string LibraryColumnFolder => Get("フォルダ", "Folder");
    public static string LibraryColumnParentFolder => Get("親フォルダ", "Parent folder");
    public static string LibraryColumnWaveform => Get("波形表示", "Waveform");
    public static string LibraryColumnJacket => Get("ジャケット", "Jacket");
    public static string LibraryColumnVideoSize => Get("解像度", "Resolution");
    public static string LibraryColumnFrameRate => Get("フレームレート", "Frame rate");
    public static string LibraryColumnVideoCodec => Get("映像コーデック", "Video codec");
    public static string LibraryColumnAudioCodec => Get("音声コーデック", "Audio codec");
    public static string LibraryColumnLoudness => Get("ラウドネス", "Loudness");
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
        LibraryFileColumn.Date => LibraryColumnDate,
        LibraryFileColumn.Folder => LibraryColumnFolder,
        LibraryFileColumn.ParentFolder => LibraryColumnParentFolder,
        LibraryFileColumn.Waveform => LibraryColumnWaveform,
        LibraryFileColumn.Jacket => LibraryColumnJacket,
        LibraryFileColumn.VideoSize => LibraryColumnVideoSize,
        LibraryFileColumn.FrameRate => LibraryColumnFrameRate,
        LibraryFileColumn.VideoCodec => LibraryColumnVideoCodec,
        LibraryFileColumn.AudioCodec => LibraryColumnAudioCodec,
        LibraryFileColumn.Loudness => LibraryColumnLoudness,
        _ => LibraryColumnName,
    };

    /// <summary>プレイリスト見出し用。長い名前は改行して列幅を抑える。設定チェックは <see cref="LibraryColumnLabel"/>。</summary>
    public static string LibraryColumnHeader(LibraryFileColumn column) => column switch
    {
        LibraryFileColumn.BitRate => Get("ビット\nレート", "Bit\nrate"),
        LibraryFileColumn.FrameRate => Get("フレーム\nレート", "Frame\nrate"),
        LibraryFileColumn.VideoCodec => Get("映像\nコーデック", "Video\ncodec"),
        LibraryFileColumn.AudioCodec => Get("音声\nコーデック", "Audio\ncodec"),
        LibraryFileColumn.AlbumArtist => Get("アルバム\nアーティスト", "Album\nartist"),
        _ => LibraryColumnLabel(column),
    };
    /// <summary>プレイリスト上段のグループラベル。UI 言語に関わらず英語（Auto next / Random と同じ）。</summary>
    public static string LibraryGroupLabel => "Group";
    public static string LibraryExplorerLabel => Get("ライブラリ", "Library");
    public static string LibraryPlaylistLabel => Get("プレイリスト", "Playlist");
    public static string LibrarySearchHint => Get("検索", "Find");
    /// <summary>プレイリスト上段チェック。UI 言語に関わらず英語。ショートカットは出さない（マニュアル側）。Alt+R 連想のため Random。</summary>
    public static string LabelLibraryShuffle => "Random";
    /// <summary>プレイリスト上段チェック。UI 言語に関わらず英語。ショートカットは出さない（マニュアル側）。</summary>
    public static string LabelLibraryAutoPlay => "Auto Play";
    /// <summary>プレイリスト上段チェック。UI 言語に関わらず英語。ショートカットは出さない（マニュアル側）。</summary>
    public static string LabelLibraryAutoPlayNext => "Auto next";
    public static string LibraryExplorerDesktop => Get("デスクトップ", "Desktop");
    public static string LibraryExplorerDocuments => Get("ドキュメント", "Documents");
    public static string LibraryExplorerMusic => Get("ミュージック", "Music");
    public static string LibraryExplorerPictures => Get("ピクチャ", "Pictures");
    public static string LibraryExplorerVideos => Get("ビデオ", "Videos");
    public static string LibraryFavoritesLabel => Get("お気に入り", "Favorites");
    public static string LibraryMenuAddToFavorites => Get("お気に入りへ追加", "Add to Favorites");
    public static string LibraryMenuAddAsExplorerRoot => Get("ツリーのルートに登録", "Register as Tree Root");
    public static string LibraryMenuRemoveExplorerRoot => Get("ツリーのルートから外す", "Remove Tree Root");
    public static string LibraryMenuRefreshExplorer => Get("ツリーを再読み込み", "Refresh Tree");
    public static string LibraryMenuRebuildCatalog => Get("ライブラリを再構築", "Rebuild Library");
    public static string LibraryMenuReplacePlaylist => Get("プレイリストをクリアして追加", "Clear Playlist and Add");
    public static string LibraryMenuAppendPlaylist => Get("プレイリストへ追加", "Add to Playlist");
    public static string LibraryMenuRemoveFromFavorites => Get("お気に入りから削除", "Remove from Favorites");
    public static string LibraryMenuClearFromPlaylist => Get("プレイリストからクリア", "Clear from Playlist");
    // アクセスキーは付けない（メニュー外クリック時の誤発火を避ける）。ショートカットは InputGestureText。
    public static string LibraryMenuMatchLoudness => Get("ラウドネスを揃える", "Match Loudness");
    public static string LibraryMenuSaveMp3 => Get("MP3 として保存", "Save as MP3");
    public static string LibraryMenuCopy => Get("コピー", "Copy");
    public static string LibraryMenuCopyFileName => Get("ファイル名をコピー", "Copy File Name");
    public static string LibraryMenuCopyFilePath => Get("フルパスをコピー", "Copy Full Path");
    public static string LibraryMenuOpenInExplorer => Get("エクスプローラーで開く", "Open in Explorer");
    /// <summary>グループなし。ラベル "Group" と同様、UI 言語に関わらず英語。</summary>
    public static string LibraryGroupNone => "None";
    /// <summary>グループ項目は列名と同じ表記（<see cref="LibraryColumnLabel"/>）。</summary>
    public static string LibraryGroupTitle => LibraryColumnTitle;
    public static string LibraryGroupArtist => LibraryColumnArtist;
    public static string LibraryGroupAlbum => LibraryColumnAlbum;
    public static string LibraryGroupGenre => LibraryColumnGenre;
    public static string LibraryGroupYear => LibraryColumnYear;
    public static string LibraryGroupKind => LibraryColumnKind;
    public static string LibraryGroupSampleRate => LibraryColumnSampleRate;
    public static string LibraryGroupBitDepth => LibraryColumnBitDepth;
    public static string LibraryGroupChannels => LibraryColumnChannels;
    public static string LibraryGroupFolder => LibraryColumnParentFolder;
    public static string LibraryGroupUntitled => Get("(未保存)", "(Untitled)");
    public static string LibraryGroupBlank => Get("(なし)", "(None)");
    public static string LibraryJacketMark => "✓";
    public static string FormatBitRate(int kbps) =>
        kbps <= 0 ? string.Empty : $"{kbps} kbps";

    /// <summary>Integrated（表示は LKFS。LUFS と同値）。未計測は空。</summary>
    public static string FormatIntegratedLufs(float? lufs)
    {
        if (lufs is not float value)
        {
            return string.Empty;
        }

        if (!float.IsFinite(value))
        {
            return "--.- " + LabelLufs;
        }

        return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            + " "
            + LabelLufs;
    }

    /// <summary>動画の総ビットレート。内部は kbps のまま、表示は Mbps。</summary>
    public static string FormatVideoBitRate(int kbps)
    {
        if (kbps <= 0)
        {
            return string.Empty;
        }

        var mbps = kbps / 1000d;
        if (Math.Abs(mbps - Math.Round(mbps)) < 0.0005)
        {
            return $"{(int)Math.Round(mbps)} Mbps";
        }

        return $"{mbps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} Mbps";
    }

    public static string FormatVideoSize(int width, int height) =>
        width > 0 && height > 0 ? $"{width}x{height}" : string.Empty;

    public static string FormatFrameRate(double framesPerSecond)
    {
        if (framesPerSecond <= 0)
        {
            return string.Empty;
        }

        // 23.976 / 29.97 / 59.94 など一般的な値は小数、整数 fps は整数表記。
        var rounded = Math.Round(framesPerSecond, 3);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.0005)
        {
            return $"{(int)Math.Round(rounded)} fps";
        }

        return $"{rounded.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} fps";
    }
    public static string TabTimeCopy => Get("コピー", "Copy");
    public static string TabTimeSaveCsv => Get("CSV", "CSV");
    public static string TabTimeSavePdf => Get("PDF", "PDF");
    public static string TabTimeSaveCsvTitle => Get("CSV として保存", "Save CSV");
    public static string TabTimeSavePdfTitle => Get("PDF として保存", "Save PDF");
    public static string TabTimeFileNameCsv => Get("ファイルの時間.csv", "FileTimes.csv");
    public static string TabTimeFileNamePdf => Get("ファイルの時間.pdf", "FileTimes.pdf");
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
    public static string OverlayClose => Get("Close", "Close");
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
        "Delete Silence" => Get("無音検出（削除）", "Detect Silence (Delete)"),
        "Detect Silence" => Get("無音検出（リージョン付加）", "Detect Silence (Add Regions)"),
        "Detect Silence Markers" => Get("無音検出（マーカー付与）", "Detect Silence (Add Markers)"),
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

    public static string FormatFileDate(DateTime? value)
    {
        if (value is not { } date || date == DateTime.MinValue)
        {
            return string.Empty;
        }

        return date.ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
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

    /// <summary>
    /// 動画オーバーレイ用。分：秒はそのまま、小数点以下だけをその秒内のフレーム番号にする（既定 30 fps）。
    /// </summary>
    public static string FormatDurationWithFrames(double seconds, double framesPerSecond = 30)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            return FormatDurationWithFrames(0, framesPerSecond);
        }

        var fps = framesPerSecond > 0 && !double.IsNaN(framesPerSecond) && !double.IsInfinity(framesPerSecond)
            ? framesPerSecond
            : 30;
        var fpsInt = Math.Max(1, (int)Math.Round(fps));
        var total = seconds;
        var minutes = (int)(total / 60d);
        var sec = total - minutes * 60d;
        var wholeSec = (int)sec;
        var frac = sec - wholeSec;
        var frame = (int)Math.Floor(frac * fps + 1e-9);
        if (frame >= fpsInt)
        {
            frame = 0;
            wholeSec++;
            if (wholeSec >= 60)
            {
                wholeSec = 0;
                minutes++;
            }
        }

        var digits = fpsInt >= 100 ? 3 : 2;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return string.Create(
            culture,
            $"{minutes:00}:{wholeSec:00}.{frame.ToString($"D{digits}", culture)}");
    }

    /// <summary>サンプル位置を動画オーバーレイ用タイムコードへ（MM:SS.ff。小数点以下は 30fps 換算のフレーム）。</summary>
    public static string FormatTimecodeWithFrames(long frame, int sampleRate, double framesPerSecond = 30)
    {
        var rate = Math.Max(1, sampleRate);
        return FormatDurationWithFrames(Math.Max(0, frame) / (double)rate, framesPerSecond);
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
