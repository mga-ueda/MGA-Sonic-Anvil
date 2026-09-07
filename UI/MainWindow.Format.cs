using System.Windows;
using System.Windows.Input;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private readonly BusyGlassOverlay _busyGlass = new();
    private bool _formatConvertBusy;

    private void PromptFormatConvert(FormatConvertKind kind)
    {
        if (_formatConvertBusy)
        {
            return;
        }

        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CloseFadeCurvePicker();
        if (_formatMenu is { IsOpen: true })
        {
            _formatMenu.IsOpen = false;
        }

        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _formatKind = kind;
        _formatPreviewResumeFrame = _document.CursorFrame;
        var current = kind switch
        {
            FormatConvertKind.BitDepth => _document.BitsPerSample,
            FormatConvertKind.Channels => _document.Channels,
            _ => _document.SampleRate,
        };
        var menu = FormatConvertPicker.Show(
            this,
            kind,
            current,
            value => ApplyFormatConvert(kind, value),
            kind is FormatConvertKind.SampleRate or FormatConvertKind.BitDepth
                ? value => PreviewFormatConvert(kind, value)
                : null);
        _formatMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopFormatPreview();
            if (ReferenceEquals(_formatMenu, menu))
            {
                _formatMenu = null;
            }
        };
    }

    private bool CloseFormatConvertPicker()
    {
        if (_formatMenu is not { IsOpen: true })
        {
            return false;
        }

        _formatMenu.IsOpen = false;
        return true;
    }

    private bool TryHandleFormatMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_formatMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key == Key.S && modifiers == ModifierKeys.None)
        {
            PromptFormatConvert(FormatConvertKind.SampleRate);
            return true;
        }

        if (key == Key.B && modifiers == ModifierKeys.None)
        {
            PromptFormatConvert(FormatConvertKind.BitDepth);
            return true;
        }

        if (key == Key.C && modifiers == ModifierKeys.None)
        {
            PromptFormatConvert(FormatConvertKind.Channels);
            return true;
        }

        if (FormatConvertPicker.IsCustomBoxFocused(_formatMenu))
        {
            return false;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            if (FormatConvertPicker.TryHighlightedValue(_formatMenu, out var previewValue))
            {
                PreviewFormatConvert(_formatKind, previewValue);
            }

            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            if (FormatConvertPicker.TryHighlightedValue(_formatMenu, out var value))
            {
                ApplyFormatConvert(_formatKind, value);
            }

            return true;
        }

        if (modifiers == ModifierKeys.None
            && TryDigitPercent(key, out var digit)
            && digit > 0)
        {
            var index = (int)Math.Round(digit * 10d) - 1;
            return FormatConvertPicker.HighlightByIndex(_formatMenu, index);
        }

        return false;
    }

    private void PreviewFormatConvert(FormatConvertKind kind, int value)
    {
        if (_document is null || _formatPreviewToggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _formatSpaceTick < 120)
        {
            return;
        }

        _formatSpaceTick = now;
        _formatPreviewToggling = true;
        try
        {
            if (_formatPreviewing && _player.IsPlaying)
            {
                StopFormatPreview();
                return;
            }

            if (!TryBuildPreviewDocument(kind, value, out var preview, out var start, out var range))
            {
                return;
            }

            StartFormatPreviewPlayback(preview, start, range);
        }
        finally
        {
            _formatPreviewToggling = false;
        }
    }

    private bool TryBuildPreviewDocument(
        FormatConvertKind kind,
        int value,
        out AudioDocument preview,
        out long startFrame,
        out WaveSelection playRange)
    {
        preview = null!;
        startFrame = 0;
        playRange = WaveSelection.Empty;
        if (_document is null)
        {
            return false;
        }

        var source = _document;
        var samples = source.Interleaved;
        var rate = source.SampleRate;
        var channels = source.Channels;
        var bits = source.BitsPerSample;
        if (kind == FormatConvertKind.SampleRate)
        {
            if (!FormatConvert.IsValidSampleRate(value) || value == rate)
            {
                return false;
            }

            samples = FormatConvert.Resample(samples, channels, rate, value);
            rate = value;
        }
        else if (kind == FormatConvertKind.BitDepth)
        {
            if (!FormatConvert.IsValidBitDepth(value) || value == bits)
            {
                return false;
            }

            if (value < bits)
            {
                samples = FormatConvert.Quantize(samples, value);
            }

            bits = value;
        }
        else
        {
            return false;
        }

        preview = new AudioDocument(samples, rate, channels, bits, AudioFileKind.Wave, null);
        var destFrames = preview.FrameCount;
        var sourceRange = source.Selection.IsEmpty
            ? PreviewWindow(source)
            : source.Selection;
        playRange = FormatConvert.ScaleSelection(sourceRange, source.SampleRate, rate, destFrames);
        if (playRange.IsEmpty)
        {
            playRange = new WaveSelection(0, destFrames);
        }

        startFrame = playRange.StartFrame;
        return destFrames > 0;
    }

    private static WaveSelection PreviewWindow(AudioDocument document)
    {
        var start = Math.Clamp(document.CursorFrame, 0, Math.Max(0, document.FrameCount));
        var length = Math.Min(document.FrameCount - start, document.SampleRate * 5L);
        if (length <= 0)
        {
            start = 0;
            length = Math.Min(document.FrameCount, document.SampleRate * 5L);
        }

        return new WaveSelection(start, start + length);
    }

    private void StartFormatPreviewPlayback(AudioDocument preview, long startFrame, WaveSelection range)
    {
        try
        {
            PausePlaybackSoft();
            _meter.Reset();
            _player.Prepare(preview, startFrame, range, loop: false);

            _formatPreviewing = true;
            _formatPreviewStartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Transport.SetPlaying(true);
        }
        catch (Exception ex)
        {
            _formatPreviewing = false;
            PausePlaybackSoft();
            RebindOriginalDocument();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopFormatPreview()
    {
        if (!_formatPreviewing)
        {
            RebindOriginalDocument();
            return;
        }

        _formatPreviewing = false;
        PausePlaybackSoft();
        RebindOriginalDocument();
        if (_document is not null)
        {
            SeekFrame(_formatPreviewResumeFrame);
        }
    }

    private void RebindOriginalDocument()
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            _player.Prepare(_document, _document.CursorFrame, null, loop: false);
            if (_player.IsPlaying)
            {
                _player.Pause();
            }
        }
        catch
        {
            // デバイス再初期化に失敗してもプレビュー終了は続行する。
        }
    }

    private void ApplyFormatConvert(FormatConvertKind kind, int value)
    {
        if (_document is null || _formatConvertBusy)
        {
            return;
        }

        CloseFormatConvertPicker();
        StopFormatPreview();
        if (kind == FormatConvertKind.SampleRate)
        {
            _ = ApplySampleRateConvertAsync(value);
            return;
        }

        var command = kind switch
        {
            FormatConvertKind.BitDepth => ProcessEdits.ConvertBitDepth(_document, value),
            FormatConvertKind.Channels => ProcessEdits.ConvertChannels(_document, value),
            _ => null,
        };
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterEdit();
    }

    private async Task ApplySampleRateConvertAsync(int destRate)
    {
        if (_document is null || _formatConvertBusy)
        {
            return;
        }

        if (!FormatConvert.IsValidSampleRate(destRate) || destRate == _document.SampleRate)
        {
            return;
        }

        var document = _document;
        var sourceRate = document.SampleRate;
        _formatConvertBusy = true;
        Exception? error = null;
        IEditCommand? command = null;
        try
        {
            ShowSampleRateBusyGlass();
            var progress = new Progress<double>(p => _busyGlass.SetProgress(p));
            command = await Task.Run(() => ProcessEdits.ConvertSampleRate(document, destRate, progress))
                .ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_document, document) || command is null)
            {
                return;
            }

            _history.Do(document, command);
            if (sourceRate > 0)
            {
                var factor = document.SampleRate / (double)sourceRate;
                Waveform.SetViewStartExternal(Waveform.ViewStart * factor);
            }

            AfterEdit();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _formatConvertBusy = false;
            if (error is not null)
            {
                _busyGlass.HideOverlay();
            }
            else
            {
                _busyGlass.BeginFadeOut();
            }
        }

        if (error is not null && IsLoaded)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowSampleRateBusyGlass()
    {
        RootChrome.UpdateLayout();
        RootDock.UpdateLayout();
        _busyGlass.ShowOverlay(
            RootChrome,
            RootDock,
            GetBusyGlassCoverBounds(),
            UiStrings.OverlaySampleRateConvert);
    }

    private Rect GetBusyGlassCoverBounds()
    {
        var host = RootChrome;
        return new Rect(
            0,
            0,
            Math.Max(0, host.ActualWidth),
            Math.Max(0, host.ActualHeight));
    }

    private void SyncBusyGlassOverlayBounds()
    {
        if (!_busyGlass.IsShowingBusy)
        {
            return;
        }

        _busyGlass.SyncBounds(GetBusyGlassCoverBounds());
    }
}
