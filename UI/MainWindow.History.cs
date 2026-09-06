using System.Windows;
using System.Windows.Input;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private int _historyAnchorIndex;
    private int _historySelectedIndex;

    private bool HistoryOpen => HistoryOverlay.Visibility == Visibility.Visible;

    private void OpenEditHistory()
    {
        if (_document is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _historyAnchorIndex = _history.CurrentIndex;
        _historySelectedIndex = _historyAnchorIndex;
        RefreshHistoryOverlay();
        HistoryOverlay.Visibility = Visibility.Visible;
    }

    private void CloseEditHistory(bool commit)
    {
        if (!HistoryOpen)
        {
            return;
        }

        if (!commit && _document is not null && _history.JumpTo(_document, _historyAnchorIndex))
        {
            AfterEdit();
        }

        HistoryOverlay.Visibility = Visibility.Collapsed;
    }

    private bool TryProcessHistoryShortcut(Key key, ModifierKeys modifiers)
    {
        if (!HistoryOpen)
        {
            return false;
        }

        if (modifiers != ModifierKeys.None)
        {
            return true;
        }

        switch (key)
        {
            case Key.Escape:
                CloseEditHistory(commit: false);
                return true;
            case Key.Enter:
            case Key.U:
                CloseEditHistory(commit: true);
                return true;
            case Key.Up:
                MoveHistorySelection(-1);
                return true;
            case Key.Down:
                MoveHistorySelection(1);
                return true;
            case Key.PageUp:
                MoveHistorySelection(-8);
                return true;
            case Key.PageDown:
                MoveHistorySelection(8);
                return true;
            case Key.Home:
                PreviewHistoryIndex(0);
                return true;
            case Key.End:
                PreviewHistoryIndex(_history.TotalCount);
                return true;
            default:
                return true;
        }
    }

    private void MoveHistorySelection(int delta)
    {
        PreviewHistoryIndex(_historySelectedIndex + delta);
    }

    private void PreviewHistoryIndex(int index)
    {
        if (_document is null)
        {
            return;
        }

        var next = Math.Clamp(index, 0, _history.TotalCount);
        if (next == _historySelectedIndex && _history.CurrentIndex == next)
        {
            RefreshHistoryOverlay();
            return;
        }

        _historySelectedIndex = next;
        if (_history.JumpTo(_document, next))
        {
            AfterEdit();
        }

        RefreshHistoryOverlay();
    }

    private void RefreshHistoryOverlay()
    {
        HistoryOverlay.SetItems(_history.Snapshot(), _historySelectedIndex);
    }

    private void HistoryOverlay_ItemChosen(object sender, int index)
    {
        PreviewHistoryIndex(index);
    }
}
