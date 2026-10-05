namespace MgaSonicAnvil.UI;

/// <summary>停止プレビュー用。冒頭が黒でも見えるフレーム位置を選ぶ。</summary>
internal static class LibraryVideoPreview
{
    /// <summary>平均輝度または明るい画素の割合がこれを超えたら「映像が見える」。</summary>
    internal const int MinAverageLuma = 18;

    internal const double MinBrightPixelRatio = 0.02;

    internal const int BrightLumaThreshold = 24;

    /// <summary>先頭付近と尺の割合から候補を作る。本再生は常に 0 から。</summary>
    internal static IReadOnlyList<TimeSpan> CandidatePositions(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return [TimeSpan.Zero];
        }

        var seconds = duration.TotalSeconds;
        var list = new List<TimeSpan>(12);
        void Add(double sec)
        {
            if (sec < 0 || sec >= seconds)
            {
                return;
            }

            var t = TimeSpan.FromSeconds(sec);
            for (var i = 0; i < list.Count; i++)
            {
                if (Math.Abs((list[i] - t).TotalMilliseconds) < 40)
                {
                    return;
                }
            }

            list.Add(t);
        }

        Add(0);
        Add(0.25);
        Add(0.5);
        Add(1);
        Add(2);
        Add(3);
        Add(5);
        Add(seconds * 0.1);
        Add(seconds * 0.2);
        Add(seconds * 0.35);
        Add(Math.Min(seconds * 0.5, 30));
        return list;
    }

    /// <summary>BGRA 画素が十分明るいか。</summary>
    internal static bool FrameLooksVisible(byte[] bgra, int pixelCount)
    {
        if (bgra.Length < pixelCount * 4 || pixelCount <= 0)
        {
            return false;
        }

        long sum = 0;
        var bright = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            var o = i * 4;
            var y = (bgra[o + 2] * 77 + bgra[o + 1] * 150 + bgra[o] * 29) >> 8;
            sum += y;
            if (y >= BrightLumaThreshold)
            {
                bright++;
            }
        }

        var avg = sum / (double)pixelCount;
        return avg >= MinAverageLuma || bright / (double)pixelCount >= MinBrightPixelRatio;
    }
}
