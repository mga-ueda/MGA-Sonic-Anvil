using System;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioDeviceOpenRetryTests
{
    [Fact]
    public void AsioBusy_RetriesUntilFlushBudget()
    {
        var busy = new InvalidOperationException("ASIO driver is already in use.");
        Assert.True(AudioDeviceOpenRetry.ShouldRetry(AudioOutputApi.Asio, busy, TimeSpan.Zero));
        Assert.True(AudioDeviceOpenRetry.ShouldRetry(
            AudioOutputApi.Asio,
            busy,
            AudioDeviceOpenRetry.Budget - TimeSpan.FromMilliseconds(1)));
        Assert.False(AudioDeviceOpenRetry.ShouldRetry(
            AudioOutputApi.Asio,
            busy,
            AudioDeviceOpenRetry.Budget));
    }

    [Fact]
    public void MissingDriver_DoesNotRetry()
    {
        Assert.True(AudioDeviceOpenRetry.IsPermanentAsioFailure(
            new InvalidOperationException(UiStrings.ErrAsioNoDrivers)));
        Assert.True(AudioDeviceOpenRetry.IsPermanentAsioFailure(
            new InvalidOperationException(UiStrings.ErrAsioDriverNotFound("Missing"))));
        Assert.False(AudioDeviceOpenRetry.ShouldRetry(
            AudioOutputApi.Asio,
            new InvalidOperationException(UiStrings.ErrAsioNoDrivers),
            TimeSpan.Zero));
    }

    [Fact]
    public void WasapiAndWaveOut_DoNotRetry()
    {
        var busy = new InvalidOperationException("device in use");
        Assert.False(AudioDeviceOpenRetry.ShouldRetry(AudioOutputApi.Wasapi, busy, TimeSpan.Zero));
        Assert.False(AudioDeviceOpenRetry.ShouldRetry(AudioOutputApi.WaveOut, busy, TimeSpan.Zero));
    }
}
