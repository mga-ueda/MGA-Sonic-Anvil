namespace MgaSonicAnvil.Domain;

/// <summary>動画を開けなかったときのダイアログ本文（なぜ再生できないか、どうすれば再生できるか）。</summary>
internal static class VideoOpenFailureText
{
    /// <summary>ffmpeg の状態。</summary>
    internal enum FfmpegState
    {
        /// <summary>設定のパスが空。</summary>
        NotConfigured,

        /// <summary>パスはあるがファイルが無い。</summary>
        Missing,

        /// <summary>ffmpeg でも開けなかった。</summary>
        Failed,
    }

    /// <param name="fileName">表示用のファイル名。</param>
    /// <param name="codec">fourcc（hvc1 など）または ffmpeg のコーデック名。不明なら空。</param>
    /// <param name="ffmpeg">ffmpeg の状態。</param>
    /// <param name="ffmpegPath">設定の ffmpeg.exe のパス（<see cref="FfmpegState.Missing"/> のとき表示）。</param>
    /// <param name="ffmpegError">ffmpeg の最後のエラー行（<see cref="FfmpegState.Failed"/> のとき表示）。</param>
    /// <param name="timedOut">開くのが時間切れになった。</param>
    public static string Build(
        string fileName,
        string? codec,
        FfmpegState ffmpeg,
        string? ffmpegPath,
        string? ffmpegError,
        bool timedOut)
    {
        var lines = new List<string>
        {
            UiStrings.Get("この動画を再生できません。", "This video cannot be played."),
            fileName,
            string.Empty,
        };

        if (timedOut)
        {
            lines.Add(UiStrings.Get(
                "開くのに時間がかかりすぎたため中断しました。ネットワーク上のファイルや、ffmpeg.exe の置き場所の読み込みが遅い可能性があります。",
                "Opening took too long and was stopped. The file or ffmpeg.exe may be on a slow network location."));
            return string.Join("\n", lines);
        }

        var friendly = FriendlyCodecName(codec);
        lines.Add(friendly.Length > 0
            ? UiStrings.Format(
                "映像形式（{0}）を Windows のデコーダで読めませんでした。",
                "Windows could not decode the video format ({0}).",
                friendly)
            : UiStrings.Get(
                "映像を Windows のデコーダで読めませんでした。",
                "Windows could not decode the video."));

        switch (ffmpeg)
        {
            case FfmpegState.Failed:
                lines.Add(UiStrings.Get("ffmpeg でも読めませんでした。", "ffmpeg could not read it either."));
                if (!string.IsNullOrWhiteSpace(ffmpegError))
                {
                    lines.Add("ffmpeg: " + ffmpegError.Trim());
                }

                return string.Join("\n", lines);

            case FfmpegState.Missing:
                lines.Add(UiStrings.Format(
                    "設定の「{0}」に指定された ffmpeg.exe が見つかりません。\n{1}",
                    "The ffmpeg.exe set in \"{0}\" was not found.\n{1}",
                    UiStrings.LabelFfmpegPath,
                    ffmpegPath ?? string.Empty));
                break;
        }

        if (StoreExtensionName(codec) is { } extension)
        {
            lines.Add(string.Empty);
            lines.Add(UiStrings.Format(
                "Microsoft Store の「{0}」を入れると Windows だけで再生できます。",
                "Installing \"{0}\" from the Microsoft Store lets Windows play it.",
                extension));
        }

        lines.Add(string.Empty);
        lines.Add(UiStrings.Format(
            "設定の「{0}」タブで「{1}」に ffmpeg.exe を指定すると、プロキシを作らずに ffmpeg で再生できます。",
            "Set ffmpeg.exe in \"{1}\" on the \"{0}\" settings tab to play it with ffmpeg without creating a proxy.",
            UiStrings.LabelSettingsTabExport,
            UiStrings.LabelFfmpegPath));
        return string.Join("\n", lines);
    }

    /// <summary>fourcc／ffmpeg のコーデック名を人が読める名前へ。知らないものはそのまま（空なら空）。</summary>
    internal static string FriendlyCodecName(string? codec)
    {
        var key = (codec ?? string.Empty).Trim();
        return key.ToLowerInvariant() switch
        {
            "" => string.Empty,
            "hvc1" or "hev1" or "hevc" or "h265" => "HEVC (H.265)",
            "avc1" or "avc3" or "h264" => "H.264",
            "av01" or "av1" => "AV1",
            "vp09" or "vp9" => "VP9",
            "vp08" or "vp8" => "VP8",
            "apcn" or "apch" or "apcs" or "apco" or "ap4h" or "ap4x" or "prores" => "Apple ProRes",
            "avdn" or "avdh" or "dnxhd" => "Avid DNxHD / DNxHR",
            "mp4v" or "xvid" or "divx" or "dx50" or "mpeg4" => "MPEG-4 Part 2",
            "mp2v" or "m2v1" or "mpeg2video" => "MPEG-2",
            "mjpa" or "mjpb" or "mjpg" or "jpeg" or "mjpeg" => "Motion JPEG",
            "cfhd" => "GoPro CineForm",
            _ => key,
        };
    }

    /// <summary>Windows 標準に無く、Store の拡張機能で足せるコーデックなら拡張機能名。</summary>
    internal static string? StoreExtensionName(string? codec) =>
        (codec ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "hvc1" or "hev1" or "hevc" or "h265" => UiStrings.Get("HEVC ビデオ拡張機能", "HEVC Video Extensions"),
            "av01" or "av1" => UiStrings.Get("AV1 Video Extension", "AV1 Video Extension"),
            "vp09" or "vp9" or "vp08" or "vp8" => UiStrings.Get("VP9 ビデオ拡張機能", "VP9 Video Extensions"),
            _ => null,
        };
}
