using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmSysChar = 0x0106;
    private const int WhKeyboardLl = 13;
    private const int LlkhfExtended = 0x01;
    private const int LlkhfAltDown = 0x20;
    private const int LlkhfUp = 0x80;
    private const int KfAltDown = 0x20000000;

    /// <summary>Alt を離した直後、非同期状態や SYSKEY が残っても修飾なしとして扱う時間。</summary>
    private const int AltReleaseGraceMs = 250;

    private static readonly MethodInfo? LeaveMenuModeMethod = typeof(HwndSource).GetMethod(
        "OnLeaveMenuMode",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? KeyScanCodeField = typeof(KeyEventArgs).GetField(
        "_scanCode",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? KeyExtendedField = typeof(KeyEventArgs).GetField(
        "_isExtendedKey",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HashSet<int> _numpadShiftScansDown = new();
    private IntPtr _numpadShiftHook;
    private LowLevelKeyboardProc? _numpadShiftHookProc;
    private long _numpadShiftJumpMs = -1000;
    private int _numpadShiftJumpDigit = -1;
    private long _numpadPercentJumpMs = -1000;
    private int _numpadPercentJumpDigit = -1;
    private readonly HashSet<int> _altNumpadDown = new();
    private readonly HashSet<int> _staleSysNumpadDown = new();
    private int _altDownMask;
    private long _altReleasedAt = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHook
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsVkDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static bool IsNumLockToggled() => (GetKeyState(0x90) & 1) != 0;

    private void AttachNumpadShiftHook()
    {
        // 低レベルフックはキューに入る前。ここで捨てないと WPF は Home／End として処理する。
        _numpadShiftHookProc = OnNumpadShiftLowLevel;
        _numpadShiftHook = SetWindowsHookEx(WhKeyboardLl, _numpadShiftHookProc, GetModuleHandle(null), 0);
        ComponentDispatcher.ThreadFilterMessage += OnNumpadShiftFilter;
        Closed += (_, _) =>
        {
            ComponentDispatcher.ThreadFilterMessage -= OnNumpadShiftFilter;
            if (_numpadShiftHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_numpadShiftHook);
                _numpadShiftHook = IntPtr.Zero;
            }
        };
    }

    private bool IsOurWindowForeground()
    {
        var root = new WindowInteropHelper(this).Handle;
        return root != IntPtr.Zero && GetForegroundWindow() == root;
    }

    private IntPtr OnNumpadShiftLowLevel(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            try
            {
                var info = Marshal.PtrToStructure<KbdLlHook>(lParam);
                var message = (int)wParam;
                var up = (info.flags & LlkhfUp) != 0 || message is WmKeyUp or WmSysKeyUp;
                if (!IsOurWindowForeground())
                {
                    return CallNextHookEx(_numpadShiftHook, nCode, wParam, lParam);
                }

                NoteAltTransition((int)info.vkCode, up);
                var systemKey = message is WmSysKeyDown or WmSysKeyUp
                    || (info.flags & LlkhfAltDown) != 0;
                // Alt＋VK_NUMPAD を先に割合へ。移動キーに化けた Alt＋テンキーは
                // ConsumeTranslatedNumpad 側で割合へ振り分け、Shift 番号ジャンプにしない。
                if (ConsumeAltNumpadPercent((int)info.vkCode, up)
                    || ConsumeTranslatedNumpad(
                        (int)info.vkCode,
                        (int)info.scanCode,
                        extended: (info.flags & LlkhfExtended) != 0,
                        up,
                        repeat: false)
                    || ConsumeStaleSysNumpad((int)info.vkCode, systemKey, up, repeat: false))
                {
                    return (IntPtr)1;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // フック内の失敗でキーを潰さない。
            }
        }

        return CallNextHookEx(_numpadShiftHook, nCode, wParam, lParam);
    }

    private void OnNumpadShiftFilter(ref MSG msg, ref bool handled)
    {
        if (handled || !IsActive)
        {
            if (!IsActive)
            {
                _numpadShiftScansDown.Clear();
                _altNumpadDown.Clear();
                _staleSysNumpadDown.Clear();
                _altDownMask = 0;
                _altReleasedAt = -1;
            }

            return;
        }

        if (msg.message == WmSysChar)
        {
            if (ConsumeStaleSysChar(unchecked((int)(msg.wParam.ToInt64() & 0xFFFF))))
            {
                handled = true;
            }

            return;
        }

        var down = msg.message is WmKeyDown or WmSysKeyDown;
        var up = msg.message is WmKeyUp or WmSysKeyUp;
        if (!down && !up)
        {
            return;
        }

        var virtualKey = unchecked((int)(msg.wParam.ToInt64() & 0xFFFF));
        var native = msg.lParam.ToInt64();
        NoteAltTransition(virtualKey, up);
        if (ConsumeAltNumpadPercent(
                virtualKey,
                up,
                repeat: LibraryPlayerMode.IsKeyRepeat(native))
            || ConsumeTranslatedNumpad(
                virtualKey,
                scanCode: (int)(native >> 16) & 0xFF,
                extended: (native & 0x01000000) != 0,
                up,
                repeat: LibraryPlayerMode.IsKeyRepeat(native))
            || ConsumeStaleSysNumpad(
                virtualKey,
                systemKey: msg.message is WmSysKeyDown or WmSysKeyUp || (native & KfAltDown) != 0,
                up,
                repeat: LibraryPlayerMode.IsKeyRepeat(native)))
        {
            handled = true;
        }
    }

    /// <summary>
    /// 変換済みテンキーなら true。キーダウンの初回だけジャンプを予約する。
    /// NumLock オンの Shift＋テンキーは Home／End 等に化ける（Shift が報告されないこともある）。
    /// Alt＋テンキーも同じ形に化けることがあり、そのときは割合ジャンプ（Shift 番号ではない）。
    /// </summary>
    private bool ConsumeTranslatedNumpad(int virtualKey, int scanCode, bool extended, bool up, bool repeat)
    {
        var scan = scanCode & 0xFF;
        if (up)
        {
            if (!_numpadShiftScansDown.Remove(scan))
            {
                return false;
            }

            // NumLock オンの Shift＋テンキーは Home 等に化けるので、離しをここで拾う。
            if (LibraryPlayerMode.TryTranslatedNumpadDigit(
                    virtualKey, scan, extended, IsNumLockToggled(), out var releasedDigit)
                && LibraryPlayerMode.IsNumpadSelectionTransportDigit(releasedDigit))
            {
                Dispatcher.BeginInvoke(() => StopEditorNumpadSelectionTransport(releasedDigit));
            }

            return true;
        }

        if (NumpadShiftPassesToText())
        {
            return false;
        }

        if (!LibraryPlayerMode.TryTranslatedNumpadDigit(virtualKey, scan, extended, IsNumLockToggled(), out var digit))
        {
            return false;
        }

        var altDown = IsAltModifierActive();
        var shiftDown = IsVkDown(0x10) || IsVkDown(0xA0) || IsVkDown(0xA1);
        var ctrlDown = IsVkDown(0x11) || IsVkDown(0xA2) || IsVkDown(0xA3);
        var winDown = IsPhysicalWinDown();
        if (LibraryPlayerMode.IsTranslatedNumpadAltPercent(altDown, shiftDown, ctrlDown, winDown))
        {
            var firstAlt = _numpadShiftScansDown.Add(scan);
            if (firstAlt && !repeat)
            {
                PostNumpadPercentJump(digit);
            }

            return true;
        }

        if (!LibraryPlayerMode.IsTranslatedNumpadShiftJump(altDown, ctrlDown, winDown))
        {
            return false;
        }

        var first = _numpadShiftScansDown.Add(scan);
        if (first && !repeat)
        {
            PostNumpadShiftJump(digit);
        }

        return true;
    }

    private bool IsPhysicalAltDown() => IsVkDown(0x12) || IsVkDown(0xA4) || IsVkDown(0xA5);

    /// <summary>VK_LWIN / VK_RWIN。他アプリの Win＋Alt＋テンキー等を飲み込まないため。</summary>
    private bool IsPhysicalWinDown() => IsVkDown(0x5B) || IsVkDown(0x5C);

    /// <summary>
    /// キー処理の先頭で呼ぶ。猶予中と、Alt が物理的に上がっているときは修飾なし。
    /// </summary>
    private bool IsAltModifierActive() =>
        !LibraryPlayerMode.IsStaleAltModifier(
            IsPhysicalAltDown(),
            _altReleasedAt,
            Environment.TickCount64,
            AltReleaseGraceMs);

    private ModifierKeys EffectiveShortcutModifiers(ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Alt) == 0 || IsAltModifierActive())
        {
            return modifiers;
        }

        return modifiers & ~ModifierKeys.Alt;
    }

    private void NoteAltTransition(int virtualKey, bool up)
    {
        var bit = virtualKey switch
        {
            0xA4 => 1,
            0xA5 => 2,
            0x12 => 4,
            _ => 0,
        };
        if (bit == 0)
        {
            return;
        }

        if (!up)
        {
            _altDownMask |= bit;
            _altReleasedAt = -1;
            return;
        }

        // 左右どちらか一方のキーアップでも全部落とす。
        // ビットが残ると、単独の Alt をもう一度押すまで次のテンキーがメニュー扱いになり、
        // Win32 がシステム音を鳴らしてショートカットまで届かない。
        ClearLatchedAlt();
    }

    private void ClearLatchedAlt()
    {
        _altDownMask = 0;
        _altReleasedAt = Environment.TickCount64;
        _altNumpadDown.Clear();
        _staleSysNumpadDown.Clear();
        _numpadPercentJumpDigit = -1;
        Dispatcher.BeginInvoke(LeaveWin32MenuMode, System.Windows.Threading.DispatcherPriority.Send);
    }

    private void LeaveWin32MenuMode()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        try
        {
            LeaveMenuModeMethod?.Invoke(source, null);
        }
        catch (TargetInvocationException)
        {
            // メニューモードでなければ何もしない。
        }
    }

    private bool ConsumeAltNumpadPercent(int virtualKey, bool up, bool repeat = false)
    {
        if (virtualKey is < 0x60 or > 0x69)
        {
            return false;
        }

        if (up)
        {
            return _altNumpadDown.Remove(virtualKey);
        }

        if (!IsAltModifierActive())
        {
            return false;
        }

        var shiftDown = IsVkDown(0x10) || IsVkDown(0xA0) || IsVkDown(0xA1);
        var ctrlDown = IsVkDown(0x11) || IsVkDown(0xA2) || IsVkDown(0xA3);
        var winDown = IsPhysicalWinDown();
        if (!LibraryPlayerMode.IsAltNumpadPercentVirtualKey(
                virtualKey,
                altDown: true,
                shiftDown,
                ctrlDown,
                winDown))
        {
            return false;
        }

        if (NumpadShiftPassesToText())
        {
            return false;
        }

        var first = _altNumpadDown.Add(virtualKey);
        if (first && !repeat)
        {
            PostNumpadPercentJump(virtualKey - 0x60);
        }

        return true;
    }

    /// <summary>
    /// Alt を離したあとの WM_SYSKEY。ここで握らないと TranslateMessage が WM_SYSCHAR を作り、
    /// DefWindowProc がメニュー不一致としてシステム音を鳴らす。再生操作はここで直接始める。
    /// </summary>
    private bool ConsumeStaleSysNumpad(int virtualKey, bool systemKey, bool up, bool repeat)
    {
        var shiftDown = IsVkDown(0x10) || IsVkDown(0xA0) || IsVkDown(0xA1);
        var ctrlDown = IsVkDown(0x11) || IsVkDown(0xA2) || IsVkDown(0xA3);
        var winDown = IsPhysicalWinDown();
        if (!LibraryPlayerMode.IsStaleSysNumpad(
                virtualKey,
                systemKey,
                IsAltModifierActive(),
                shiftDown,
                ctrlDown,
                winDown))
        {
            return false;
        }

        if (IsMenuTakingNumpadKeys())
        {
            return false;
        }

        var digit = virtualKey - 0x60;
        if (up)
        {
            if (!_staleSysNumpadDown.Remove(virtualKey))
            {
                return false;
            }

            if (!IsNumpadTextInputFocused())
            {
                Dispatcher.BeginInvoke(() => ReleasePlainNumpadKey((Key)((int)Key.NumPad0 + digit)));
            }

            return true;
        }

        var first = _staleSysNumpadDown.Add(virtualKey);
        if (IsNumpadTextInputFocused())
        {
            var textDigit = digit;
            Dispatcher.BeginInvoke(() => InsertFocusedNumpadDigit(textDigit));
            return true;
        }

        var transportRepeat = repeat || !first;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsNumpadTextInputFocused() || IsMenuTakingNumpadKeys())
            {
                return;
            }

            TryHandleTransportNumpad((Key)((int)Key.NumPad0 + digit), ModifierKeys.None, transportRepeat);
        });
        return true;
    }

    private bool ConsumeStaleSysChar(int character)
    {
        if (IsNumpadTextInputFocused() || IsMenuTakingNumpadKeys() || IsAltModifierActive())
        {
            return false;
        }

        return character is >= '0' and <= '9';
    }

    private bool IsNumpadTextInputFocused() =>
        IsLibraryGroupComboFocused
        || StatusTimes.IsTimeFocused
        || IsEditingFileName
        || Waveform.IsEditingMarkerComment
        || IsTileSearchFocused
        || (IsLibraryMaximized && LibraryBrowser.IsSearchFocused);

    private bool IsMenuTakingNumpadKeys() =>
        _volumeMenu is { IsOpen: true }
        || _timeStretchMenu is { IsOpen: true }
        || _fadeMenu is { IsOpen: true }
        || _formatMenu is { IsOpen: true };

    private static void InsertFocusedNumpadDigit(int digit)
    {
        if (digit is < 0 or > 9 || Keyboard.FocusedElement is not TextBox box || box.IsReadOnly)
        {
            return;
        }

        TextCompositionManager.StartComposition(
            new TextComposition(InputManager.Current, box, ((char)('0' + digit)).ToString()));
    }

    private void ReleasePlainNumpadKey(Key key)
    {
        if (key is Key.NumPad7 or Key.NumPad9)
        {
            if (TryDigitKey(key, out var seekDigit))
            {
                StopEditorNumpadSelectionTransport(seekDigit);
            }

            if (_seekNudgeDirection != 0
                && ((key == Key.NumPad7 && _seekNudgeDirection < 0)
                    || (key == Key.NumPad9 && _seekNudgeDirection > 0)))
            {
                StopSeekNudge();
            }

            return;
        }

        if (key is Key.NumPad1 or Key.NumPad3)
        {
            if (TryDigitKey(key, out var shuttleDigit))
            {
                StopEditorNumpadSelectionTransport(shuttleDigit);
            }

            if (_playbackShuttleDirection != 0
                && ((key == Key.NumPad1 && _playbackShuttleDirection < 0)
                    || (key == Key.NumPad3 && _playbackShuttleDirection > 0)))
            {
                StopPlaybackShuttle();
            }

            return;
        }

        if (key is Key.NumPad4 or Key.NumPad6)
        {
            TryPlayLibraryAfterTrackKeyRelease();
        }
    }

    private void PostNumpadPercentJump(int digit)
    {
        var now = Environment.TickCount64;
        if (digit == _numpadPercentJumpDigit && now - _numpadPercentJumpMs < 40)
        {
            return;
        }

        _numpadPercentJumpDigit = digit;
        _numpadPercentJumpMs = now;
        var percent = digit / 10d;
        Dispatcher.BeginInvoke(() => JumpByVisiblePercent(percent, extendSelection: false));
    }

    private void PostNumpadShiftJump(int digit)
    {
        var now = Environment.TickCount64;
        if (digit == _numpadShiftJumpDigit && now - _numpadShiftJumpMs < 40)
        {
            return;
        }

        _numpadShiftJumpDigit = digit;
        _numpadShiftJumpMs = now;
        // 選択可能な波形コンテキストでは 1／3／7／9 を選択付きシーク／シャトルへ（マーカージャンプより優先）。
        if (AllowsWaveformSelectionTransport()
            && LibraryPlayerMode.IsNumpadSelectionTransportDigit(digit))
        {
            Dispatcher.BeginInvoke(() => BeginEditorNumpadSelectionTransport(digit));
            return;
        }

        Dispatcher.BeginInvoke(() =>
            TryHandleNumpadCueDigitJump((Key)((int)Key.NumPad0 + digit), ModifierKeys.Shift));
    }

    private bool TryConsumeTranslatedNumpadEvent(KeyEventArgs e)
    {
        if (KeyExtendedField?.GetValue(e) is not bool extended
            || KeyScanCodeField?.GetValue(e) is not int scan)
        {
            return false;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = key switch
        {
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Clear => 0x0C,
            _ => 0,
        };
        if (virtualKey == 0)
        {
            return false;
        }

        return ConsumeTranslatedNumpad(virtualKey, scan, extended, up: false, repeat: e.IsRepeat);
    }

    private bool NumpadShiftPassesToText() =>
        IsNumpadTextInputFocused() || IsMenuTakingNumpadKeys();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryConsumeTranslatedNumpadEvent(e))
        {
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        key = LibraryPlayerMode.ResolveDigitKey(key, e.ImeProcessedKey);
        // 文字入力中は修飾を残し、テンキーを入力へ渡す。それ以外は残留した Alt を外す。
        var modifiers = NumpadShiftPassesToText()
            ? Keyboard.Modifiers
            : EffectiveShortcutModifiers(Keyboard.Modifiers);
        if (MenuAccessKeys.IsContextMenuKey(key, modifiers))
        {
            if (!e.IsRepeat)
            {
                _pendingContextMenuKey = true;
            }

            e.Handled = true;
            return;
        }

        if (TryProcessShortcut(key, modifiers, e.IsRepeat))
        {
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (KeyScanCodeField?.GetValue(e) is int scan && _numpadShiftScansDown.Remove(scan & 0xFF))
        {
            if (LibraryPlayerMode.TryNumpadScanDigit(scan & 0xFF, out var releasedDigit)
                && LibraryPlayerMode.IsNumpadSelectionTransportDigit(releasedDigit))
            {
                StopEditorNumpadSelectionTransport(releasedDigit);
            }

            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        key = LibraryPlayerMode.ResolveDigitKey(key, e.ImeProcessedKey);
        if (key is Key.LeftShift or Key.RightShift)
        {
            if (_transportSelectionExtend && _transportSelectionDigit >= 0)
            {
                StopEditorNumpadSelectionTransport(_transportSelectionDigit);
            }
        }

        if (key is Key.Apps or Key.F10)
        {
            if (_pendingContextMenuKey)
            {
                _pendingContextMenuKey = false;
                e.Handled = true;
                OpenContextMenuFromKeyboard();
            }

            return;
        }

        if (key is Key.NumPad7 or Key.NumPad9 or Key.J or Key.K)
        {
            if (LibraryPlayerMode.TrySelectionTransportDigit(key, out var seekDigit))
            {
                StopEditorNumpadSelectionTransport(seekDigit);
            }

            if (_seekNudgeDirection != 0
                && ((key is Key.NumPad7 or Key.J) && _seekNudgeDirection < 0
                    || (key is Key.NumPad9 or Key.K) && _seekNudgeDirection > 0))
            {
                StopSeekNudge();
            }

            return;
        }

        if (key is Key.Left or Key.Right or Key.NumPad1 or Key.NumPad3 or Key.OemComma or Key.OemPeriod)
        {
            if (_videoFrameStepDirection != 0
                && (key == Key.Left && _videoFrameStepDirection < 0
                    || key == Key.Right && _videoFrameStepDirection > 0))
            {
                StopVideoFrameStep();
            }

            if (_playbackShuttleDirection != 0
                && ((key is Key.Left or Key.NumPad1 or Key.OemComma) && _playbackShuttleDirection < 0
                    || (key is Key.Right or Key.NumPad3 or Key.OemPeriod) && _playbackShuttleDirection > 0))
            {
                StopPlaybackShuttle();
            }

            if (_markerNudgeDirection != 0
                && (key == Key.Left && _markerNudgeDirection < 0
                    || key == Key.Right && _markerNudgeDirection > 0
                    || (Keyboard.Modifiers & ModifierKeys.Alt) == 0))
            {
                StopMarkerNudge();
            }

            CommitTimelineNudgeSession();
            return;
        }

        if (key is Key.Up or Key.Down)
        {
            if (_boostNudgeDirection != 0
                && (key == Key.Up && _boostNudgeDirection > 0
                    || key == Key.Down && _boostNudgeDirection < 0
                    || (Keyboard.Modifiers & ModifierKeys.Alt) == 0))
            {
                StopSpectrogramBoostNudge();
            }

            TryPlayLibraryAfterArrowRelease();
            return;
        }

        if (key is Key.NumPad4 or Key.NumPad6)
        {
            TryPlayLibraryAfterTrackKeyRelease();
        }

        if (key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            if (!LibraryBrowser.PlaylistVisualIsPdf)
            {
                TryPlayLibraryAfterArrowRelease();
            }

            return;
        }

        if (key is Key.Add or Key.Subtract or Key.OemPlus or Key.OemMinus)
        {
            LibraryBrowser.ResetPlaylistPdfZoomLatch();
            ResetVideoLaunchScaleLatch();
            return;
        }

        if (key is Key.LeftAlt or Key.RightAlt)
        {
            // 単独 Alt のキーアップと同じく、修飾と Win32 メニュー状態をここで終わらせる。
            // Handled にしないと DefWindowProc がメニュー不一致のシステム音を鳴らす。
            ClearLatchedAlt();
            LeaveWin32MenuMode();
            if (_markerNudgeDirection != 0)
            {
                StopMarkerNudge();
            }

            if (_boostNudgeDirection != 0)
            {
                StopSpectrogramBoostNudge();
            }

            e.Handled = true;
        }

        if (_placeRepeatKind != PlaceRepeatKind.None && !IsPlaceHeld(_placeRepeatKind))
        {
            StopPlaceRepeat();
        }
    }

    private void MainWindow_AltMenuKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.LeftAlt or Key.RightAlt))
        {
            return;
        }

        ClearLatchedAlt();
        LeaveWin32MenuMode();
        e.Handled = true;
    }

    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsUiBusy)
        {
            e.Handled = true;
            return;
        }

        if (_volumeMenu is { IsOpen: true })
        {
            VolumeGainPicker.TryNudge(_volumeMenu, Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        if (_timeStretchMenu is { IsOpen: true })
        {
            TimeStretchPicker.TryNudge(_timeStretchMenu, Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        if (_silenceMenu is { IsOpen: true })
        {
            SilenceInsertPicker.TryNudge(_silenceMenu, Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        if (_silenceSelectionMenu is { IsOpen: true })
        {
            e.Handled = true;
            return;
        }

        if (IsLibraryMaximized
            && e.OriginalSource is System.Windows.DependencyObject wheelOrigin
            && (IsDescendantOf(wheelOrigin, Overview)
                || FindWaveformFromOrigin(wheelOrigin) is not null))
        {
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is not System.Windows.DependencyObject origin)
        {
            return;
        }

        if (IsDescendantOf(origin, Overview))
        {
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                Waveform.WheelTimeZoomAtPlayhead(e.Delta);
                e.Handled = true;
            }

            return;
        }

        var view = FindWaveformFromOrigin(origin);
        if (view is null)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Shift)
        {
            view.PanByVisibleFraction(e.Delta > 0 ? -0.1 : 0.1);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control)
        {
            view.WheelAmpZoom(e.Delta);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None)
        {
            view.WheelTimeZoomAtPlayhead(e.Delta);
            e.Handled = true;
        }
    }

    private static bool IsDescendantOf(
        System.Windows.DependencyObject? origin,
        System.Windows.DependencyObject target)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, target))
            {
                return true;
            }

            origin = NextAncestor(origin);
        }

        return false;
    }

    /// <summary>
    /// Run など Visual ではない要素は VisualTreeHelper が例外を投げる。論理親へ渡してからビジュアルを辿る。
    /// </summary>
    private static System.Windows.DependencyObject? NextAncestor(System.Windows.DependencyObject current)
    {
        if (current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
        {
            return System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return System.Windows.LogicalTreeHelper.GetParent(current);
    }

    private bool TryProcessShortcut(Key key, ModifierKeys modifiers, bool isRepeat = false)
    {
        if (IsUiBusy)
        {
            StopPlaybackShuttle();
            StopSeekNudge();
            StopVideoFrameStep();
            if (AppDialogKeys.IsEscape(key, modifiers))
            {
                TryRequestOpenCancel();
            }

            return true;
        }

        if (TryProcessChromeShortcut(key, modifiers))
        {
            return true;
        }

        if (TryConsumeRecordingShortcut(key, modifiers))
        {
            StopPlaybackShuttle();
            StopSeekNudge();
            StopVideoFrameStep();
            return true;
        }

        var searchHandled = TryProcessSearchFocusShortcut(key, modifiers);
        if (searchHandled is not null)
        {
            return searchHandled.Value;
        }

        var librarySession = TryProcessLibrarySessionShortcut(key, modifiers, isRepeat);
        if (librarySession is not null)
        {
            return librarySession.Value;
        }

        if (StatusTimes.IsTimeFocused)
        {
            StopPlaybackShuttle();
            return false;
        }

        if (!IsPlaybackShuttleKey(key, modifiers))
        {
            StopPlaybackShuttle();
        }

        if (key is not (Key.Left or Key.Right))
        {
            CommitTimelineNudgeSession();
        }

        if (_placeRepeatKind != PlaceRepeatKind.None && !IsContinuingPlaceKey(key, modifiers))
        {
            StopPlaceRepeat();
        }

        if (TryProcessLibraryPlaylistShortcut(key, modifiers, isRepeat))
        {
            return true;
        }

        if (TryProcessHistoryShortcut(key, modifiers))
        {
            return true;
        }

        if (TryProcessCrossfadeShortcut(key, modifiers))
        {
            return true;
        }

        if (TryProcessEscapeShortcut(key, modifiers))
        {
            return true;
        }

        if (IsEditingFileName)
        {
            return false;
        }

        if (Waveform.IsEditingMarkerComment || StatusTimes.IsTimeFocused)
        {
            return false;
        }

        if (TryHandleFormatMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (_fadeMenu is { IsOpen: true } fadeMenu)
        {
            return TryProcessFadeMenuShortcut(fadeMenu, key, modifiers);
        }

        if (modifiers == ModifierKeys.Shift && key is Key.A or Key.V)
        {
            CloseVolumeGainPicker();
            Waveform.ExitAnalysisView();
            return true;
        }

        if (TryHandleVolumeMenuShortcut(key, modifiers))
        {
            return true;
        }

        // 数字・小数点・Delete などをゲイン欄へ渡す。
        if (_volumeMenu is { IsOpen: true })
        {
            return false;
        }

        if (TryHandleTimeStretchMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (TryHandleSilenceMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (_silenceMenu is { IsOpen: true })
        {
            return false;
        }

        if (TryHandleSilenceSelectionMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (_silenceSelectionMenu is { IsOpen: true })
        {
            return false;
        }

        if (TryProcessDocumentShortcut(key, modifiers, isRepeat))
        {
            return true;
        }

        if (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RedoEdit();
            return true;
        }

        if (key == Key.Y && modifiers == ModifierKeys.Control)
        {
            RedoEdit();
            return true;
        }

        if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            UndoEdit();
            return true;
        }

        if (key == Key.A && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            // タイル表示かどうかに関係なくタブを全選択。検索フィルター中はヒットしたタブのみ。
            SelectAllTabs();
            return true;
        }

        if (key == Key.A && modifiers == ModifierKeys.Control)
        {
            // タブバー、またはタイルの帯の上ではタブの全選択。
            if (DocumentTabHost.Visibility == System.Windows.Visibility.Visible && DocumentTabHost.IsMouseOver
                || IsPointerOverTileHeader())
            {
                SelectAllTabs();
                return true;
            }

            Waveform.SelectAll();
            return true;
        }

        if (key == Key.C && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowColorDevPanel();
            return true;
        }

        if (key == Key.C && modifiers == ModifierKeys.Control)
        {
            ApplyCopy();
            return true;
        }

        if (key == Key.X && modifiers == ModifierKeys.Control)
        {
            ApplyCut();
            return true;
        }

        if (key == Key.V && modifiers == ModifierKeys.Control)
        {
            ApplyPaste();
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Control)
        {
            // プリロールは波形エリアにフォーカスがあるときだけ。
            if (LibraryPlayerMode.PrefersPrerollOnCtrlSpace(IsWaveformAreaFocused))
            {
                StartPrerollPlayback();
            }

            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.Alt)
        {
            RestartFromLastPlaybackStart();
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            // 動画本再生中：その場停止（プレイリストは出さない）。
            if (IsPlaylistVideoPlaying())
            {
                return PausePlaybackHere(immersiveVideoHold: true);
            }

            // 停止中の動画／PDF：本再生／明るい表示へ（Enter は常に先頭から。Space は再開）。
            if (IsLibraryMaximized
                && LibraryPlayerMode.PlaysLibrarySelectionOnSpace(
                    IsLibraryExplorerFocused,
                    IsLibraryFavoritesFocused)
                && IsLibraryVisualTransportTarget()
                && !IsPlaybackActive())
            {
                PlayLibrarySelectionOnceOrToggle(fromStart: true);
                return true;
            }

            return PausePlaybackHere();
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            if (IsLibraryMaximized)
            {
                // チェックモード中は再生ではなくチェック操作（ツリーは上で処理済み）。
                if (LibraryBrowser.CheckSelectMode
                    && !IsLibraryExplorerFocused
                    && !IsLibraryFavoritesFocused
                    && LibraryBrowser.IsListKeyboardFocused)
                {
                    if (!isRepeat)
                    {
                        LibraryBrowser.ApplyPlaylistSpaceCheck();
                    }

                    return true;
                }

                if (!LibraryPlayerMode.PlaysLibrarySelectionOnSpace(
                        IsLibraryExplorerFocused,
                        IsLibraryFavoritesFocused))
                {
                    return true;
                }

                PlayLibrarySelectionOnceOrToggle();
                return true;
            }

            TogglePlayback();
            return true;
        }

        // .（OemPeriod）は早送り。中央寄せは Z とテンキー .（Decimal）。
        if ((key is Key.Z or Key.Decimal) && modifiers == ModifierKeys.None)
        {
            if (IsPlaybackActive())
            {
                DetachOverviewScrubKeepPlayback();
                if (Waveform.CenterLocked)
                {
                    Waveform.UnlockCenter();
                }
                else
                {
                    Waveform.LockCenterToPlayhead();
                }
            }
            else
            {
                Waveform.CenterViewOnPlayhead();
            }

            return true;
        }

        if (key == Key.L && modifiers == ModifierKeys.Shift)
        {
            SetSampleLoopFromSelection();
            return true;
        }

        if (key == Key.R && modifiers == ModifierKeys.Shift)
        {
            return BeginOrContinuePlaceRepeat(PlaceRepeatKind.Region);
        }

        if (key == Key.L && modifiers == ModifierKeys.None)
        {
            JumpToLoopPrerollAndPlay();
            return true;
        }

        if (key == Key.A && modifiers == ModifierKeys.None)
        {
            // スペクトログラム／重ね。フェード調整中はロック。
            if (!BlocksOutsideFadeToolActions)
            {
                Waveform.ToggleSpectrogramView();
            }

            return true;
        }

        if (key is Key.M or Key.Insert && modifiers == ModifierKeys.None)
        {
            // プレイヤーは上の IsLibraryMaximized 分岐で消費済み。ここへ来たらエディタのみ。
            if (IsLibraryMaximized)
            {
                return true;
            }

            return BeginOrContinuePlaceRepeat(PlaceRepeatKind.Marker);
        }

        if (key == Key.Q && modifiers == ModifierKeys.None)
        {
            ToggleRangeClick();
            return true;
        }

        if (key == Key.E && modifiers == ModifierKeys.Alt && _waapiPanelVisible)
        {
            TogglePlayPostExit();
            return true;
        }

        if (key == Key.W && modifiers == ModifierKeys.Alt)
        {
            ToggleWaapiPanel();
            return true;
        }

        if (key == Key.R && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RenameMarkerAtPosition();
            return true;
        }

        if (key == Key.U && modifiers == ModifierKeys.None)
        {
            OpenEditHistory();
            return true;
        }

        if (key == Key.G && modifiers == ModifierKeys.None)
        {
            return StatusTimes.FocusCurrentTime();
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

        if (key == Key.I && modifiers == ModifierKeys.None)
        {
            PromptFade(fadeIn: true);
            return true;
        }

        if (key == Key.O && modifiers == ModifierKeys.None)
        {
            PromptFade(fadeIn: false);
            return true;
        }

        if (key == Key.X && modifiers == ModifierKeys.None)
        {
            ApplyFadeAroundPlayhead();
            return true;
        }

        if (key == Key.N && modifiers == ModifierKeys.None)
        {
            ApplyNormalize();
            return true;
        }

        if (key == Key.R && modifiers == ModifierKeys.None)
        {
            ApplyReverse();
            return true;
        }

        if (key == Key.V && modifiers == ModifierKeys.None)
        {
            // 音量。フェード調整中はロック（PromptVolume 側でも拒否）。
            if (!BlocksOutsideFadeToolActions)
            {
                PromptVolume();
            }

            return true;
        }

        if (key == Key.E && modifiers == ModifierKeys.None && Waveform.TryToggleLoudnessPen())
        {
            return true;
        }

        if (key == Key.P && modifiers == ModifierKeys.None)
        {
            PromptPitch();
            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.None)
        {
            PromptTimeStretch();
            return true;
        }

        if (key == Key.Delete && modifiers == ModifierKeys.Control)
        {
            ApplyDeleteMarkers();
            return true;
        }

        if (key == Key.D && modifiers == ModifierKeys.Control)
        {
            // Ctrl+T はタイル巡回済み。無音検出は Ctrl+D（プレイリスト複数選択も可）。
            if (!IsRecording && (_document is not null || HasBatchEditTargets))
            {
                ApplyDetectSilence();
            }

            return true;
        }

        if (key == Key.Insert && modifiers == ModifierKeys.Control)
        {
            if (!IsRecording && (_document is not null || HasBatchEditTargets))
            {
                PromptInsertSilenceAt(Waveform.PlayheadFrame);
            }

            return true;
        }

        if (key == Key.Delete && modifiers == ModifierKeys.Shift)
        {
            if (!IsLibraryMaximized && !IsRecording && _document is not null)
            {
                PromptSilenceSelection();
            }

            return true;
        }

        if (key == Key.Delete && modifiers == ModifierKeys.None)
        {
            ApplyDelete();
            return true;
        }

        if (key == Key.Up && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ZoomAmpToMax();
            return true;
        }

        if (key == Key.Down && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ResetAmpZoom();
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.Control)
        {
            Waveform.ZoomTimeToMax();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Control)
        {
            Waveform.ResetTimeZoom();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        if (key == Key.Home && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.SelectToDocumentEdge(-1);
            return true;
        }

        if (key == Key.End && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.SelectToDocumentEdge(1);
            return true;
        }

        if (key == Key.Home && modifiers == ModifierKeys.Control)
        {
            ExecuteTransport(TransportCommand.GoToStart);
            return true;
        }

        if (key == Key.End && modifiers == ModifierKeys.Control)
        {
            ExecuteTransport(TransportCommand.GoToEnd);
            return true;
        }

        if (TryHandleWaveformTimelineNavKey(key, modifiers))
        {
            return true;
        }

        if (_markerNudgeDirection != 0
            && key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl)
        {
            if (_nudgeAtPlayhead)
            {
                ApplyHeldMarkerNudge();
                return true;
            }

            if (key is Key.LeftShift or Key.RightShift)
            {
                ApplyNudgeStep(_markerNudgeDirection);
                return true;
            }
        }

        if (key is Key.Left or Key.Right && (modifiers & ModifierKeys.Alt) != 0)
        {
            var extra = modifiers & ~ModifierKeys.Alt & ~ModifierKeys.Shift & ~ModifierKeys.Control;
            if (extra == ModifierKeys.None)
            {
                return BeginOrContinueMarkerNudge(key == Key.Left ? -1 : 1);
            }
        }

        if (key == Key.Left && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ExtendSelectionToMarker(-1);
            return true;
        }

        if (key == Key.Right && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ExtendSelectionToMarker(1);
            return true;
        }

        if (key == Key.Left && modifiers == ModifierKeys.Control)
        {
            Waveform.SeekToMarker(-1);
            return true;
        }

        if (key == Key.Right && modifiers == ModifierKeys.Control)
        {
            Waveform.SeekToMarker(1);
            return true;
        }

        if (key == Key.Left && modifiers == ModifierKeys.Shift)
        {
            if (Waveform.HasSelectedTimelineItems)
            {
                return NudgePlayheadOrSelection(-1);
            }

            Waveform.NudgeSelection(-1);
            return true;
        }

        if (key == Key.Right && modifiers == ModifierKeys.Shift)
        {
            if (Waveform.HasSelectedTimelineItems)
            {
                return NudgePlayheadOrSelection(1);
            }

            Waveform.NudgeSelection(1);
            return true;
        }

        if (key == Key.Left && modifiers == ModifierKeys.None)
        {
            return NudgePlayheadOrSelection(-1);
        }

        if (key == Key.Right && modifiers == ModifierKeys.None)
        {
            return NudgePlayheadOrSelection(1);
        }

        if (key is Key.Up or Key.Down && modifiers == ModifierKeys.Alt)
        {
            return BeginOrContinueSpectrogramBoostNudge(key == Key.Up ? 1 : -1);
        }

        if (key == Key.Up && modifiers == ModifierKeys.Shift)
        {
            Waveform.ZoomAmpIn();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Shift)
        {
            Waveform.ZoomAmpOut();
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.None)
        {
            Waveform.ZoomTimeIn();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.None)
        {
            Waveform.ZoomTimeOut();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        // エディタ: 上段 無修飾＝割合、Shift＝マーカー番号、Ctrl＝リージョン番号。
        // Shift／Ctrl＋テンキーは上で同じジャンプ済み。無修飾テンキーは再生。
        if (!IsLibraryMaximized && TryHandleEditorDigitJump(key, modifiers))
        {
            return true;
        }

        return false;
    }

    /// <summary>F8–F12、ステータス／モニター列など、モード横断のトグル。</summary>
    private bool TryProcessChromeShortcut(Key key, ModifierKeys modifiers)
    {
        if (key == Key.F11 && modifiers == ModifierKeys.None)
        {
            if (!BlocksOutsideFadeToolActions)
            {
                ToggleWaveformMaximize();
            }

            return true;
        }

        if (key == Key.F12 && modifiers == ModifierKeys.None)
        {
            if (!BlocksOutsideFadeToolActions)
            {
                ToggleAnalyzerMaximize();
            }

            return true;
        }

        if (key == Key.F8 && modifiers == ModifierKeys.None)
        {
            if (!BlocksOutsideFadeToolActions)
            {
                ToggleVideoMiniPlayer();
            }

            return true;
        }

        if (key == Key.F9 && modifiers == ModifierKeys.None)
        {
            if (!BlocksOutsideFadeToolActions)
            {
                ToggleLibraryMinimalChrome();
            }

            return true;
        }

        if (key == Key.F10 && modifiers == ModifierKeys.None)
        {
            if (!BlocksOutsideFadeToolActions)
            {
                StopSeekNudge();
                StopVideoFrameStep();
                ToggleLibraryMaximize();
            }

            return true;
        }

        // ステータスバー系は F8／F9 でバーが隠れても、エディタ／プレイヤー共通で効かせる。
        if (key == Key.S && modifiers == ModifierKeys.Alt)
        {
            SilentSkipCheck.IsChecked = SilentSkipCheck.IsChecked != true;
            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.Alt)
        {
            AlwaysOnTopCheck.IsChecked = AlwaysOnTopCheck.IsChecked != true;
            return true;
        }

        // Single Loop はステータスバー（Silent Skip の右）。プレイヤー限定にするとエディタで効かない。
        if (LibraryPlayerMode.IsAutoLoopToggle(key, modifiers))
        {
            LibraryBrowser.ToggleAutoLoop();
            return true;
        }

        // モニター列（MONO／DIM／LC／BPM）。エディタ／プレイヤー共通。ボタン操作と同じ。
        if (LibraryPlayerMode.IsMonitorMonoToggle(key, modifiers))
        {
            MonitorToggles.ToggleMono();
            return true;
        }

        if (LibraryPlayerMode.IsMonitorDimToggle(key, modifiers))
        {
            MonitorToggles.ToggleDim();
            return true;
        }

        if (LibraryPlayerMode.IsMonitorLowCutToggle(key, modifiers))
        {
            MonitorToggles.ToggleLowCut();
            return true;
        }

        if (LibraryPlayerMode.IsMonitorBpmCycle(key, modifiers))
        {
            MonitorToggles.CycleBpmMode();
            return true;
        }

        return false;
    }

    /// <summary>ライブラリ／タイル検索。処理したら true/false、未該当は null。</summary>
    private bool? TryProcessSearchFocusShortcut(Key key, ModifierKeys modifiers)
    {
        // タイル検索。Ctrl+F で開き、ボックス内では Enter / Alt+Enter / Esc を拾う。
        // プレイヤーではライブラリ／プレイリスト検索が Ctrl+F を使う。
        if (IsLibraryMaximized && TryProcessLibrarySearchKey(key, modifiers))
        {
            return true;
        }

        if (TryProcessTileSearchKey(key, modifiers))
        {
            return true;
        }

        if (IsTileSearchFocused)
        {
            // 検索ボックスへの文字入力はそのまま通す（1 文字ごとに判定される）。
            return false;
        }

        // ライブラリ／プレイリスト検索フォーカス中は文字入力を優先。
        // Alt+R／N／P（と Ctrl+Shift+P）だけは下のトグルへ通す（Alt は検索に文字を入れない）。
        if (IsLibraryMaximized
            && LibraryBrowser.IsSearchFocused
            && !LibraryPlayerMode.BypassesSearchFocus(key, modifiers))
        {
            return false;
        }

        return null;
    }

    /// <summary>プレイヤーセッション（ミニ／PDF／ファイル操作／ツリー等）。未該当は null。</summary>
    private bool? TryProcessLibrarySessionShortcut(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!IsNumpadTextInputFocused()
            && !IsMenuTakingNumpadKeys()
            && TryNudgePlaybackPitch(key, modifiers))
        {
            return true;
        }

        // 動画ミニ: In／Out がある Escape は解除だけ。無ければ Q と同じくプレイリストへ戻る。
        if (IsVideoMiniPlayerActive()
            && !isRepeat
            && LibraryPlayerMode.LeavesVideoMiniToPlaylist(key, modifiers))
        {
            if (LibraryPlayerMode.ClearsVideoMiniInOutInsteadOfLeaving(
                    key,
                    modifiers,
                    LibraryBrowser.PlaylistVisualIsVideo,
                    Waveform.ScratchCues.HasAbPoint))
            {
                Waveform.ClearPlayerAbRepeat();
                SyncPlayWindowFromDocument();
                return true;
            }

            LeaveVideoMiniToPlaylist(minimalChrome: false);
            return true;
        }

        // F8／引数起動の動画・PDF ミニ: +／- で窓サイズを拡縮（テンキー可）。
        // PDF 内容ズームより先に窓拡縮へ回す。
        if (IsVideoMiniPlayerActive()
            && !_playlistVideoFullscreen
            && TryHandleVideoLaunchScaleKey(key, modifiers, isRepeat))
        {
            return true;
        }

        // PDF 表示中はページ／内容拡縮を優先し、プレイリスト操作キーは奪う。
        if (IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualIsPdf
            && TryHandlePlaylistPdfKey(key, modifiers, isRepeat))
        {
            return true;
        }

        // プレイヤーのコピー／貼り付け／移動／削除／名前変更。F1／F3 はペイン移動に使わない。
        if (IsLibraryMaximized
            && !isRepeat
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && TryHandleLibraryFileShortcut(key, modifiers))
        {
            return true;
        }

        // プレイヤー専用: Tab でツリー→お気に入り→プレイリスト。
        if (IsLibraryMaximized && IsLibraryPaneCycleShortcut(key, modifiers))
        {
            if (key == Key.Tab)
            {
                LibraryBrowser.CyclePaneFocus(reverse: modifiers == ModifierKeys.Shift);
            }
            return true;
        }

        // F5 はツリー再読込だけ。Ctrl+F5 は索引の再構築（すりガラスでロック）。
        if (IsLibraryMaximized && LibraryPlayerMode.IsExplorerRefresh(key, modifiers))
        {
            RefreshLibraryTree();
            return true;
        }

        if (IsLibraryMaximized && LibraryPlayerMode.IsLibraryCatalogRebuild(key, modifiers))
        {
            RebuildLibraryCatalog();
            return true;
        }

        // 空リスト／ツリー／検索フォーカスでも Alt+R はランダム切替。タイプアヘッドより先に拾う。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.IsShuffleToggle(key, modifiers))
        {
            LibraryBrowser.ToggleShuffle();
            return true;
        }

        // Alt+N は次の曲を自動再生。素の N（次の曲）より先、タイプアヘッド／検索入力より先。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.IsAutoPlayNextToggle(key, modifiers))
        {
            LibraryBrowser.ToggleAutoPlayNext();
            return true;
        }

        // Alt+L（Single Loop）は先頭のステータスバー系で処理済み。

        // Alt+P は Enter 追加時の自動再生。素の P（前の曲）より先、タイプアヘッド／検索入力より先。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.IsAutoPlayToggle(key, modifiers))
        {
            LibraryBrowser.ToggleAutoPlay();
            return true;
        }

        // Ctrl+Shift+P は動画プロキシの強制生成（設定の自動生成オフ／F8 抑止を無視）。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && !isRepeat
            && LibraryPlayerMode.IsForceVideoProxyEncode(key, modifiers))
        {
            RequestForceVideoProxyEncode();
            return true;
        }

        // P／N は前後曲（テンキー 4／6）。ツリー／検索フォーカス中はタイプアヘッド（検索入力）を優先。
        if (IsLibraryMaximized
            && LibraryPlayerMode.AllowsPlayerLetterTransport(IsLibraryExplorerFocused)
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && !isRepeat
            && LibraryPlayerMode.IsPlayerTrackStepKey(key, modifiers))
        {
            TryStepLibraryPlaylist(key == Key.P ? -1 : 1);
            return true;
        }

        // R は再生し直し（テンキー 5）。Alt+R のランダムより後。ツリー／検索中はタイプアヘッド優先。
        if (IsLibraryMaximized
            && LibraryPlayerMode.AllowsPlayerLetterTransport(IsLibraryExplorerFocused)
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && !isRepeat
            && LibraryPlayerMode.IsPlayerRestartKey(key, modifiers))
        {
            RestartLibraryTrack();
            return true;
        }

        // 一時マーカーがあるとき Ctrl+←／→ をマーカージャンプに。
        if (IsLibraryMaximized
            && LibraryPlayerMode.PrefersMarkerArrowJump(
                IsPlayerScratchContext(),
                Waveform.HasPlayerScratchMarkers)
            && key is Key.Left or Key.Right
            && modifiers == ModifierKeys.Control)
        {
            Waveform.SeekToMarker(key == Key.Left ? -1 : 1);
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.PrefersMarkerArrowJump(
                IsPlayerScratchContext(),
                Waveform.HasPlayerScratchMarkers)
            && key is Key.Left or Key.Right
            && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ExtendSelectionToMarker(key == Key.Left ? -1 : 1);
            return true;
        }

        // Ctrl+Delete／選択中 Delete は捨てマーカー削除。
        if (IsLibraryMaximized
            && IsPlayerScratchContext()
            && key == Key.Delete
            && modifiers == ModifierKeys.Control)
        {
            ApplyPlayerScratchMarkerDelete();
            return true;
        }

        if (IsLibraryMaximized
            && IsPlayerScratchContext()
            && key == Key.Delete
            && modifiers == ModifierKeys.None
            && LibraryPlayerMode.PrefersScratchMarkerDelete(
                playerMode: true,
                Waveform.HasSelectedMarkers))
        {
            ApplyPlayerScratchMarkerDelete();
            return true;
        }

        // I／O／M／Ins。ツリーフォーカス中は I／O／M をタイプアヘッドへ譲る（Ins はマーカーのまま）。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.AllowsPlayerScratchCueKey(
                key,
                modifiers,
                LibraryBrowser.IsExplorerTreeFocused)
            && TryHandlePlayerScratchCueKey(key, modifiers))
        {
            return true;
        }

        // Ctrl+←／↑ ライブラリ、Ctrl+→ プレイリスト、Ctrl+↓ お気に入り。
        // プレイヤー中の Ctrl+←／→（マーカー）は上で回したのでここには来ない。暗いプレビューではペインへ。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && LibraryPlayerMode.IsPaneFocusArrow(key, modifiers))
        {
            switch (key)
            {
                case Key.Left:
                case Key.Up:
                    LibraryBrowser.FocusExplorer();
                    break;
                case Key.Right:
                    LibraryBrowser.FocusList();
                    break;
                default:
                    LibraryBrowser.FocusFavorites();
                    break;
            }

            return true;
        }

        // テンキー再生操作（プレイヤー／エディタ共通）。上段数字の割合／マーカーは別経路。
        // ツリーのインクリメントサーチ中はテンキー数字を検索へ譲る。
        if (!IsLibraryGroupComboFocused
            && !StatusTimes.IsTimeFocused
            && !IsEditingFileName
            && !Waveform.IsEditingMarkerComment
            && _volumeMenu is not { IsOpen: true }
            && _timeStretchMenu is not { IsOpen: true }
            && _fadeMenu is not { IsOpen: true }
            && _formatMenu is not { IsOpen: true }
            && !(IsLibraryExplorerFocused
                && !LibraryBrowser.IsExplorerSearchFocused
                && LibraryPlayerMode.IsPlayerNumpadKey(key, modifiers))
            && TryHandleTransportNumpad(key, modifiers, isRepeat))
        {
            return true;
        }

        if (IsLibraryMaximized && TryHandlePlaylistVideoFrameStep(key, modifiers, isRepeat))
        {
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.IsVideoFullscreenToggle(key, modifiers)
            && TryTogglePlaylistVideoFullscreen())
        {
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.IsVideoHudToggle(key, modifiers)
            && TryTogglePlaylistVideoHud())
        {
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.IsVideoTimecodeToggle(key, modifiers)
            && TryTogglePlaylistVideoTimecode())
        {
            return true;
        }

        if (IsLibraryMaximized
            && !isRepeat
            && LibraryPlayerMode.IsPdfBackgroundPinToggle(key, modifiers)
            && TryTogglePdfBackgroundPin())
        {
            return true;
        }

        // F10: 本再生／イマーシブ一時停止／PDF 明るい表示から暗いプレビューへ（Escape と同じ。動画ミニの Q は上でプレイリストへ戻済み）。
        if (IsLibraryMaximized
            && key == Key.Q
            && modifiers == ModifierKeys.None
            && !isRepeat
            && TryExitPlaylistRealPlayback())
        {
            return true;
        }

        // Shift／Ctrl＋テンキーは番号ジャンプ。Alt＋テンキーは割合。無修飾は再生のまま。
        // ツリーの Shift+8（上段）は配下展開のまま。文字入力中はテンキーを渡す。
        if (!NumpadShiftPassesToText()
            && TryHandleNumpadCueDigitJump(key, modifiers))
        {
            return true;
        }

        // 上段 0–9: 無修飾＝割合。Shift＋上段＝マーカー番号（二桁可。無修飾テンキーは上で再生操作済み）。
        // ツリーフォーカス中は使わない（Shift+8＝配下展開を優先。割合／マーカーは他ペインから）。
        if (IsLibraryMaximized
            && LibraryPlayerMode.AllowsPlayerDigitJump(IsLibraryExplorerFocused)
            && !IsLibraryGroupComboFocused
            && !StatusTimes.IsTimeFocused
            && !IsEditingFileName
            && _volumeMenu is not { IsOpen: true }
            && _timeStretchMenu is not { IsOpen: true }
            && _fadeMenu is not { IsOpen: true }
            && _formatMenu is not { IsOpen: true }
            && TryHandleLibraryDigitJump(key, modifiers))
        {
            return true;
        }

        if (IsLibraryMaximized
            && key is Key.Left or Key.Right
            && modifiers == ModifierKeys.None)
        {
            StopPlaybackShuttle();
            if (IsLibraryExplorerFocused)
            {
                return false;
            }

            return true;
        }

        if (IsLibraryGroupComboFocused)
        {
            return false;
        }

        if (IsLibraryExplorerFocused)
        {
            if (!LibraryBrowser.IsExplorerSearchFocused)
            {
                if (LibraryBrowser.TryExplorerTypeahead(key, modifiers))
                {
                    return true;
                }
            }

            if (LibraryPlayerMode.IsPlaylistFolderAddKey(key, modifiers))
            {
                // Enter＝クリア直下／Ctrl+Enter＝クリア再帰／Shift＋＝追加（Ctrl で再帰）。
                LibraryBrowser.ClearExplorerTypeahead();
                var clear = LibraryPlayerMode.ClearsPlaylistOnFolderAdd(modifiers);
                var recursive = LibraryPlayerMode.RecursesPlaylistFolderAdd(modifiers);
                if (clear)
                {
                    LibraryBrowser.ReplacePlaylistFromSelection(recursive);
                }
                else
                {
                    LibraryBrowser.OpenSelectedFolder(recursive);
                }

                return true;
            }

            if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                LibraryBrowser.ExpandSelectedExplorerSubtree();
                return true;
            }

            if (LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                LibraryBrowser.CollapseSelectedExplorerSubtree();
                return true;
            }

            if (LibraryPlayerMode.IsExplorerCollapseAllRoots(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                LibraryBrowser.CollapseAllExplorerRoots();
                return true;
            }

            // 設定のルート「上へ／下へ」と同じ並び替え。
            if (LibraryPlayerMode.IsExplorerRootReorder(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                LibraryBrowser.TryMoveSelectedExplorerRoots(key == Key.Up ? -1 : 1);
                return true;
            }

            if (key == Key.Space && modifiers == ModifierKeys.None)
            {
                LibraryBrowser.TryExplorerSpaceExpandOrMoveDown();
                return true;
            }

            if (key == Key.Space && modifiers == ModifierKeys.Shift)
            {
                LibraryBrowser.TryExplorerShiftSpaceCollapseOrMoveUp();
                return true;
            }

            if (LibraryPlayerMode.ExplorerOwnsHorizontal(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                StopPlaybackShuttle();
                return false;
            }

            if (LibraryPlayerMode.BlocksExplorerKey(key, modifiers))
            {
                return true;
            }

            // Ctrl+Q などアプリ共通ショートカットは続行。タイプアヘッドはツリーへ渡す。
            if (!LibraryPlayerMode.AllowsKey(key, modifiers))
            {
                return false;
            }
        }

        if (IsLibraryFavoritesFocused)
        {
            // Enter 系追加／Delete はお気に入りリスト側で処理。
            // ここで握るとプレイリストの Enter（再生）に吸われ、追加されない。
            if (LibraryPlayerMode.IsPlaylistFolderAddKey(key, modifiers))
            {
                return false;
            }

            if (key == Key.Delete && modifiers == ModifierKeys.None)
            {
                return false;
            }

            if (LibraryPlayerMode.BlocksExplorerKey(key, modifiers))
            {
                return true;
            }

            if (!LibraryPlayerMode.AllowsKey(key, modifiers))
            {
                return false;
            }
        }

        return null;
    }

    /// <summary>プレイリスト上の移動／選択／捨てキュー Undo など。</summary>
    private bool TryProcessLibraryPlaylistShortcut(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (IsLibraryMaximized)
        {
            // 選択は据え置きで表示だけ1行動かす。ツリーフォーカス時はルート並び替えが先。
            if (LibraryPlayerMode.IsPlaylistScroll(key, modifiers)
                && !IsLibraryExplorerFocused)
            {
                LibraryBrowser.ScrollPlaylistByRows(key == Key.Up ? -1 : 1);
                return true;
            }

            // Shift+Space: プレイリストのチェックモード（ツリーは上で折りたたみ処理済み）。
            if (LibraryPlayerMode.IsCheckSelectModeToggle(key, modifiers)
                && !IsLibraryExplorerFocused
                && !IsLibraryFavoritesFocused)
            {
                if (!isRepeat)
                {
                    LibraryBrowser.ToggleCheckSelectMode();
                }

                return true;
            }

            if ((key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
                && modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                // 動画本再生・ミニ・PDF 本表示では Home／End／Page をエディタと同じタイムライン操作へ。
                if ((key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
                    && LibraryPlayerMode.UsesEditorTimelineNavKeys(_playlistVideoChromeVisible))
                {
                    _libraryPlayOnArrowRelease = false;
                    return TryHandleWaveformTimelineNavKey(key, modifiers);
                }

                var extend = modifiers == ModifierKeys.Shift;
                // 単発は端から反対側へ回る。長押し連続は端で一旦止まる（Ctrl+Tab と同じ）。
                var wrap = !isRepeat;
                if (key is Key.Home or Key.End)
                {
                    LibraryBrowser.MoveSelectionToEdge(key == Key.Home ? -1 : 1, extend);
                }
                else if (key is Key.PageUp or Key.PageDown)
                {
                    LibraryBrowser.MoveSelectionPage(key == Key.PageUp ? -1 : 1, extend, wrap);
                }
                else
                {
                    LibraryBrowser.MoveSelection(key == Key.Up ? -1 : 1, extend, wrap);
                }

                _libraryPlayOnArrowRelease = !extend;
                return true;
            }

            if (key == Key.A && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
            {
                // 動画本再生など前面 UI 隠れ中は全選択しない（続けて Delete で空にする事故を防ぐ）。
                if (LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(
                        _playlistVideoChromeVisible))
                {
                    return true;
                }

                LibraryBrowser.SelectAllRows();
                return true;
            }

            if (key == Key.C && modifiers == ModifierKeys.Control)
            {
                LibraryBrowser.CopySelected();
                return true;
            }

            // Enter はグローバル側（本再生中＝その場停止、停止中の動画／PDF＝Space と同じ）。

            // リストから外すだけ（ディスクは消さない）。選択中マーカーの Delete は上で処理済み。
            if (key == Key.Delete && modifiers == ModifierKeys.None)
            {
                if (LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(
                        _playlistVideoChromeVisible))
                {
                    return true;
                }

                RemoveLibrarySelectedFromList();
                return true;
            }

            // 捨てキュー操作は上の TryHandlePlayerScratchCueKey 済み。
            // 非コンテキスト時も I／O／M／Ins はここで消費し、エディタのフェード／AddMarker へ落とさない。
            if (modifiers == ModifierKeys.None
                && key is Key.I or Key.O or Key.M or Key.Insert)
            {
                return true;
            }

            if (IsPlayerScratchContext()
                && key is Key.Left or Key.Right
                && (modifiers & ModifierKeys.Alt) != 0
                && (modifiers & ~(ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Control)) == 0)
            {
                return BeginOrContinueMarkerNudge(key == Key.Left ? -1 : 1);
            }

            // 捨てキューの Undo／Redo。戻せない Ctrl+Z は何もしない（エディタの履歴へ落とさない）。
            if (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
                || key == Key.Y && modifiers == ModifierKeys.Control)
            {
                if (IsPlayerScratchContext())
                {
                    UndoOrRedoPlayerScratch(redo: true);
                }

                return true;
            }

            if (key == Key.Z && modifiers == ModifierKeys.Control)
            {
                if (IsPlayerScratchContext() && Waveform.CanUndoPlayerScratch)
                {
                    UndoOrRedoPlayerScratch(redo: false);
                }

                return true;
            }

            // タブを閉じない。リストから外すのも Delete のみ。
            if (key == Key.W
                && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
            {
                return true;
            }

            if (!LibraryPlayerMode.AllowsKey(key, modifiers))
            {
                return true;
            }

            // 破壊編集は右クリックメニューのみ。ショートカットは握りつぶす（誤操作防止）。
            if (LibraryPlayerMode.IsDestructiveEditShortcut(key, modifiers))
            {
                return true;
            }
        }

        return false;
    }


    private bool TryConsumeRecordingShortcut(Key key, ModifierKeys modifiers)
    {
        if (!_recording)
        {
            return false;
        }

        if (key == Key.R && modifiers == ModifierKeys.Control)
        {
            StopRecording();
            return true;
        }

        if (key is Key.Space or Key.Enter or Key.Escape && modifiers == ModifierKeys.None)
        {
            StopRecording();
            return true;
        }

        if (key == Key.Q && modifiers == ModifierKeys.Control)
        {
            Close();
            return true;
        }

        return true;
    }

    private bool TryProcessEscapeShortcut(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Escape)
        {
            return false;
        }

        if (IsCrossfadeEditMode)
        {
            ExitCrossfadeEditMode(apply: false);
            return true;
        }

        // ツリーのタイプアヘッド打ち込みを先に捨てる（1秒待ちの代わりに即クリア）。
        if (IsLibraryMaximized
            && LibraryBrowser.IsExplorerTreeFocused
            && LibraryBrowser.ClearExplorerTypeahead())
        {
            return true;
        }

        if (IsLibraryMaximized
            && (LibraryBrowser.IsExplorerTreeFocused || LibraryBrowser.IsListKeyboardFocused)
            && !LibraryBrowser.IsSearchFocused
            && LibraryBrowser.ClearCutGhost())
        {
            try
            {
                Clipboard.Clear();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }

            LibraryBrowser.FocusPane(LibraryBrowser.ActivePane);
            return true;
        }

        if (_recording)
        {
            StopRecording();
            return true;
        }

        StopMarkerNudge();
        StopPlaceRepeat();
        StopSpectrogramBoostNudge();
        if (StatusTimes.IsEditing)
        {
            StatusTimes.CancelEdit();
            Waveform.Focus();
            return true;
        }

        if (StatusTimes.IsTimeFocused)
        {
            Waveform.Focus();
            return true;
        }

        if (Waveform.CancelMarkerCommentEdit())
        {
            return true;
        }

        if (IsEditingFileName)
        {
            CancelFileNameEdit();
            return true;
        }

        if (CloseFadeCurvePicker() || CloseFormatConvertPicker() || CloseVolumeGainPicker() || CloseTimeStretchPicker() || CloseInsertSilencePicker() || CloseSilenceSelectionPicker())
        {
            return true;
        }

        if (Waveform.IsScrubbing)
        {
            Overview.CancelDrag();
            Waveform.CancelScrub();
            return true;
        }

        if (Waveform.CancelMarkerInteraction())
        {
            return true;
        }

        if (IsPlayerScratchContext() && Waveform.ScratchCues.HasAbPoint)
        {
            Waveform.ClearPlayerAbRepeat();
            SyncPlayWindowFromDocument();
            return true;
        }

        // 本再生／イマーシブ一時停止／PDF 明るい表示から、暗いプレビュー（前面 UI）へ戻す（Q も同じ）。
        if (IsLibraryMaximized && TryExitPlaylistRealPlayback())
        {
            return true;
        }

        if (ClearTabSelection())
        {
            return true;
        }

        if (Waveform.ClearSelection())
        {
            return true;
        }

        if (Waveform.TryCancelLoudnessPen())
        {
            return true;
        }

        if (Waveform.TryExitAnalysisView())
        {
            return true;
        }

        return true;
    }

    private bool TryProcessFadeMenuShortcut(
        System.Windows.Controls.ContextMenu fadeMenu,
        Key key,
        ModifierKeys modifiers)
    {
        if (key == Key.I && modifiers == ModifierKeys.None)
        {
            PromptFade(fadeIn: true);
            return true;
        }

        if (key == Key.O && modifiers == ModifierKeys.None)
        {
            PromptFade(fadeIn: false);
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            PreviewFade(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(fadeMenu));
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyFade(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(fadeMenu));
            CloseFadeCurvePicker();
            return true;
        }

        if (modifiers == ModifierKeys.None
            && TryDigitPercent(key, out var fadeDigit)
            && fadeDigit > 0)
        {
            var index = (int)Math.Round(fadeDigit * 10d) - 1;
            if (FadeCurvePicker.HighlightByIndex(fadeMenu, index))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryProcessDocumentShortcut(Key key, ModifierKeys modifiers, bool isRepeat = false)
    {
        if (key == Key.F2 && modifiers == ModifierKeys.None)
        {
            if (_activeSession is not null)
            {
                BeginFileNameEdit(_activeSession);
                return true;
            }
        }

        if (key == Key.D && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (IsRecording)
            {
                return true;
            }

            if (HasTabSelection)
            {
                DuplicateSessions(SelectedTabsInOrder());
            }
            else if (_activeSession is not null)
            {
                DuplicateSession(_activeSession);
            }

            return true;
        }

        if (key == Key.B && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!IsRecording && ResolveBatchEditTargets().Length >= 2)
            {
                MergeSelectedTabs();
            }

            return true;
        }

        if (key == Key.Q && modifiers == ModifierKeys.Control)
        {
            Close();
            return true;
        }

        if (key == Key.R && modifiers == ModifierKeys.Control)
        {
            ToggleRecording();
            return true;
        }

        if (key == Key.W && modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt))
        {
            LaunchWwiseProjectFromShortcut();
            return true;
        }

        if (key == Key.W && modifiers == ModifierKeys.Control)
        {
            CloseDocument();
            return true;
        }

        if (key == Key.W && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CloseAllTabs();
            return true;
        }

        if (key == Key.T && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!IsLibraryMaximized)
            {
                ReopenLastClosedTab();
            }

            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                CycleWaveformTile();
            }

            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(-1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.None)
        {
            if (IsLibraryMaximized)
            {
                return true;
            }

            CycleChannelSolo(1);
            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.Shift)
        {
            if (IsLibraryMaximized)
            {
                return true;
            }

            CycleChannelSolo(-1);
            return true;
        }

        if (key == Key.PageDown && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.PageUp && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(-1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.N && modifiers == ModifierKeys.Control)
        {
            NewDocument();
            return true;
        }

        if (key == Key.O && modifiers == ModifierKeys.Control)
        {
            OpenFromDialog();
            return true;
        }

        if (key == Key.O && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenSettings();
            return true;
        }

        if (key == Key.E && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (WaapiBar.ExportEnabled)
            {
                _ = ExportToWwiseAsync();
            }

            return true;
        }

        if (key == Key.S && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Save(saveAs: true);
            return true;
        }

        if (key == Key.M && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ExportMp3FromUi();
            return true;
        }

        if (key == Key.S && modifiers == ModifierKeys.Control)
        {
            Save(saveAs: false);
            return true;
        }

        return false;
    }

    private bool TryHandleVolumeMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_volumeMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key is Key.Up or Key.Down)
        {
            VolumeGainPicker.TryNudge(_volumeMenu, key == Key.Up ? 1 : -1);
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            PreviewVolume(VolumeGainPicker.ReadGain(_volumeMenu));
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyVolume(VolumeGainPicker.ReadGain(_volumeMenu));
            return true;
        }

        if (key == Key.V && modifiers == ModifierKeys.None)
        {
            return true;
        }

        return false;
    }

    private bool TryHandleTimeStretchMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_timeStretchMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key is Key.Up or Key.Down)
        {
            TimeStretchPicker.TryNudge(_timeStretchMenu, key == Key.Up ? 1 : -1);
            return true;
        }

        if (key == Key.Tab)
        {
            TimeStretchPicker.TryMoveFocus(
                _timeStretchMenu,
                reverse: (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            PreviewTimeStretch(TimeStretchPicker.ReadDestFrames(_timeStretchMenu));
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyTimeStretch(TimeStretchPicker.ReadDestFrames(_timeStretchMenu));
            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.None)
        {
            return true;
        }

        return false;
    }

    private bool TryHandleSilenceMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_silenceMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key is Key.Up or Key.Down)
        {
            SilenceInsertPicker.TryNudge(_silenceMenu, key == Key.Up ? 1 : -1);
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyInsertSilence(SilenceInsertPicker.ReadSeconds(_silenceMenu));
            return true;
        }

        return false;
    }

    private bool TryHandleSilenceSelectionMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_silenceSelectionMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplySilenceSelection();
            return true;
        }

        return false;
    }

    /// <summary>
    /// エディタの上段数字ジャンプ。無修飾＝割合、Shift＝マーカー番号、Ctrl＝リージョン番号。
    /// </summary>
    private bool TryHandleEditorDigitJump(Key key, ModifierKeys modifiers)
    {
        if (!LibraryPlayerMode.IsDigitJumpChord(key, modifiers)
            || !TryDigitKey(key, out var digit))
        {
            return false;
        }

        if (LibraryPlayerMode.IsRegionDigitJump(key, modifiers))
        {
            if (_document is null || _document.Regions.Count == 0)
            {
                return true;
            }

            return ApplyResolvedRegionDigitJump(digit, _document.Regions.Count);
        }

        if (LibraryPlayerMode.IsMarkerDigitJump(key, modifiers))
        {
            if (_document is null || _document.Markers.Count == 0)
            {
                return true;
            }

            return ApplyResolvedMarkerDigitJump(
                digit,
                _document.Markers.Count,
                allowPercentFallback: false);
        }

        if (!TryDigitPercent(key, out var percent))
        {
            return false;
        }

        JumpByVisiblePercent(percent, extendSelection: false);
        return true;
    }

    private bool ApplyResolvedMarkerDigitJump(int digit, int markerCount, bool allowPercentFallback)
    {
        if (!LibraryPlayerMode.TryResolveMarkerDigitJump(
                _markerNumber,
                digit,
                markerCount,
                out var nextPending,
                out var seekId,
                out var percentFallback))
        {
            return false;
        }

        if (seekId is int id)
        {
            _markerNumber = nextPending;
            _regionNumber = 0;
            Waveform.SeekToMarkerId(id, extendSelection: false);
            RestartMarkerDigitTimer();
            return true;
        }

        ResetMarkerDigitEntry();
        if (allowPercentFallback && percentFallback)
        {
            JumpByVisiblePercent(digit / 10d, extendSelection: false);
        }

        return true;
    }

    private bool ApplyResolvedRegionDigitJump(int digit, int regionCount)
    {
        if (!LibraryPlayerMode.TryResolveRegionDigitJump(
                _regionNumber,
                digit,
                regionCount,
                out var nextPending,
                out var seekId))
        {
            return false;
        }

        _markerNumber = 0;
        if (seekId is int id)
        {
            _regionNumber = nextPending;
            Waveform.SeekToRegionNumber(id);
            RestartMarkerDigitTimer();
            return true;
        }

        if (nextPending > 0)
        {
            _regionNumber = nextPending;
            RestartMarkerDigitTimer();
            return true;
        }

        ResetMarkerDigitEntry();
        return true;
    }

    private void JumpByVisiblePercent(double percent, bool extendSelection)
    {
        if (extendSelection)
        {
            Waveform.ExtendSelectionToVisiblePercent(percent);
            return;
        }

        Waveform.JumpVisiblePercent(percent);
    }

    private void RestartMarkerDigitTimer()
    {
        _markerDigitTimer.Stop();
        _markerDigitTimer.Start();
    }

    private void ResetMarkerDigitEntry()
    {
        _markerDigitTimer.Stop();
        _markerNumber = 0;
        _regionNumber = 0;
        ResetPdfPageDigitEntry();
    }

    private static bool TryDigitPercent(Key key, out double percent)
    {
        percent = 0;
        // 割合ジャンプは上段のみ（テンキーは再生操作）。
        var digit = key switch
        {
            Key.D0 => 0,
            Key.D1 => 1,
            Key.D2 => 2,
            Key.D3 => 3,
            Key.D4 => 4,
            Key.D5 => 5,
            Key.D6 => 6,
            Key.D7 => 7,
            Key.D8 => 8,
            Key.D9 => 9,
            _ => -1,
        };
        if (digit < 0)
        {
            return false;
        }

        percent = digit / 10d;
        return true;
    }

    private static bool IsLibraryPaneCycleShortcut(Key key, ModifierKeys modifiers) =>
        LibraryPlayerMode.IsPaneCycleKey(key, modifiers);

    /// <summary>
    /// プレイヤーのライブラリ／プレイリスト検索。Ctrl+F でフォーカス中のペインの検索へ。
    /// Enter で決定してツリーまたはリストへ戻る。Esc は未確定の入力を捨てる。
    /// </summary>
    private bool TryProcessLibrarySearchKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.F && modifiers == ModifierKeys.Control)
        {
            LibraryBrowser.FocusPaneSearch(LibraryBrowser.ActivePane);
            return true;
        }

        if (!LibraryBrowser.IsSearchFocused || ImeComposition.IsComposing)
        {
            return false;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            LibraryBrowser.CommitSearchFocus();
            return true;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            LibraryBrowser.CancelSearchFocus();
            return true;
        }

        return false;
    }

    private bool TryHandleTransportNumpad(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        // テンキー＋ J／K／,／.（プレイヤー／エディタ共通。中央寄せは Z／テンキー .）。
        var command = LibraryPlayerMode.PlayerTransportCommand(key, modifiers);
        // ツリー／検索フォーカス中は J／K をタイプアヘッド（検索入力）に譲る。,／.／テンキーはそのまま。
        if (command is LibraryNumpadCommand.SeekBack or LibraryNumpadCommand.SeekForward
            && !LibraryPlayerMode.AllowsPlayerLetterTransport(IsLibraryExplorerFocused))
        {
            command = LibraryNumpadCommand.None;
        }

        var numpad = LibraryPlayerMode.UsesTransportNumpad()
            && LibraryPlayerMode.IsPlayerNumpadKey(key, modifiers);
        if (command == LibraryNumpadCommand.None && numpad)
        {
            command = LibraryPlayerMode.PlayerNumpadCommand(key, modifiers);
        }

        if (command == LibraryNumpadCommand.None)
        {
            // 2／8 など未割り当てテンキーは何もしない（割合ジャンプへ落とさない）。
            if (numpad)
            {
                StopPlaybackShuttle();
                StopSeekNudge();
                return true;
            }

            return false;
        }

        // キー種別ではなくコマンドで止める（テンキー 1／3／7／9 を押しっぱなしにするため）。
        if (command is not (LibraryNumpadCommand.Rewind or LibraryNumpadCommand.FastForward))
        {
            StopPlaybackShuttle();
        }

        if (command is not (LibraryNumpadCommand.SeekBack or LibraryNumpadCommand.SeekForward))
        {
            StopSeekNudge();
        }

        if (isRepeat
            && !LibraryPlayerMode.PlayerNumpadRepeatIsHold(command)
            && !LibraryPlayerMode.PlayerNumpadTrackStepRepeats(command))
        {
            return true;
        }

        switch (command)
        {
            case LibraryNumpadCommand.PlayPause:
                if (IsLibraryMaximized)
                {
                    ToggleLibraryPlayPause();
                }
                else if (IsPlaybackActive())
                {
                    // Space（停止で頭へ戻る）ではなく、プレイヤーの 0 と同じ一時停止／再開。
                    PausePlaybackHere();
                }
                else if (_document is not null)
                {
                    StartPlayback(_document.CursorFrame, prerollSeconds: 0);
                }

                break;
            case LibraryNumpadCommand.Restart:
                if (IsLibraryMaximized)
                {
                    RestartLibraryTrack();
                }
                else if (_document is not null)
                {
                    // プレイヤーの 5（頭から）に揃え、Alt+Enter（再生開始位置）とは別。
                    StartPlayback(0, prerollSeconds: 0);
                }

                break;
            case LibraryNumpadCommand.PreviousTrack:
                if (IsLibraryMaximized)
                {
                    // ↑↓ と同様、長押しは選択だけ送り、離したときに再生する。
                    StepLibraryTrackSelection(-1, wrap: !isRepeat);
                }
                else
                {
                    ActivateAdjacentTab(-1, wrap: !isRepeat);
                }

                break;
            case LibraryNumpadCommand.NextTrack:
                if (IsLibraryMaximized)
                {
                    StepLibraryTrackSelection(1, wrap: !isRepeat);
                }
                else
                {
                    ActivateAdjacentTab(1, wrap: !isRepeat);
                }

                break;
            case LibraryNumpadCommand.SeekBack:
                BeginOrContinueSeekNudge(-1);
                break;
            case LibraryNumpadCommand.SeekForward:
                BeginOrContinueSeekNudge(1);
                break;
            case LibraryNumpadCommand.Rewind:
                if (CanPlaybackShuttle(allowWhileStopped: true))
                {
                    BeginOrContinuePlaybackShuttle(-1);
                }

                break;
            case LibraryNumpadCommand.FastForward:
                if (CanPlaybackShuttle(allowWhileStopped: true))
                {
                    BeginOrContinuePlaybackShuttle(1);
                }

                break;
        }

        return true;
    }

    /// <summary>
    /// Shift＋テンキー 7／9 と Shift＋J／K＝±5 秒シーク＋選択伸長、
    /// Shift＋テンキー 1／3＝±3 倍シャトル＋選択伸長。
    /// エディタと、プレイヤーのシーク＆選択コンテキスト（<see cref="AllowsWaveformSelectionTransport"/>）。
    /// </summary>
    private void BeginEditorNumpadSelectionTransport(int digit)
    {
        if (!AllowsWaveformSelectionTransport()
            || !LibraryPlayerMode.IsNumpadSelectionTransportDigit(digit))
        {
            return;
        }

        var command = LibraryPlayerMode.NumpadSelectionTransportCommand(digit);
        if (command == LibraryNumpadCommand.None)
        {
            return;
        }

        var starting = _transportSelectionDigit != digit;
        if (starting)
        {
            // 空のときだけ起点をヘッドに。既存選択があるのに再設定すると
            // Shift 押しっぱなしで 1／3 を離→再押した瞬間に選択が潰れる。
            if (_document is null || _document.Selection.IsEmpty)
            {
                Waveform.SetKeyboardSelectAnchor(Waveform.PlayheadFrame);
            }

            _transportSelectionDigit = digit;
            _transportSelectionExtend = true;
            // 選択が伸びても再生ループに吸い込まれないようにする。
            SyncPlayWindowFromDocument();
        }

        switch (command)
        {
            case LibraryNumpadCommand.SeekBack:
                BeginOrContinueSeekNudge(-1);
                ApplyTransportSelectionExtend();
                break;
            case LibraryNumpadCommand.SeekForward:
                BeginOrContinueSeekNudge(1);
                ApplyTransportSelectionExtend();
                break;
            case LibraryNumpadCommand.Rewind:
                if (CanPlaybackShuttle(allowWhileStopped: true))
                {
                    BeginOrContinuePlaybackShuttle(-1);
                    ApplyTransportSelectionExtend();
                }

                break;
            case LibraryNumpadCommand.FastForward:
                if (CanPlaybackShuttle(allowWhileStopped: true))
                {
                    BeginOrContinuePlaybackShuttle(1);
                    ApplyTransportSelectionExtend();
                }

                break;
        }
    }

    private void StopEditorNumpadSelectionTransport(int digit)
    {
        if (!_transportSelectionExtend || _transportSelectionDigit != digit)
        {
            return;
        }

        var command = LibraryPlayerMode.NumpadSelectionTransportCommand(digit);
        // フラグは StopSeekNudge／StopPlaybackShuttle 側でも落とす。先に digit を渡すために残す。
        if (command is LibraryNumpadCommand.SeekBack or LibraryNumpadCommand.SeekForward)
        {
            StopSeekNudge();
        }
        else if (command is LibraryNumpadCommand.Rewind or LibraryNumpadCommand.FastForward)
        {
            StopPlaybackShuttle();
        }
        else
        {
            _transportSelectionDigit = -1;
            _transportSelectionExtend = false;
            SyncPlayWindowFromDocument();
        }
    }

    private void ApplyTransportSelectionExtend()
    {
        if (!_transportSelectionExtend || _document is null)
        {
            return;
        }

        Waveform.ExtendSelectionTo(Waveform.PlayheadFrame);
    }

    /// <summary>
    /// Shift／Ctrl＋テンキーの番号ジャンプ。Alt＋テンキーは表示範囲の割合（上段の無修飾と同じ）。
    /// エディタはドキュメントのマーカー／リージョン。
    /// プレイヤーの Shift は一時マーカー（音声波形／動画本再生／F8／フレーム一時停止）。
    /// プレイヤーの Ctrl はファイルにリージョンがあるときその先頭へ。
    /// 上段の Ctrl もプレイヤーでは同じ（ツリーの Shift+8 とは別キー）。
    /// </summary>
    private bool TryHandleNumpadCueDigitJump(Key key, ModifierKeys modifiers)
    {
        if (LibraryPlayerMode.IsNumpadPercentDigitJump(key, modifiers) && TryDigitKey(key, out var percentDigit))
        {
            var now = Environment.TickCount64;
            if (percentDigit != _numpadPercentJumpDigit || now - _numpadPercentJumpMs >= 40)
            {
                _numpadPercentJumpDigit = percentDigit;
                _numpadPercentJumpMs = now;
                JumpByVisiblePercent(percentDigit / 10d, extendSelection: false);
            }

            return true;
        }

        // NumLock オフ等で Shift＋NumPad が数字のまま届くとき、および Shift＋J／K。
        // 選択可能な波形コンテキストのみ（J／K は 7／9 と同じ ±5 秒）。
        // ツリーフォーカス中の J／K はフォルダ名ジャンプへ譲る。
        if (AllowsWaveformSelectionTransport()
            && LibraryPlayerMode.IsNumpadSelectionTransport(key, modifiers)
            && LibraryPlayerMode.TrySelectionTransportDigit(key, out var selectionDigit)
            && (key is not (Key.J or Key.K)
                || LibraryPlayerMode.AllowsPlayerLetterTransport(IsLibraryExplorerFocused)))
        {
            BeginEditorNumpadSelectionTransport(selectionDigit);
            return true;
        }

        var numpadMarker = LibraryPlayerMode.IsNumpadMarkerDigitJump(key, modifiers);
        var numpadRegion = LibraryPlayerMode.IsNumpadRegionDigitJump(key, modifiers);
        var playerTopRegion = IsLibraryMaximized && LibraryPlayerMode.IsRegionDigitJump(key, modifiers);
        if (!numpadMarker && !numpadRegion && !playerTopRegion)
        {
            return false;
        }

        if (!TryDigitKey(key, out var digit))
        {
            return false;
        }

        if (numpadMarker)
        {
            if (IsLibraryMaximized)
            {
                if (!LibraryPlayerMode.CanPlayerMarkerDigitJump(
                        playerMarkerContext: IsPlayerScratchContext(),
                        Waveform.HasPlayerScratchMarkers))
                {
                    return false;
                }

                ApplyResolvedMarkerDigitJump(
                    digit,
                    Waveform.ScratchCues.Markers.Count,
                    allowPercentFallback: false);
                return true;
            }

            if (_document is null || _document.Markers.Count == 0)
            {
                return true;
            }

            return ApplyResolvedMarkerDigitJump(
                digit,
                _document.Markers.Count,
                allowPercentFallback: false);
        }

        var regionCount = _document?.Regions.Count ?? 0;
        if (regionCount == 0)
        {
            return true;
        }

        return ApplyResolvedRegionDigitJump(digit, regionCount);
    }

    private bool TryHandleLibraryDigitJump(Key key, ModifierKeys modifiers)
    {
        if (!LibraryPlayerMode.IsDigitJumpChord(key, modifiers)
            || !TryDigitKey(key, out var digit))
        {
            return false;
        }

        // シークや行更新のあとでプレイリストへフォーカスが吸い戻されないように残す。
        var keepExplorer = IsLibraryExplorerFocused;
        var keepFavorites = IsLibraryFavoritesFocused;

        if (LibraryPlayerMode.IsMarkerDigitJump(key, modifiers))
        {
            if (LibraryPlayerMode.CanPlayerMarkerDigitJump(
                    playerMarkerContext: IsPlayerScratchContext(),
                    Waveform.HasPlayerScratchMarkers))
            {
                ApplyResolvedMarkerDigitJump(
                    digit,
                    Waveform.ScratchCues.Markers.Count,
                    allowPercentFallback: false);
            }
        }
        else if (TryDigitPercent(key, out var percent))
        {
            JumpByVisiblePercent(percent, extendSelection: false);
        }
        else
        {
            return false;
        }

        if (keepExplorer)
        {
            LibraryBrowser.RequestExplorerFocus();
        }
        else if (keepFavorites)
        {
            LibraryBrowser.FocusFavorites();
        }

        return true;
    }

    private static bool TryDigitKey(Key key, out int digit)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            digit = key - Key.D0;
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            digit = key - Key.NumPad0;
            return true;
        }

        digit = -1;
        return false;
    }

    /// <summary>再生中の音を半音上げ下げする。プレイヤーとエディタで同じ。</summary>
    private bool TryNudgePlaybackPitch(Key key, ModifierKeys modifiers)
    {
        if (_document is null)
        {
            return false;
        }

        if (LibraryPlayerMode.IsPlaybackPitchReset(key, modifiers))
        {
            // リージョン番号の二桁目（10 など）は従来どおり番号ジャンプへ渡す。
            if (_regionNumber != 0)
            {
                return false;
            }

            ApplyPlaybackPitchStep(0);
            return true;
        }

        if (!LibraryPlayerMode.TryPlaybackPitchStep(key, modifiers, out var step))
        {
            return false;
        }

        ApplyPlaybackPitchStep(step);
        return true;
    }

    /// <summary>再生中の音を半音動かす。0 で元の高さへ戻す。</summary>
    private void ApplyPlaybackPitchStep(int step)
    {
        if (_document is null)
        {
            return;
        }

        if (step == 0)
        {
            _player.SetPlaybackSemitones(0);
            ShowPlaybackPitchToast(returnedToOriginal: true);
            return;
        }

        var next = PitchShift.Snap(_player.PlaybackSemitones + step);
        if (!_player.SetPlaybackSemitones(next))
        {
            return;
        }

        ShowPlaybackPitchToast(returnedToOriginal: false);
    }

    /// <summary>ショートカットでピッチが変わったとき、中央に現在の半音を出してフェードアウト。</summary>
    private void ShowPlaybackPitchToast(bool returnedToOriginal)
    {
        PlaybackPitchToastText.Text = returnedToOriginal
            ? "Pitch Reset"
            : FormatPlaybackPitchToast(_player.PlaybackSemitones);
        LibraryPlayerMode.FadeElementOpacity(
            PlaybackPitchToast,
            to: 1,
            instant: true,
            hitTestVisible: false);
        _playbackPitchToastTimer ??= new DispatcherTimer
        {
            Interval = LibraryExplorerTypeahead.ToastIdleClear,
        };
        _playbackPitchToastTimer.Tick -= OnPlaybackPitchToastTick;
        _playbackPitchToastTimer.Tick += OnPlaybackPitchToastTick;
        _playbackPitchToastTimer.Stop();
        _playbackPitchToastTimer.Start();
    }

    private void OnPlaybackPitchToastTick(object? sender, EventArgs e)
    {
        _playbackPitchToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            PlaybackPitchToast,
            to: 0,
            instant: false,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
    }

    private static string FormatPlaybackPitchToast(int semitones)
    {
        var value = PitchShift.Snap(semitones);
        var number = value.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture);
        var unit = Math.Abs(value) == 1 ? "Semitone" : "Semitones";
        return number + " " + unit;
    }

    private bool TryHandleVideoLaunchScaleKey(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (LibraryPlayerMode.IsZoomInKey(key, modifiers))
        {
            return TryScaleVideoLaunchWindow(1, isRepeat);
        }

        if (LibraryPlayerMode.IsZoomOutKey(key, modifiers))
        {
            return TryScaleVideoLaunchWindow(-1, isRepeat);
        }

        return false;
    }

    /// <summary>
    /// エディタと同じ Home／End／PageUp／PageDown（無修飾・Shift）の波形タイムライン操作。
    /// 動画本再生・ミニ・PDF 本表示でもプレイリスト移動の代わりに使う。
    /// </summary>
    private bool TryHandleWaveformTimelineNavKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Home && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionToViewEdge(-1);
            return true;
        }

        if (key == Key.End && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionToViewEdge(1);
            return true;
        }

        if (key == Key.Home && modifiers == ModifierKeys.None)
        {
            Waveform.SeekToViewEdge(-1);
            return true;
        }

        if (key == Key.End && modifiers == ModifierKeys.None)
        {
            Waveform.SeekToViewEdge(1);
            return true;
        }

        if (key == Key.PageUp && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionByVisibleFraction(-0.05);
            return true;
        }

        if (key == Key.PageDown && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionByVisibleFraction(0.05);
            return true;
        }

        if (key == Key.PageUp && modifiers == ModifierKeys.None)
        {
            Waveform.SeekByVisibleFraction(-0.05);
            return true;
        }

        if (key == Key.PageDown && modifiers == ModifierKeys.None)
        {
            Waveform.SeekByVisibleFraction(0.05);
            return true;
        }

        return false;
    }

    private bool TryHandlePlaylistPdfKey(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        // 選択プレビュー（暗表示）ではプレイリスト操作のまま。表示モード中だけ奪う。
        if (!LibraryBrowser.PlaylistVisualIsPdf || !LibraryBrowser.PlaylistVisualPlaying)
        {
            return false;
        }

        if (LibraryPlayerMode.IsZoomInKey(key, modifiers))
        {
            return LibraryBrowser.TryZoomPlaylistPdf(1, isRepeat);
        }

        if (LibraryPlayerMode.IsZoomOutKey(key, modifiers))
        {
            return LibraryBrowser.TryZoomPlaylistPdf(-1, isRepeat);
        }

        // 本再生（明るい表示）からの抜けは Escape／Q。Space／Enter は抜かない。
        if (modifiers == ModifierKeys.None && key is Key.Escape or Key.Q)
        {
            if (!isRepeat)
            {
                ExitPlaylistPdfView();
            }

            return true;
        }

        if (modifiers == ModifierKeys.None && key is Key.Enter or Key.Space)
        {
            return true;
        }

        // 上段数字だけページ番号へ（テンキーは再生操作＝4 前／6 次などへ通す。二桁可）。
        if (LibraryPlayerMode.IsPdfPageDigitKey(key, modifiers)
            && TryDigitKey(key, out var pageDigit))
        {
            if (!isRepeat)
            {
                ApplyResolvedPdfPageDigitJump(pageDigit);
            }

            return true;
        }

        if (LibraryPlayerMode.IsPdfPageKey(key, modifiers))
        {
            _libraryPlayOnArrowRelease = false;
            ResetPdfPageDigitEntry();
            switch (key)
            {
                case Key.Left:
                    if (LibraryBrowser.TryStepPlaylistPdfPage(-1))
                    {
                        SyncPlaylistPdfCursorFromVisual(flashPageNumber: true);
                    }

                    return true;
                case Key.Right:
                    if (LibraryBrowser.TryStepPlaylistPdfPage(1))
                    {
                        SyncPlaylistPdfCursorFromVisual(flashPageNumber: true);
                    }

                    return true;
            }
        }

        return LibraryPlayerMode.BlocksPlaylistWhilePdf(key, modifiers);
    }

    private bool ApplyResolvedPdfPageDigitJump(int digit)
    {
        if (!LibraryPlayerMode.TryResolvePdfPageDigitJump(
                _pdfPageDigit,
                digit,
                LibraryBrowser.PlaylistPdfPageCount,
                out var nextPending,
                out var seekPage))
        {
            return false;
        }

        if (seekPage is int page)
        {
            _pdfPageDigit = nextPending;
            if (LibraryBrowser.TryGoPlaylistPdfPageNumber(page))
            {
                SyncPlaylistPdfCursorFromVisual(flashPageNumber: true);
            }

            RestartMarkerDigitTimer();
            return true;
        }

        // 無効桁は消費のみ。桁送りも切る。
        _pdfPageDigit = 0;
        return true;
    }

    private void ResetPdfPageDigitEntry() => _pdfPageDigit = 0;

    private void SyncPlaylistPdfCursorFromVisual(bool flashPageNumber = false)
    {
        if (_document is null || !LibraryPlaylistDocuments.IsPdf(_document))
        {
            return;
        }

        var rate = Math.Max(1, _document.SampleRate);
        var frame = (long)Math.Round(LibraryBrowser.PlaylistVisualPosition.TotalSeconds * rate);
        frame = Math.Clamp(frame, 0, _document.FrameCount);
        _document.CursorFrame = frame;
        Waveform.PlayheadFrame = frame;
        SyncOverviewPlayhead();
        RefreshStatus();
        RefreshPlaylistPdfPageNumber();
        if (flashPageNumber)
        {
            FlashPlaylistPdfPageNumber();
        }
    }
}
