using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 終了中プロセスが ASIO を握っているあいだ、新規側はフォールバックせず同じデバイスをやり直す。
/// </summary>
internal static class AudioDeviceOpenRetry
{
    public const int SleepMilliseconds = 250;

    public static TimeSpan Budget { get; } = TimeSpan.FromMilliseconds(AudioOutputFlush.MaxMilliseconds);

    public static bool ShouldRetry(AudioOutputApi api, Exception error, TimeSpan elapsed)
    {
        if (api != AudioOutputApi.Asio || elapsed >= Budget)
        {
            return false;
        }

        return !IsPermanentAsioFailure(error);
    }

    public static bool IsPermanentAsioFailure(Exception error)
    {
        var message = error.Message;
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        if (string.Equals(message, UiStrings.ErrAsioNoDrivers, StringComparison.Ordinal))
        {
            return true;
        }

        return message.StartsWith("ASIO driver '", StringComparison.Ordinal)
            && message.Contains("was not found", StringComparison.Ordinal);
    }
}
