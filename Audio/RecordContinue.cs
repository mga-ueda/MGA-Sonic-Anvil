namespace MgaSonicAnvil.Audio;

/// <summary>未保存の録音へ追記できるか。保存後は不可。</summary>
internal static class RecordContinue
{
    /// <summary>録音中にピークピラミッドを作り直す間隔（約 0.5 秒）。</summary>
    public static int DrawSettleFrames(int sampleRate) =>
        Math.Max(1, Math.Clamp(sampleRate, 1, 384000) / 2);

    /// <summary>表示カラムのうち、settle 済みフレームだけに収まる先頭の本数。</summary>
    public static int CountPrefixColumns(
        long startFrame,
        long rangeFrames,
        int colCount,
        long settledFrames)
    {
        if (colCount <= 0 || rangeFrames <= 0 || settledFrames <= startFrame)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < colCount; i++)
        {
            var colEnd = startFrame + (i + 1) * rangeFrames / colCount;
            if (colEnd > settledFrames)
            {
                break;
            }

            count++;
        }

        return count;
    }

    public static bool Matches(AudioDocument document, int sampleRate, int channels) =>
        document.CanContinueRecording
        && document.SampleRate == sampleRate
        && document.Channels == channels;
}
