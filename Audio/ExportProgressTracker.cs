namespace MgaSonicAnvil.Audio;

internal enum ExportJobState
{
    Waiting,
    Running,
    Done,
    Failed,
}

internal readonly record struct ExportJobProgress(string Name, double Progress);

internal readonly record struct ExportProgressSnapshot(
    double Overall,
    int Finished,
    int Total,
    IReadOnlyList<ExportJobProgress> Running);

/// <summary>並列書き出しの進捗。曲の長さ（フレーム数）で全体を重み付けする。</summary>
internal sealed class ExportProgressTracker
{
    private readonly object _gate = new();
    private readonly string[] _names;
    private readonly double[] _weights;
    private readonly double[] _progress;
    private readonly ExportJobState[] _states;
    private readonly IProgress<ExportProgressSnapshot>? _sink;

    public ExportProgressTracker(
        IReadOnlyList<string> names,
        IReadOnlyList<long> frameCounts,
        IProgress<ExportProgressSnapshot>? sink = null)
    {
        _names = names.ToArray();
        _weights = new double[_names.Length];
        _progress = new double[_names.Length];
        _states = new ExportJobState[_names.Length];
        for (var i = 0; i < _names.Length; i++)
        {
            _weights[i] = Math.Max(1, i < frameCounts.Count ? frameCounts[i] : 1);
            _states[i] = ExportJobState.Waiting;
        }

        _sink = sink;
    }

    public void Report(int index, double progress, ExportJobState state)
    {
        if ((uint)index >= (uint)_names.Length)
        {
            return;
        }

        lock (_gate)
        {
            _progress[index] = Math.Clamp(progress, 0, 1);
            _states[index] = state;
            if (state is ExportJobState.Done or ExportJobState.Failed)
            {
                _progress[index] = 1;
            }

            _sink?.Report(CaptureLocked());
        }
    }

    public ExportProgressSnapshot Capture()
    {
        lock (_gate)
        {
            return CaptureLocked();
        }
    }

    private ExportProgressSnapshot CaptureLocked()
    {
        var weightSum = 0d;
        var weighted = 0d;
        var finished = 0;
        List<ExportJobProgress>? running = null;
        for (var i = 0; i < _names.Length; i++)
        {
            weightSum += _weights[i];
            weighted += _progress[i] * _weights[i];
            if (_states[i] is ExportJobState.Done or ExportJobState.Failed)
            {
                finished++;
            }
            else if (_states[i] == ExportJobState.Running)
            {
                running ??= [];
                running.Add(new ExportJobProgress(_names[i], _progress[i]));
            }
        }

        var overall = weightSum <= 0 ? 0 : weighted / weightSum;
        IReadOnlyList<ExportJobProgress> rows = running ?? (IReadOnlyList<ExportJobProgress>)[];
        return new ExportProgressSnapshot(overall, finished, _names.Length, rows);
    }
}
