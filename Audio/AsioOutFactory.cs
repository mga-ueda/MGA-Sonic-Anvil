using System.Reflection;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// Steinberg built-in ASIO Driver は <c>bufferSwitch</c> を呼ばず
/// <c>bufferSwitchTimeInfo</c> だけを呼ぶ。NAudio 2.2 の timeInfo はバッファを
/// 埋めずに戻るので、再生しても無音になる。timeInfo を同じ bufferSwitch へ転送する。
/// </summary>
internal static class AsioOutFactory
{
    public static AsioOut Open(string driverName)
    {
        var asio = new AsioOut(driverName)
        {
            AutoStop = false,
        };
        TryForwardBufferSwitchTimeInfo(asio);
        return asio;
    }

    internal static AsioCallbacks ForwardBufferSwitchTimeInfo(AsioCallbacks callbacks)
    {
        var bufferSwitch = callbacks.pbufferSwitch;
        if (bufferSwitch is null)
        {
            return callbacks;
        }

        callbacks.pbufferSwitchTimeInfo = (asioTime, index, direct) =>
        {
            bufferSwitch(index, direct);
            return asioTime;
        };
        return callbacks;
    }

    private static void TryForwardBufferSwitchTimeInfo(AsioOut asio)
    {
        if (FindDriverExt(asio) is not { } ext)
        {
            return;
        }

        var field = ext.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(candidate => candidate.FieldType == typeof(AsioCallbacks));
        if (field?.GetValue(ext) is not AsioCallbacks callbacks)
        {
            return;
        }

        field.SetValue(ext, ForwardBufferSwitchTimeInfo(callbacks));
    }

    private static AsioDriverExt? FindDriverExt(AsioOut asio)
    {
        var named = typeof(AsioOut).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);
        if (named?.GetValue(asio) is AsioDriverExt driver)
        {
            return driver;
        }

        return typeof(AsioOut)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(candidate => typeof(AsioDriverExt).IsAssignableFrom(candidate.FieldType))
            ?.GetValue(asio) as AsioDriverExt;
    }
}
