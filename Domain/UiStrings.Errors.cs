namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string ErrUndefinedColorKey(string key) => Format(
        "未定義の色キー: {0}",
        "Undefined color key: {0}",
        key);

    public static string ErrEmptyAudioFile => Get("Empty audio file.", "Empty audio file.");

    public static string ErrChannelCountChanged(int from, int to) => Format(
        "Channel count changed while reading ({0} → {1}).",
        "Channel count changed while reading ({0} → {1}).",
        from,
        to);

    public static string ErrAiffExportNotSupported => Get(
        "AIFF export is not supported.",
        "AIFF export is not supported.");

    public static string ErrorNoCaptureDevice => Get(
        "録音できるデバイスがありません。",
        "No capture device is available.");
    public static string ErrorRecordFailed => Get(
        "録音を開始できませんでした。",
        "Could not start recording.");
    public static string ErrorToneFailed => Get(
        "テスト音を再生できませんでした。",
        "Could not play the test tone.");
    public static string ErrorChannelVoiceFailed => Get(
        "チャンネル名を読み上げられませんでした。Windows の英語音声合成を確認してください。",
        "Could not speak the channel name. Check Windows English speech synthesis.");
    public static string ErrAudioOutputUnavailable => Get(
        "Audio output device is not available.",
        "Audio output device is not available.");

    public static string ErrAsioNoOutputChannels => Get(
        "ASIO driver has no output channels.",
        "ASIO driver has no output channels.");

    public static string ErrAsioSampleRateUnsupported(string driverName, int rate) => Format(
        "ASIO '{0}' does not support {1} Hz.",
        "ASIO '{0}' does not support {1} Hz.",
        driverName,
        rate);

    public static string ErrAsioSampleRateBeforeInit => Get(
        "Could not read the ASIO driver sample rate before Init.",
        "Could not read the ASIO driver sample rate before Init.");

    public static string ErrAsioNoDrivers => Get(
        "No ASIO drivers are installed.",
        "No ASIO drivers are installed.");

    public static string ErrAsioDriverNotFound(string driverName) => Format(
        "ASIO driver '{0}' was not found.",
        "ASIO driver '{0}' was not found.",
        driverName);

    public static string ErrWaveTooShortToAppendMeta => Get(
        "Wave file is too short to append metadata.",
        "Wave file is too short to append metadata.");

    public static string ErrNotRiffWave => Get("Not a RIFF wave file.", "Not a RIFF wave file.");
    public static string ErrNotWaveFile => Get("Not a WAVE file.", "Not a WAVE file.");
    public static string ErrWaveExceedsRiffLimit => Get(
        "Wave file exceeds RIFF size limit.",
        "Wave file exceeds RIFF size limit.");

    public static string ErrFileAssociationNoExe => Get(
        "実行ファイルの場所が分かりません。",
        "Could not find this executable.");

    public static string ErrFileAssociationNotDefault => Get(
        "Windows が既定のアプリを保護しているため、このアプリを既定にできませんでした。「プログラムから開く」には登録済みです。",
        "Windows is protecting the default app, so this app could not be made the default. It is registered under Open with.");

    public static string ErrFileAssociationFailed(string detail) => Format(
        "関連付けを変更できませんでした。{0}",
        "Could not change the file association. {0}",
        detail);
}
