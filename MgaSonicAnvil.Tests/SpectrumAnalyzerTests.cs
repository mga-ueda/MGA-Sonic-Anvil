using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpectrumAnalyzerTests
{
    [Fact]
    public void CreateBands_AtFortyEightK_HasThirtyOneCenters()
    {
        var bands = SpectrumAnalyzer.CreateBands(24000);
        Assert.Equal(31, bands.Count);
        Assert.Equal(20, bands.Centers[0], 6);
        Assert.Equal(20000, bands.Centers[^1], 6);
    }

    [Fact]
    public void CreateBands_DropsLabelsAboveNyquist()
    {
        var bands = SpectrumAnalyzer.CreateBands(8000);
        Assert.True(bands.Centers[^1] <= 8000 * 0.995 + 1e-6);
        Assert.DoesNotContain(bands.Centers, hz => hz > 8000);
    }

    [Fact]
    public void OriginalAspect_MatchesLayerMusicCheckerCanvas()
    {
        Assert.Equal(353, SpectrumAnalyzer.OriginalOuterHeightPx);
        Assert.Equal(1070, SpectrumAnalyzer.OriginalOuterWidthPx);
        Assert.InRange(SpectrumAnalyzer.OriginalAspect, 3.0, 3.1);
    }

    [Fact]
    public void RequiredPlot_IsOnePixelBarAndGutter()
    {
        Assert.Equal(31, SpectrumAnalyzer.MaxBandCount);
        Assert.Equal(61, SpectrumAnalyzer.RequiredPlotDevicePx);
        var rects = SpectrumAnalyzer.CreateBarRects(0, SpectrumAnalyzer.RequiredPlotDevicePx, 31, 1);
        Assert.True(rects.All(r => r.BarW == 1));
    }

    [Fact]
    public void BarRects_DistributeRemainderPixels()
    {
        var rects = SpectrumAnalyzer.CreateBarRects(10, 100, 31, 1);
        Assert.Equal(31, rects.Length);
        var used = rects.Sum(r => r.BarW) + 30;
        Assert.Equal(100, used);
        Assert.Equal(10, rects[0].X1);
        Assert.True(rects.All(r => r.BarW >= 2));
    }

    [Fact]
    public void LedCells_KeepOneRowPerDecibel()
    {
        Assert.Equal(50, SpectrumAnalyzer.LedRowCount(-50));
        var cells = SpectrumAnalyzer.CreateLedCells(0, 299, -50);
        Assert.Equal(50, cells.Count);
        Assert.Equal(-50, cells.LoInt);
        Assert.InRange(cells.Bot[0] - cells.Top[0], 4.5, 5.5);
    }

    [Fact]
    public void SoftenDisplayPeak_KeepsZeroAndFloor()
    {
        Assert.Equal(0, SpectrumAnalyzer.SoftenDisplayPeak(0), 3);
        Assert.Equal(-20, SpectrumAnalyzer.SoftenDisplayPeak(-20), 3);
        Assert.True(SpectrumAnalyzer.SoftenDisplayPeak(-3) < -3);
    }

    [Fact]
    public void DbNorm_MapsFloorToZeroAndCeilingToOne()
    {
        Assert.Equal(0, SpectrumAnalyzer.DbNorm(SpectrumAnalyzer.FloorDb));
        Assert.Equal(1, SpectrumAnalyzer.DbNorm(SpectrumAnalyzer.CeilingDb));
    }

    [Fact]
    public void Process_SineNearOneK_RaisesThatBand()
    {
        var analyzer = new SpectrumAnalyzer();
        var samples = new float[SpectrumAnalyzer.FftSize];
        const int rate = 48000;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
        }

        analyzer.Process(samples, rate, 1 / 60d, active: true);
        var bands = analyzer.CurrentBands;
        var env = analyzer.EnvelopeDb;
        var at1k = Array.IndexOf(bands.Centers, 1000);
        Assert.InRange(at1k, 0, bands.Count - 1);
        var peakBand = 0;
        for (var i = 1; i < env.Length; i++)
        {
            if (env[i] > env[peakBand])
            {
                peakBand = i;
            }
        }

        Assert.InRange(Math.Abs(bands.Centers[peakBand] - 1000), 0, 400);
        Assert.True(env[at1k] > SpectrumAnalyzer.FloorDb + 10);
    }

    [Fact]
    public void Process_Idle_DropsPeakHoldToFloor()
    {
        var analyzer = new SpectrumAnalyzer();
        var samples = new float[SpectrumAnalyzer.FftSize];
        const int rate = 48000;
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
        }

        for (var i = 0; i < 8; i++)
        {
            analyzer.Process(samples, rate, 1 / 60d, active: true);
        }

        Assert.True(analyzer.HasVisibleLevel);
        for (var i = 0; i < 20; i++)
        {
            analyzer.Process(samples, rate, 1 / 60d, active: false);
        }

        var peakBand = 0;
        for (var i = 1; i < analyzer.PeakHoldDb.Length; i++)
        {
            if (analyzer.PeakHoldDb[i] > analyzer.PeakHoldDb[peakBand])
            {
                peakBand = i;
            }
        }

        Assert.True(analyzer.PeakHoldDb[peakBand] > analyzer.EnvelopeDb[peakBand] + 5f);

        var idleSec = SpectrumAnalyzer.PeakHoldCenterSec
            + (SpectrumAnalyzer.CeilingDb - SpectrumAnalyzer.FloorDb) / SpectrumAnalyzer.PeakReleaseDbPerSec
            + 2;
        var idleFrames = (int)Math.Ceiling(idleSec * 60);
        for (var i = 0; i < idleFrames; i++)
        {
            analyzer.Process(samples, rate, 1 / 60d, active: false);
        }

        Assert.False(analyzer.HasVisibleLevel);
        foreach (var db in analyzer.PeakHoldDb)
        {
            Assert.True(db <= SpectrumAnalyzer.FloorDb + 0.3f);
        }
    }
}
