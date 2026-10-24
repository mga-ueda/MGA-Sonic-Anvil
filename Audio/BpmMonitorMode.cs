namespace MgaSonicAnvil.Audio;

/// <summary>
/// BPM モニター列の 2 段階。セッションのみ（設定に残さない）。
/// オン時は解析・表示と、本アプリ WAV の拍クリックを有効にする。
/// </summary>
internal enum BpmMonitorMode
{
    /// <summary>解析もクリックもオフ（既定）。</summary>
    Off = 0,

    /// <summary>BPM 解析・表示＋本アプリ Low.wav の拍クリック。</summary>
    On = 1,
}
