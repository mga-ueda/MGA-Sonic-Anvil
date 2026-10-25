using System.Windows.Threading;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BatchEditBusyProgressTests
{
    [Fact]
    public void Report_PushesSnapshotWhenPaintPercentSteps()
    {
        var names = new[] { "a.wav" };
        var progress = new double[1];
        var weights = new[] { 100L };
        var applied = new List<HistoryPasteBusySnapshot>();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var busy = new BatchEditBusyProgress(
            names,
            progress,
            weights,
            dispatcher,
            snap => applied.Add(snap));

        var tab = busy.ForTab(0);
        tab.Report(0);
        tab.Report(0.024);
        DrainDispatcher(dispatcher);
        Assert.True(applied.Count >= 1);
        Assert.Equal(2, BusyGlassOverlay.PaintPercent(applied[^1].Overall));

        tab.Report(0.5);
        DrainDispatcher(dispatcher);
        Assert.Contains(applied, snap => BusyGlassOverlay.PaintPercent(snap.Overall) == 50);

        tab.Report(0.99);
        DrainDispatcher(dispatcher);
        Assert.Contains(applied, snap => BusyGlassOverlay.PaintPercent(snap.Overall) == 99);
    }

    [Fact]
    public void Report_DoesNotThrowToCallerWhenApplyFails()
    {
        var busy = new BatchEditBusyProgress(
            ["a.wav"],
            new double[1],
            [1L],
            Dispatcher.CurrentDispatcher,
            _ => throw new InvalidOperationException("ui"));

        var tab = busy.ForTab(0);
        tab.Report(0.1);
        DrainDispatcher(Dispatcher.CurrentDispatcher);
        tab.Report(0.5);
        DrainDispatcher(Dispatcher.CurrentDispatcher);
    }

    [Fact]
    public void Report_IgnoresSubPercentNoiseUntilPaintSteps()
    {
        var applied = new List<HistoryPasteBusySnapshot>();
        var busy = new BatchEditBusyProgress(
            ["a.wav"],
            new double[1],
            [1L],
            Dispatcher.CurrentDispatcher,
            snap => applied.Add(snap));

        var tab = busy.ForTab(0);
        tab.Report(0.081);
        DrainDispatcher(Dispatcher.CurrentDispatcher);
        var afterFirst = applied.Count;
        Assert.True(afterFirst >= 1);

        tab.Report(0.084);
        DrainDispatcher(Dispatcher.CurrentDispatcher);
        Assert.Equal(afterFirst, applied.Count);

        tab.Report(0.12);
        DrainDispatcher(Dispatcher.CurrentDispatcher);
        Assert.True(applied.Count > afterFirst);
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
