using System.IO.MemoryMappedFiles;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// ファイル全体の STFT を OS テンポラリ上のメモリマップに置く。
/// スクロールは FFT せず、このキャッシュから列を読む。
/// </summary>
internal sealed class SpectrogramCache : IDisposable
{
    private const int HeaderSize = 64;
    private const string TempFolderName = "MGA-Sonic-Anvil";
    private const string FilePrefix = "spec-";

    private readonly float[] _window = new float[SpectrogramEngine.FftSize];
    private readonly float _windowSum;
    private readonly object _gate = new();
    private Session? _published;
    private Session? _building;
    private CancellationTokenSource? _buildCancel;
    private int _buildGeneration;
    private bool _disposed;

    public SpectrogramCache()
    {
        SpectrogramEngine.FillHann(_window, out _windowSum);
        TryDeleteStaleTempFiles();
    }

    public bool IsReady
    {
        get
        {
            lock (_gate)
            {
                return _published is { IsComplete: true };
            }
        }
    }

    public void Ensure(AudioDocument document, Action? onUpdated)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_published is { } ready && ready.Matches(document) && ready.IsComplete)
            {
                return;
            }

            if (_building is { } work && work.Matches(document))
            {
                return;
            }

            StartBuildNoLock(document, onUpdated);
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            CancelBuildNoLock();
            _published?.Dispose();
            _published = null;
        }
    }

    public bool TryColor(long frame, double bin, out int bgra)
    {
        Session? session;
        lock (_gate)
        {
            session = _published;
            if (session is null || !session.IsComplete)
            {
                session = _building;
            }
        }

        if (session is null)
        {
            bgra = 0;
            return false;
        }

        return session.TryColor(frame, bin, out bgra);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            CancelBuildNoLock();
            _published?.Dispose();
            _published = null;
            _building?.Dispose();
            _building = null;
        }
    }

    private void StartBuildNoLock(AudioDocument document, Action? onUpdated)
    {
        CancelBuildNoLock();
        _published?.Dispose();
        _published = null;
        _buildCancel = new CancellationTokenSource();
        var token = _buildCancel.Token;
        var generation = ++_buildGeneration;
        var samples = document.Interleaved;
        var channels = Math.Max(1, document.Channels);
        var rate = document.SampleRate;
        var frames = document.FrameCount;
        var session = Session.Create(samples, channels, rate, frames);
        _building = session;
        _ = Task.Run(() => Build(session, generation, token, onUpdated), token);
    }

    private void CancelBuildNoLock()
    {
            _buildGeneration++;
            if (_buildCancel is not null)
            {
                _buildCancel.Cancel();
                _buildCancel.Dispose();
                _buildCancel = null;
            }

            _building = null;
    }

    private void Build(Session session, int generation, CancellationToken token, Action? onUpdated)
    {
        try
        {
            var bins = SpectrogramEngine.BinCount;
            var mix = new float[SpectrogramEngine.FftSize];
            var re = new double[SpectrogramEngine.FftSize];
            var im = new double[SpectrogramEngine.FftSize];
            var column = new byte[bins];
            var lastNotify = -1;
            var notifyEvery = Math.Max(1, session.ColumnCount / 10);
            for (var col = 0; col < session.ColumnCount; col++)
            {
                token.ThrowIfCancellationRequested();
                var origin = (long)col * SpectrogramEngine.Hop - SpectrogramEngine.FftSize / 2;
                SpectrogramEngine.FillMonoMix(session.Samples, session.Channels, origin, session.FrameCount, mix);
                SpectrogramEngine.AnalyzeWindow(mix, _window, _windowSum, re, im);
                SpectrogramEngine.WriteColumnLut(re.AsSpan(0, bins), column);
                session.WriteColumn(col, column);
                session.ReadyColumns = col + 1;
                if (col - lastNotify >= notifyEvery || col + 1 == session.ColumnCount)
                {
                    lastNotify = col;
                    onUpdated?.Invoke();
                }
            }

            lock (_gate)
            {
                if (_disposed || generation != _buildGeneration)
                {
                    session.Dispose();
                    return;
                }

                _published?.Dispose();
                _published = session;
                if (ReferenceEquals(_building, session))
                {
                    _building = null;
                }
            }

            onUpdated?.Invoke();
        }
        catch (OperationCanceledException)
        {
            session.Dispose();
        }
        catch
        {
            session.Dispose();
            lock (_gate)
            {
                if (ReferenceEquals(_building, session))
                {
                    _building = null;
                }
            }
        }
    }

    private static void TryDeleteStaleTempFiles()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), TempFolderName);
            if (!Directory.Exists(dir))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var path in Directory.EnumerateFiles(dir, FilePrefix + "*.bin"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class Session : IDisposable
    {
        private readonly MemoryMappedFile _map;
        private readonly MemoryMappedViewAccessor _view;
        private readonly FileStream _stream;
        private int _readyColumns;
        private bool _disposed;

        private Session(
            FileStream stream,
            MemoryMappedFile map,
            MemoryMappedViewAccessor view,
            float[] samples,
            int channels,
            int sampleRate,
            long frameCount,
            int columnCount)
        {
            _stream = stream;
            _map = map;
            _view = view;
            Samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
            FrameCount = frameCount;
            ColumnCount = columnCount;
        }

        public float[] Samples { get; }

        public int Channels { get; }

        public int SampleRate { get; }

        public long FrameCount { get; }

        public int ColumnCount { get; }

        public int ReadyColumns
        {
            get => Volatile.Read(ref _readyColumns);
            set => Volatile.Write(ref _readyColumns, value);
        }

        public bool IsComplete => ReadyColumns >= ColumnCount;

        public bool Matches(AudioDocument document) =>
            ReferenceEquals(Samples, document.Interleaved)
            && Channels == document.Channels
            && SampleRate == document.SampleRate
            && FrameCount == document.FrameCount;

        public static Session Create(float[] samples, int channels, int sampleRate, long frameCount)
        {
            var columns = SpectrogramEngine.ColumnCount(frameCount);
            var bins = SpectrogramEngine.BinCount;
            var capacity = HeaderSize + (long)columns * bins;
            var dir = Path.Combine(Path.GetTempPath(), TempFolderName);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FilePrefix + Guid.NewGuid().ToString("N") + ".bin");
            var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                4096,
                FileOptions.DeleteOnClose);
            stream.SetLength(capacity);
            var map = MemoryMappedFile.CreateFromFile(
                stream,
                mapName: null,
                capacity,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true);
            var view = map.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.ReadWrite);
            view.WriteArray(0, "MGASPEC1"u8.ToArray(), 0, 8);
            view.Write(8, 1);
            view.Write(12, sampleRate);
            view.Write(16, channels);
            view.Write(20, SpectrogramEngine.FftSize);
            view.Write(24, SpectrogramEngine.Hop);
            view.Write(28, bins);
            view.Write(32, columns);
            view.Write(36, frameCount);
            return new Session(stream, map, view, samples, channels, sampleRate, frameCount, columns);
        }

        public void WriteColumn(int column, byte[] values)
        {
            var offset = HeaderSize + (long)column * SpectrogramEngine.BinCount;
            _view.WriteArray(offset, values, 0, values.Length);
        }

        public bool TryColor(long frame, double bin, out int bgra)
        {
            var ready = ReadyColumns;
            if (ready <= 0 || _disposed)
            {
                bgra = 0;
                return false;
            }

            var lastCol = Math.Min(ColumnCount, ready) - 1;
            if (lastCol < 0)
            {
                bgra = 0;
                return false;
            }

            var colF = Math.Clamp(frame / (double)SpectrogramEngine.Hop, 0, lastCol);
            var c0 = (int)Math.Floor(colF);
            var c1 = Math.Min(lastCol, c0 + 1);
            var tx = colF - c0;

            var lastBin = SpectrogramEngine.BinCount - 1;
            var binF = Math.Clamp(bin, 0, lastBin);
            var b0 = (int)Math.Floor(binF);
            var b1 = Math.Min(lastBin, b0 + 1);
            var ty = binF - b0;

            var v00 = ReadLut(c0, b0);
            var v01 = ReadLut(c0, b1);
            var v10 = ReadLut(c1, b0);
            var v11 = ReadLut(c1, b1);
            var v0 = v00 + (v01 - v00) * ty;
            var v1 = v10 + (v11 - v10) * ty;
            var mixed = (byte)Math.Clamp(Math.Round(v0 + (v1 - v0) * tx), 0, 255);
            bgra = SpectrogramEngine.ColorFromLutByte(mixed);
            return true;
        }

        private byte ReadLut(int column, int bin)
        {
            var offset = HeaderSize + (long)column * SpectrogramEngine.BinCount + bin;
            return _view.ReadByte(offset);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _view.Dispose();
            _map.Dispose();
            _stream.Dispose();
        }
    }
}
