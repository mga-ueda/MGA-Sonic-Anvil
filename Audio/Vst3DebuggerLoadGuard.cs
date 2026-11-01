using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// Altiverb（iLok/PACE）は .NET デバッガ付きプロセスでは InitDll が戻らない
/// （ネイティブ CreateThread でもタイムアウトする）。読み込みを試みず拒否する。
/// Soft Limit 等はデバッガ下でも通常ロード可能。
/// </summary>
internal static class Vst3DebuggerLoadGuard
{
    public static bool IsDebuggerPresentLikely =>
        System.Diagnostics.Debugger.IsAttached || IsDebuggerPresent();

    public static bool IsPaceRiskPlugin(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
        {
            return false;
        }

        return pathOrName.Contains("Altiverb", StringComparison.OrdinalIgnoreCase)
            || pathOrName.Contains("Audio Ease", StringComparison.OrdinalIgnoreCase)
            || pathOrName.Contains("AudioEase", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// デバッガ付きでは Altiverb を読まない（InitDll から戻らない）。
    /// </summary>
    public static bool ShouldRefuseUnderDebugger(string pluginPath) =>
        IsPaceRiskPlugin(pluginPath) && IsDebuggerPresentLikely;

    [DllImport("kernel32")]
    private static extern bool IsDebuggerPresent();
}
