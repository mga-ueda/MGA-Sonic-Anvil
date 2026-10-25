using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 一括編集のタブ別進捗。ワーカーから直接呼び、表示％が変わったときだけ
/// 最新スナップショットを UI へ BeginInvoke する（System.Progress の SyncContext 経由は使わない）。
/// </summary>
internal sealed class BatchEditBusyProgress
{
    private readonly object _gate = new();
    private readonly string[] _names;
    private readonly double[] _progress;
    private readonly long[] _weights;
    private readonly Dispatcher _dispatcher;
    private readonly Action<HistoryPasteBusySnapshot> _apply;
    private readonly int[] _lastJobPaint;
    private int _lastOverallPaint = -1;
    private bool _flushQueued;

    public BatchEditBusyProgress(
        string[] names,
        double[] progress,
        long[] weights,
        Dispatcher dispatcher,
        Action<HistoryPasteBusySnapshot> apply)
    {
        _names = names;
        _progress = progress;
        _weights = weights;
        _dispatcher = dispatcher;
        _apply = apply;
        _lastJobPaint = new int[progress.Length];
        Array.Fill(_lastJobPaint, -1);
    }

    /// <summary>指定タブ用。呼び出しスレッドで進捗を書き、必要なら UI へ送る。例外は外へ出さない。</summary>
    public IProgress<double> ForTab(int index) => new TabProgress(this, index);

    public void Report(int index, double value)
    {
        if ((uint)index >= (uint)_progress.Length)
        {
            return;
        }

        var queueFlush = false;
        lock (_gate)
        {
            _progress[index] = Math.Clamp(Math.Max(0.02, value), 0d, 0.99d);
            var jobPaint = BusyGlassOverlay.PaintPercent(_progress[index]);
            var captured = HistoryPasteBusyProgress.Capture(_names, _progress, _weights);
            var overallPaint = BusyGlassOverlay.PaintPercent(captured.Overall);
            if (jobPaint == _lastJobPaint[index] && overallPaint == _lastOverallPaint)
            {
                return;
            }

            _lastJobPaint[index] = jobPaint;
            _lastOverallPaint = overallPaint;
            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
            queueFlush = true;
        }

        if (!queueFlush)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(FlushToUi);
        }
        catch
        {
            lock (_gate)
            {
                _flushQueued = false;
            }
        }
    }

    private void FlushToUi()
    {
        HistoryPasteBusySnapshot snap;
        lock (_gate)
        {
            _flushQueued = false;
            snap = HistoryPasteBusyProgress.Capture(_names, _progress, _weights);
            _lastOverallPaint = BusyGlassOverlay.PaintPercent(snap.Overall);
            for (var i = 0; i < _progress.Length; i++)
            {
                _lastJobPaint[i] = BusyGlassOverlay.PaintPercent(_progress[i]);
            }
        }

        try
        {
            _apply(snap);
        }
        catch
        {
            // すりガラス更新失敗で計測／ゲイン本体を止めない。
        }
    }

    private sealed class TabProgress(BatchEditBusyProgress owner, int index) : IProgress<double>
    {
        public void Report(double value)
        {
            try
            {
                owner.Report(index, value);
            }
            catch
            {
                // IProgress.Report は投げない。ワーカーループを守る。
            }
        }
    }
}
