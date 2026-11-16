using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed partial class LibraryBrowserView
{
    private void SchedulePlaylistWaveforms()
    {
        CancelPlaylistWaveforms(clearBusy: false);
        if (!IsWaveformColumnEffectivelyVisible() || _items.Count == 0)
        {
            SetPlaylistWaveBusy(false);
            return;
        }

        var cts = new CancellationTokenSource();
        _playlistWaveCts = cts;
        var generation = ++_playlistWaveGeneration;
        var token = cts.Token;
        SetPlaylistWaveBusy(true);
        _ = RunPlaylistWaveformsAsync(generation, token);
    }

    private void CancelPlaylistWaveforms(bool clearBusy = true)
    {
        _playlistWaveGeneration++;
        if (_playlistWaveCts is null)
        {
            if (clearBusy)
            {
                SetPlaylistWaveBusy(false);
            }

            return;
        }

        try
        {
            _playlistWaveCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _playlistWaveCts.Dispose();
        _playlistWaveCts = null;
        if (clearBusy)
        {
            SetPlaylistWaveBusy(false);
        }
    }

    private bool IsWaveformColumnEffectivelyVisible()
    {
        if (!_columns.TryGetValue(LibraryFileColumn.Waveform, out var column)
            || column.Visibility != Visibility.Visible)
        {
            return false;
        }

        return LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Waveform,
            _visibleColumns,
            CurrentUsedColumns());
    }

    /// <summary>設定の Wave / MP3 それぞれの S/M/L を反映する。</summary>
    public void SetPlaylistWaveformOptions(
        LibraryPlaylistWaveformSize waveSize,
        LibraryPlaylistWaveformSize mp3Size)
    {
        _playlistWaveSizeWave = LibraryPlaylistWaveformSizes.Clamp(waveSize);
        _playlistWaveSizeMp3 = LibraryPlaylistWaveformSizes.Clamp(mp3Size);
        ApplyActiveColumnVisibility(notify: false);
        RefreshPlaylistWaveformSize(reschedule: true);
    }

    private void RefreshPlaylistWaveformSize(bool reschedule)
    {
        var next = LibraryPlaylistWaveformSizes.Resolve(
            _playlistWaveSizeWave,
            _playlistWaveSizeMp3,
            _rows);
        LibraryPlaylistWaveform.SetEffectiveSize(next);
        ApplyWaveformColumnWidth();
        if (reschedule)
        {
            SchedulePlaylistWaveforms();
        }
    }

    private void ApplyWaveformColumnWidth()
    {
        if (!_columns.TryGetValue(LibraryFileColumn.Waveform, out var column))
        {
            return;
        }

        SetColumnPixelWidth(column, LibraryPlaylistWaveform.ColumnWidth);
    }

    public void ApplyCompletedPlaylistWaveform(string path, PeakPyramid peaks)
    {
        if (!IsWaveformColumnEffectivelyVisible())
        {
            return;
        }

        LibraryPlaylistWaveform.SetFromCompletedPeaks(path, peaks);
    }

    private async Task RunPlaylistWaveformsAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            // リスト描画と入力を優先。ApplicationIdle のあとで波形を埋め始める。
            try
            {
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (generation != _playlistWaveGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var barCount = LibraryPlaylistWaveform.BarCount;
            var paths = new List<(string Path, PeakPyramid? Peaks)>(_items.Count);
            foreach (var row in _items)
            {
                if (row.Tag is not DocumentSession session
                    || session.Document.SourcePath is not { Length: > 0 } path)
                {
                    continue;
                }

                if (LibraryPlaylistWaveform.HasEntry(path, barCount))
                {
                    continue;
                }

                paths.Add((path, PeakPyramid.CanBuildPlaylistBarsFromPeaks(session.Document.Peaks)
                    ? session.Document.Peaks
                    : null));
            }

            foreach (var (path, peaks) in paths)
            {
                if (generation != _playlistWaveGeneration || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (LibraryPlaylistWaveform.BarCount != barCount)
                {
                    return;
                }

                if (LibraryPlaylistWaveform.HasEntry(path, barCount))
                {
                    continue;
                }

                float[] bars;
                try
                {
                    bars = await Task.Run(
                        () => LibraryPlaylistWaveform.BuildBars(path, peaks, barCount, cancellationToken),
                        cancellationToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    bars = new float[barCount];
                }

                if (generation != _playlistWaveGeneration || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // 走査中に先読みピークが埋めたら、推定尺の粗い棒で上書きしない。
                LibraryPlaylistWaveform.TrySet(path, barCount, bars);

                // 1 曲ごとに UI へ制御を返す（キー・スクロールを止めない）。
                try
                {
                    await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            FinishPlaylistWaveforms(generation);
        }
    }

    private void FinishPlaylistWaveforms(int generation)
    {
        if (generation != _playlistWaveGeneration)
        {
            return;
        }

        if (_playlistWaveCts is not null)
        {
            _playlistWaveCts.Dispose();
            _playlistWaveCts = null;
        }

        SetPlaylistWaveBusy(false);
    }

    private void SchedulePlaylistLoudness()
    {
        CancelPlaylistLoudness(clearBusy: false);
        if (!IsLoudnessColumnEffectivelyVisible() || _items.Count == 0)
        {
            SetPlaylistLoudnessBusy(false);
            return;
        }

        var cts = new CancellationTokenSource();
        _playlistLoudnessCts = cts;
        var generation = ++_playlistLoudnessGeneration;
        var token = cts.Token;
        SetPlaylistLoudnessBusy(true);
        _ = RunPlaylistLoudnessAsync(generation, token);
    }

    private void CancelPlaylistLoudness(bool clearBusy = true)
    {
        _playlistLoudnessGeneration++;
        if (_playlistLoudnessCts is null)
        {
            if (clearBusy)
            {
                SetPlaylistLoudnessBusy(false);
            }

            return;
        }

        try
        {
            _playlistLoudnessCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _playlistLoudnessCts.Dispose();
        _playlistLoudnessCts = null;
        if (clearBusy)
        {
            SetPlaylistLoudnessBusy(false);
        }
    }

    private bool IsLoudnessColumnEffectivelyVisible()
    {
        if (!_columns.TryGetValue(LibraryFileColumn.Loudness, out var column)
            || column.Visibility != Visibility.Visible)
        {
            return false;
        }

        return LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Loudness,
            _visibleColumns,
            CurrentUsedColumns());
    }

    private async Task RunPlaylistLoudnessAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (generation != _playlistLoudnessGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // CreateRow でキャッシュ済みは既に Tags へ載っている。ここは未計測の本計測だけ。
            var sessions = new List<DocumentSession>(_items.Count);
            foreach (var row in _items)
            {
                if (row.Tag is not DocumentSession session)
                {
                    continue;
                }

                var document = session.Document;
                if (!AudioCodec.IsUncompressedPcm(document.SourceKind)
                    || document.SourcePath is not { Length: > 0 } sourcePath)
                {
                    continue;
                }

                if (document.Tags.HasFileLoudness)
                {
                    if (LibraryMediaCache.TryGetFileLoudness(sourcePath, out _))
                    {
                        continue;
                    }

                    document.ApplyTags(document.Tags.WithoutFileLoudness());
                }

                sessions.Add(session);
            }

            var pending = new List<DocumentSession>(16);
            foreach (var session in sessions)
            {
                if (generation != _playlistLoudnessGeneration || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!IsLoudnessColumnEffectivelyVisible())
                {
                    return;
                }

                var document = session.Document;
                if (document.Tags.HasFileLoudness
                    || document.SourcePath is not { Length: > 0 } path)
                {
                    continue;
                }

                FileLoudness? measured;
                try
                {
                    measured = await Task.Run(
                        () =>
                        {
                            // 行作成後に5項目キャッシュが載った／他経路で埋まった場合。
                            if (LibraryMediaCache.TryGetFileLoudness(path, out var cached))
                            {
                                return (FileLoudness?)cached;
                            }

                            if (!LoudnessMeterEngine.TryMeasureFileLoudness(
                                    document,
                                    out var value,
                                    cancellationToken))
                            {
                                return null;
                            }

                            LibraryMediaCache.PutFileLoudness(path, value);
                            return value;
                        },
                        cancellationToken).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    continue;
                }

                if (generation != _playlistLoudnessGeneration || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (measured is null)
                {
                    continue;
                }

                document.ApplyTags(document.Tags.WithFileLoudness(measured.Value));
                pending.Add(session);

                // まとめて行を差し替え、選択の再適用・フォーカス奪取を毎回やらない。
                if (pending.Count < 16)
                {
                    continue;
                }

                RefreshLoudnessRows(pending);
                pending.Clear();
                try
                {
                    await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }

            if (pending.Count > 0
                && generation == _playlistLoudnessGeneration
                && !cancellationToken.IsCancellationRequested)
            {
                RefreshLoudnessRows(pending);
            }
        }
        finally
        {
            FinishPlaylistLoudness(generation);
        }
    }

    private void FinishPlaylistLoudness(int generation)
    {
        if (generation != _playlistLoudnessGeneration)
        {
            return;
        }

        if (_playlistLoudnessCts is not null)
        {
            _playlistLoudnessCts.Dispose();
            _playlistLoudnessCts = null;
        }

        SetPlaylistLoudnessBusy(false);
    }

    /// <summary>フォルダ走査などファイル列挙の待ち表示。</summary>
    public void SetPlaylistEnumerateBusy(bool busy)
    {
        if (_playlistEnumerateBusy == busy)
        {
            return;
        }

        _playlistEnumerateBusy = busy;
        RaisePlaylistMediaBusyChanged();
    }

    private void SetPlaylistWaveBusy(bool busy)
    {
        if (_playlistWaveBusy == busy)
        {
            return;
        }

        _playlistWaveBusy = busy;
        RaisePlaylistMediaBusyChanged();
    }

    private void SetPlaylistLoudnessBusy(bool busy)
    {
        if (_playlistLoudnessBusy == busy)
        {
            return;
        }

        _playlistLoudnessBusy = busy;
        RaisePlaylistMediaBusyChanged();
    }

    private void RaisePlaylistMediaBusyChanged()
    {
        var busy = IsPlaylistMediaBusy;
        // フェードアウト中は直前の文言を残す（空文字＋赤枠だけが見えるのを防ぐ）。
        if (busy)
        {
            RefreshPlaylistBusyDetail();
        }

        if (busy == _playlistMediaBusyNotified)
        {
            return;
        }

        _playlistMediaBusyNotified = busy;
        SyncPlaylistBusyChrome(busy);
        PlaylistMediaBusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncPlaylistBusyChrome(bool busy)
    {
        StopPlaylistBusyAnimations();
        if (busy)
        {
            ShowPlaylistBusyChrome();
            return;
        }

        BeginPlaylistBusyFadeOut();
    }

    private void StopPlaylistBusyAnimations()
    {
        _playlistBusyFadeTicket++;
        _playlistBusyBadge.BeginAnimation(UIElement.OpacityProperty, null);
        if (_playlistBusyBadgeBack is not null)
        {
            _playlistBusyBadgeBack.BeginAnimation(Brush.OpacityProperty, null);
        }
    }

    private void ShowPlaylistBusyChrome()
    {
        RefreshPlaylistBusyDetail();
        _playlistBusyBadge.Visibility = Visibility.Visible;
        _playlistBusyBadge.Opacity = 1;

        var back = new SolidColorBrush(Theme.Get("PlaylistBusyBadgeBackBrush"));
        _playlistBusyBadgeBack = back;
        _playlistBusyBadge.Background = back;
        back.BeginAnimation(
            Brush.OpacityProperty,
            new DoubleAnimation
            {
                From = 1,
                To = 0.25,
                Duration = TimeSpan.FromSeconds(PlaylistBusyBlinkSeconds),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    private void BeginPlaylistBusyFadeOut()
    {
        if (_playlistBusyBadge.Visibility != Visibility.Visible)
        {
            HidePlaylistBusyChrome();
            return;
        }

        if (_playlistBusyBadgeBack is not null)
        {
            _playlistBusyBadgeBack.Opacity = 1;
        }

        var ticket = _playlistBusyFadeTicket;
        var fade = CreatePlaylistBusyFadeOut(_playlistBusyBadge.Opacity);
        fade.Completed += (_, _) =>
        {
            if (ticket != _playlistBusyFadeTicket || IsPlaylistMediaBusy)
            {
                return;
            }

            HidePlaylistBusyChrome();
        };
        _playlistBusyBadge.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static DoubleAnimation CreatePlaylistBusyFadeOut(double from) =>
        new()
        {
            From = from,
            To = 0,
            Duration = TimeSpan.FromSeconds(PlaylistBusyFadeOutSeconds),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn },
        };

    private void HidePlaylistBusyChrome()
    {
        _playlistBusyBadge.BeginAnimation(UIElement.OpacityProperty, null);
        if (_playlistBusyBadgeBack is not null)
        {
            _playlistBusyBadgeBack.BeginAnimation(Brush.OpacityProperty, null);
            _playlistBusyBadgeBack = null;
        }

        if (_playlistBusyBadge.Child is TextBlock text)
        {
            text.Text = string.Empty;
        }

        _playlistBusyBadge.Visibility = Visibility.Collapsed;
        _playlistBusyBadge.Opacity = 1;
        _playlistBusyBadge.SetResourceReference(Border.BackgroundProperty, "PlaylistBusyBadgeBackBrush");
    }

    private void RefreshPlaylistBusyDetail()
    {
        if (_playlistBusyBadge.Child is TextBlock text)
        {
            text.Text = FormatPlaylistBusyDetail(
                _playlistEnumerateBusy,
                _playlistWaveBusy,
                _playlistLoudnessBusy);
        }
    }

    internal static string FormatPlaylistBusyDetail(
        bool enumerateBusy,
        bool waveBusy,
        bool loudnessBusy)
    {
        var parts = new List<string>(3);
        if (enumerateBusy)
        {
            parts.Add(UiStrings.PlaylistBusyEnumerate);
        }

        if (waveBusy)
        {
            parts.Add(UiStrings.PlaylistBusyWaveform);
        }

        if (loudnessBusy)
        {
            parts.Add(UiStrings.PlaylistBusyLoudness);
        }

        return parts.Count == 0
            ? string.Empty
            : string.Join(UiStrings.PlaylistBusyDetailSeparator, parts);
    }

    internal static Border CreatePlaylistBusyBadge()
    {
        var text = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "LatchToggleOnForeBrush");
        var badge = new Border
        {
            Child = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
            Focusable = false,
            Visibility = Visibility.Collapsed,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "PlaylistBusyBadgeBackBrush");
        DesignMetrics.BindCorner(badge);
        return badge;
    }

    /// <summary>
    /// ラウドネス列だけ更新。列再計算や EnsureListFocused はしない（Shift+上下の選択を壊さない）。
    /// </summary>
    private void RefreshLoudnessRows(IReadOnlyList<DocumentSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        var touch = new HashSet<DocumentSession>(sessions);
        var preserve = SelectedSessions;
        var active = SelectedSession;
        var keepFocus = _grid.IsKeyboardFocusWithin;
        var changed = false;
        var nextRows = _rows.ToArray();
        BeginRowSync();
        try
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Tag is not DocumentSession session || !touch.Contains(session))
                {
                    continue;
                }

                var next = CreateRowWithDefaults(session);
                next.GroupKey = LibraryFileList.GroupLabel(next, _group);
                if (LibraryFileList.SameContent(_items[i], next))
                {
                    continue;
                }

                for (var r = 0; r < nextRows.Length; r++)
                {
                    if (ReferenceEquals(nextRows[r].Tag, session))
                    {
                        nextRows[r] = next;
                        break;
                    }
                }

                _items[i] = next;
                changed = true;
            }

            if (changed)
            {
                _rows = nextRows;
            }

            if (preserve.Length > 0)
            {
                ApplyRowSelectionCore(active ?? preserve[0], preserve);
            }
        }
        finally
        {
            EndRowSync();
        }

        // フォーカスが外れたときだけ戻す。毎回 Ensure するとキー選択が阻害される。
        if (keepFocus && !_grid.IsKeyboardFocusWithin)
        {
            EnsureListFocused();
        }

        NotePlaylistSelectionChanged();
    }

}
