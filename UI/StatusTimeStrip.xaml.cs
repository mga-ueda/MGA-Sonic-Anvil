using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum StatusTimeField
{
    Current,
    SelStart,
    SelLength,
    SelEnd,
    Total,
}

internal partial class StatusTimeStrip : UserControl
{
    private readonly Dictionary<TextBox, FieldState> _fields = new();
    private long _currentFrame;
    private WaveSelection _selection = WaveSelection.Empty;
    private long _totalFrames;
    private int _sampleRate;
    private bool _hasDocument;
    private bool _showSamples;
    private bool _committing;

    public event EventHandler<long>? CurrentCommitted;

    public event EventHandler<WaveSelection>? SelectionCommitted;

    public event EventHandler? RequestWaveformFocus;

    public bool IsTimeFocused => _fields.Keys.Any(box => box.IsKeyboardFocusWithin);

    public bool IsEditing => _fields.Values.Any(state => state.Editing) || IsTimeFocused;

    public StatusTimeStrip()
    {
        InitializeComponent();
        _showSamples = AppStorage.Settings.StatusShowSamples;
        Bind(CurrentBox, StatusTimeField.Current);
        Bind(SelStartBox, StatusTimeField.SelStart);
        Bind(SelLengthBox, StatusTimeField.SelLength);
        Bind(SelEndBox, StatusTimeField.SelEnd);
        TotalText.ContextMenu = CreateMenu(StatusTimeField.Total);
        ContextMenu = CreateMenu(StatusTimeField.Total);
        ApplyLocalizedText();
        SetState(0, WaveSelection.Empty, 0, 0, hasDocument: false);
    }

    public void ApplyLocalizedText()
    {
        CurrentLabel.Text = UiStrings.LabelStatusNowPos;
        SelStartLabel.Text = UiStrings.LabelStatusSelStart;
        SelLengthLabel.Text = UiStrings.LabelStatusSelWidth;
        SelEndLabel.Text = UiStrings.LabelStatusSelEnd;
        TotalLabel.Text = UiStrings.LabelStatusEndPos;
        TipService.Set(CurrentLabel, UiStrings.TipTimecode, respectsEnabled: false);
        TipService.Set(CurrentBox, UiStrings.TipTimecode);
        TipService.Set(SelStartLabel, UiStrings.TipSelectionStartTime, respectsEnabled: false);
        TipService.Set(SelStartBox, UiStrings.TipSelectionStartTime);
        TipService.Set(SelLengthLabel, UiStrings.TipSelectionLengthTime, respectsEnabled: false);
        TipService.Set(SelLengthBox, UiStrings.TipSelectionLengthTime);
        TipService.Set(SelEndLabel, UiStrings.TipSelectionEndTime, respectsEnabled: false);
        TipService.Set(SelEndBox, UiStrings.TipSelectionEndTime);
        TipService.Set(TotalLabel, UiStrings.TipTotalTime, respectsEnabled: false);
        TipService.Set(TotalText, UiStrings.TipTotalTime, respectsEnabled: false);
    }

    public void SetState(long currentFrame, WaveSelection selection, long totalFrames, int sampleRate, bool hasDocument)
    {
        _currentFrame = Math.Max(0, currentFrame);
        _selection = selection;
        _totalFrames = Math.Max(0, totalFrames);
        _sampleRate = sampleRate;
        _hasDocument = hasDocument;
        CurrentBox.IsEnabled = hasDocument;
        SelStartBox.IsEnabled = hasDocument;
        SelLengthBox.IsEnabled = hasDocument;
        SelEndBox.IsEnabled = hasDocument;
        if (!hasDocument)
        {
            CancelEdit();
        }

        RefreshTexts();
    }

    public void CancelEdit()
    {
        foreach (var state in _fields.Values)
        {
            EndEdit(state, commit: false);
        }
    }

    public bool FocusCurrentTime()
    {
        if (!CurrentBox.IsEnabled)
        {
            return false;
        }

        _fields[CurrentBox].SelectAllOnFocus = true;
        return CurrentBox.Focus();
    }

    private void Bind(TextBox box, StatusTimeField field)
    {
        var state = new FieldState(field, box);
        _fields[box] = state;
        box.ContextMenu = CreateMenu(field);
        box.Tag = state;
    }

    private ContextMenu CreateMenu(StatusTimeField field)
    {
        var menu = new ContextMenu();
        var timeItem = new MenuItem();
        var sampleItem = new MenuItem();
        var copyItem = new MenuItem();
        var pasteItem = new MenuItem();
        timeItem.Click += (_, _) => SetShowSamples(false);
        sampleItem.Click += (_, _) => SetShowSamples(true);
        copyItem.Click += (_, _) => CopyField(field);
        pasteItem.Click += (_, _) => PasteField(field);
        menu.Items.Add(timeItem);
        menu.Items.Add(sampleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Opened += (_, _) =>
        {
            timeItem.Header = Mark(!_showSamples, UiStrings.MenuShowTime);
            sampleItem.Header = Mark(_showSamples, UiStrings.MenuShowSamples);
            copyItem.Header = UiStrings.MenuCopy;
            pasteItem.Header = UiStrings.MenuPaste;
            copyItem.IsEnabled = CopyText(field).Length > 0;
            pasteItem.IsEnabled = field != StatusTimeField.Total && _hasDocument && Clipboard.ContainsText();
            TipService.Set(timeItem, UiStrings.MenuShowTime);
            TipService.Set(sampleItem, UiStrings.MenuShowSamples);
            TipService.Set(copyItem, UiStrings.MenuCopy);
            TipService.Set(pasteItem, UiStrings.MenuPaste);
        };
        return menu;
    }

    private static string Mark(bool on, string label) => on ? "●  " + label : "○  " + label;

    private void SetShowSamples(bool showSamples)
    {
        if (_showSamples == showSamples)
        {
            return;
        }

        CancelEdit();
        _showSamples = showSamples;
        AppStorage.Settings.StatusShowSamples = showSamples;
        AppStorage.Save();
        RefreshTexts();
    }

    private void RefreshTexts()
    {
        foreach (var state in _fields.Values)
        {
            if (!state.Editing)
            {
                Write(state.Box, FormatField(state.Field));
            }
        }

        var total = FormatField(StatusTimeField.Total);
        if (TotalText.Text != total)
        {
            TotalText.Text = total;
        }
    }

    private static void Write(TextBox box, string text)
    {
        if (box.Text != text)
        {
            box.Text = text;
        }
    }

    private string FormatField(StatusTimeField field)
    {
        if (field is StatusTimeField.SelStart or StatusTimeField.SelLength or StatusTimeField.SelEnd
            && _selection.IsEmpty)
        {
            return string.Empty;
        }

        var frame = field switch
        {
            StatusTimeField.Current => _currentFrame,
            StatusTimeField.SelStart => _selection.StartFrame,
            StatusTimeField.SelLength => _selection.Length,
            StatusTimeField.SelEnd => _selection.EndFrame,
            _ => _totalFrames,
        };
        return UiStrings.FormatStatusTime(frame, _sampleRate, _showSamples);
    }

    private void Field_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox box && _fields.TryGetValue(box, out var state))
        {
            Nudge(state, Math.Sign(e.Delta));
            e.Handled = true;
        }
    }

    private void Label_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var field = sender == CurrentLabel ? StatusTimeField.Current
            : sender == SelStartLabel ? StatusTimeField.SelStart
            : sender == SelLengthLabel ? StatusTimeField.SelLength
            : sender == SelEndLabel ? StatusTimeField.SelEnd
            : StatusTimeField.Total;
        if (field != StatusTimeField.Total && TryGetBox(field, out var box) && _fields.TryGetValue(box, out var state))
        {
            Nudge(state, Math.Sign(e.Delta));
            e.Handled = true;
        }
    }

    private void Nudge(FieldState state, int direction)
    {
        if (!_hasDocument || direction == 0)
        {
            return;
        }

        var step = StatusTimeEdit.NudgeStep(_showSamples, _sampleRate, Keyboard.Modifiers);
        var value = FieldValue(state) + (long)direction * step;
        if (value < 0)
        {
            value = 0;
        }

        ApplyValue(state.Field, value);
        Write(state.Box, FormatField(state.Field));
        if (state.Box.IsKeyboardFocusWithin)
        {
            state.Box.CaretIndex = state.Box.Text.Length;
        }
    }

    private long FieldValue(FieldState state)
    {
        if (UiStrings.TryParseStatusTime(state.Box.Text, _sampleRate, _showSamples, out var parsed))
        {
            return parsed;
        }

        return state.Field switch
        {
            StatusTimeField.Current => _currentFrame,
            StatusTimeField.SelStart => _selection.IsEmpty ? 0 : _selection.StartFrame,
            StatusTimeField.SelLength => _selection.Length,
            StatusTimeField.SelEnd => _selection.IsEmpty ? 0 : _selection.EndFrame,
            _ => 0,
        };
    }

    private void ApplyValue(StatusTimeField field, long frame)
    {
        if (field == StatusTimeField.Current)
        {
            CurrentCommitted?.Invoke(this, StatusTimeEdit.ClampFrame(frame, _totalFrames));
            return;
        }

        var next = field switch
        {
            StatusTimeField.SelStart => StatusTimeEdit.ApplyStart(_selection, frame, _totalFrames),
            StatusTimeField.SelLength => StatusTimeEdit.ApplyLength(_selection, frame, _currentFrame, _totalFrames),
            StatusTimeField.SelEnd => StatusTimeEdit.ApplyEnd(_selection, frame, _totalFrames),
            _ => _selection,
        };
        SelectionCommitted?.Invoke(this, next);
    }

    private void Field_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || !box.IsEnabled || !_fields.TryGetValue(box, out var state))
        {
            return;
        }

        if (!box.IsKeyboardFocusWithin)
        {
            state.SelectAllOnFocus = true;
            box.Focus();
            e.Handled = true;
        }
    }

    private void Field_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box || !_fields.TryGetValue(box, out var state))
        {
            return;
        }

        state.Editing = true;
        if (state.SelectAllOnFocus)
        {
            state.SelectAllOnFocus = false;
            box.Dispatcher.BeginInvoke(box.SelectAll);
        }
    }

    private void Field_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || !_fields.TryGetValue(box, out var state))
        {
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            Nudge(state, e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            EndEdit(state, commit: true);
            RequestWaveformFocus?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            EndEdit(state, commit: false);
            e.Handled = true;
            return;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.None;
        if (ctrl && e.Key == Key.C)
        {
            CopyField(state.Field);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.V)
        {
            PasteField(state.Field);
            e.Handled = true;
        }
    }

    private void Field_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box || !_fields.TryGetValue(box, out var state))
        {
            return;
        }

        if (state.Editing && !_committing)
        {
            EndEdit(state, commit: true);
        }
    }

    private void EndEdit(FieldState state, bool commit)
    {
        if (!state.Editing)
        {
            Write(state.Box, FormatField(state.Field));
            return;
        }

        _committing = true;
        try
        {
            state.Editing = false;
            if (commit && TryCommit(state))
            {
                return;
            }

            Write(state.Box, FormatField(state.Field));
            if (state.Box.IsKeyboardFocusWithin && !commit)
            {
                Keyboard.ClearFocus();
            }
        }
        finally
        {
            _committing = false;
        }
    }

    private bool TryCommit(FieldState state)
    {
        if (!_hasDocument || !UiStrings.TryParseStatusTime(state.Box.Text, _sampleRate, _showSamples, out var frame))
        {
            return false;
        }

        if (state.Field == StatusTimeField.Current)
        {
            CurrentCommitted?.Invoke(this, StatusTimeEdit.ClampFrame(frame, _totalFrames));
            return true;
        }

        var next = state.Field switch
        {
            StatusTimeField.SelStart => StatusTimeEdit.ApplyStart(_selection, frame, _totalFrames),
            StatusTimeField.SelLength => StatusTimeEdit.ApplyLength(_selection, frame, _currentFrame, _totalFrames),
            StatusTimeField.SelEnd => StatusTimeEdit.ApplyEnd(_selection, frame, _totalFrames),
            _ => _selection,
        };
        SelectionCommitted?.Invoke(this, next);
        return true;
    }

    private string CopyText(StatusTimeField field)
    {
        if (TryGetBox(field, out var box) && box.IsKeyboardFocusWithin && box.SelectionLength > 0)
        {
            return box.SelectedText;
        }

        return FormatField(field);
    }

    private void CopyField(StatusTimeField field)
    {
        var text = CopyText(field);
        if (text.Length == 0)
        {
            return;
        }

        Clipboard.SetText(text);
    }

    private void PasteField(StatusTimeField field)
    {
        if (field == StatusTimeField.Total || !_hasDocument || !Clipboard.ContainsText()
            || !TryGetBox(field, out var box))
        {
            return;
        }

        var text = Clipboard.GetText();
        if (!box.IsKeyboardFocusWithin
            && UiStrings.TryParseStatusTime(text, _sampleRate, _showSamples, out var frame))
        {
            var state = _fields[box];
            state.Editing = false;
            if (field == StatusTimeField.Current)
            {
                CurrentCommitted?.Invoke(this, StatusTimeEdit.ClampFrame(frame, _totalFrames));
                return;
            }

            var next = field switch
            {
                StatusTimeField.SelStart => StatusTimeEdit.ApplyStart(_selection, frame, _totalFrames),
                StatusTimeField.SelLength => StatusTimeEdit.ApplyLength(_selection, frame, _currentFrame, _totalFrames),
                StatusTimeField.SelEnd => StatusTimeEdit.ApplyEnd(_selection, frame, _totalFrames),
                _ => _selection,
            };
            SelectionCommitted?.Invoke(this, next);
            return;
        }

        var edit = _fields[box];
        edit.Editing = true;
        box.Focus();
        box.SelectedText = text;
    }

    private bool TryGetBox(StatusTimeField field, out TextBox box)
    {
        foreach (var pair in _fields)
        {
            if (pair.Value.Field == field)
            {
                box = pair.Key;
                return true;
            }
        }

        box = null!;
        return false;
    }

    private sealed class FieldState(StatusTimeField field, TextBox box)
    {
        public StatusTimeField Field { get; } = field;

        public TextBox Box { get; } = box;

        public bool Editing { get; set; }

        public bool SelectAllOnFocus { get; set; }
    }
}
