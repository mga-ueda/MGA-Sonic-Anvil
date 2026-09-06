using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void CaptureActiveSessionView()
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession.TimeZoom = Waveform.TimeZoom;
        _activeSession.AmpZoom = Waveform.AmpZoom;
        _activeSession.ViewStart = Waveform.ViewStart;
        _activeSession.PlayheadFrame = Waveform.PlayheadFrame;
        _activeSession.LoopEnabled = Waveform.LoopEnabled;
        _activeSession.SelectedMarkerFrames.Clear();
        _activeSession.SelectedMarkerFrames.AddRange(Waveform.SelectedMarkerFrames);
    }

    private void ActivateSession(DocumentSession session)
    {
        if (ReferenceEquals(_activeSession, session) && ReferenceEquals(_document, session.Document))
        {
            RebuildTabBar();
            return;
        }

        if (_activeSession is not null && _sessions.Contains(_activeSession))
        {
            CaptureActiveSessionView();
        }

        BindWorkspace(session);
    }

    private void ActivateAdjacentTab(int delta)
    {
        if (_sessions.Count <= 1 || _activeSession is null)
        {
            return;
        }

        var index = _sessions.IndexOf(_activeSession);
        if (index < 0)
        {
            return;
        }

        var count = _sessions.Count;
        var next = ((index + delta) % count + count) % count;
        ActivateSession(_sessions[next]);
    }

    private void CloseSession(DocumentSession session)
    {
        if (!OfferSaveIfDirty(session))
        {
            return;
        }

        var index = _sessions.IndexOf(session);
        if (index < 0)
        {
            return;
        }

        var closingActive = ReferenceEquals(session, _activeSession);
        _sessions.RemoveAt(index);
        if (_sessions.Count == 0)
        {
            BindWorkspace(null);
            ForgetClosedDocument();
            return;
        }

        if (closingActive)
        {
            BindWorkspace(_sessions[Math.Min(index, _sessions.Count - 1)]);
        }
        else
        {
            RebuildTabBar();
            RefreshTabHeaders();
        }
    }

    private DocumentSession? FindSessionByPath(string path)
    {
        if (!TryNormalizePath(path, out var full))
        {
            return null;
        }

        foreach (var session in _sessions)
        {
            if (session.Document.SourcePath is not { } existing
                || !TryNormalizePath(existing, out var existingFull))
            {
                continue;
            }

            if (string.Equals(existingFull, full, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }

        return null;
    }

    private static bool TryNormalizePath(string path, out string full)
    {
        try
        {
            full = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            full = path;
            return !string.IsNullOrWhiteSpace(path);
        }
    }

    private void RebuildTabBar()
    {
        DocumentTabs.Children.Clear();
        DocumentTabHost.Visibility = _sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var session in _sessions)
        {
            DocumentTabs.Children.Add(CreateTabItem(session));
        }

        foreach (var child in DocumentTabs.Children.OfType<FrameworkElement>())
        {
            if (child.Tag is DocumentSession session && ReferenceEquals(session, _activeSession))
            {
                child.BringIntoView();
                break;
            }
        }
    }

    private void RefreshTabHeaders()
    {
        foreach (var border in DocumentTabs.Children.OfType<Border>())
        {
            if (border.Tag is not DocumentSession session || border.Child is not DockPanel dock)
            {
                continue;
            }

            ApplyTabChrome(session, dock);
        }
    }

    private FrameworkElement CreateTabItem(DocumentSession session)
    {
        var active = ReferenceEquals(session, _activeSession);
        var border = new Border
        {
            Tag = session,
            Background = BrushOrTransparent(active ? "ChromeMidBrush" : null),
            BorderBrush = (Brush)FindResource("ChromeBorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Cursor = Cursors.Hand,
            MinWidth = 80,
            MaxWidth = 220,
        };

        var title = new TextBlock
        {
            Text = session.TabTitle,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = session.Document.SourcePath ?? UiStrings.UntitledDocument,
        };

        var close = new TextBlock
        {
            Text = "×",
            Width = 14,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("MutedForeBrush"),
            Cursor = Cursors.Hand,
            ToolTip = UiStrings.TipCloseTab,
        };
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseSession(session);
        };

        var grid = new Grid { Margin = new Thickness(10, 0, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(close, 1);
        grid.Children.Add(title);
        grid.Children.Add(close);

        var body = new DockPanel();
        var underline = new Border { Height = 2 };
        DockPanel.SetDock(underline, Dock.Bottom);
        body.Children.Add(underline);
        body.Children.Add(grid);
        ApplyTabChrome(session, body);
        border.Child = body;
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!e.Handled)
            {
                ActivateSession(session);
            }
        };
        border.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                CloseSession(session);
            }
        };
        return border;
    }

    private Brush BrushOrTransparent(string? key) =>
        key is null ? Brushes.Transparent : (Brush)FindResource(key);

    private void ApplyTabChrome(DocumentSession session, DockPanel dock)
    {
        var dirty = session.Document.IsDirty;
        var active = ReferenceEquals(session, _activeSession);
        var accent = (Brush)FindResource(dirty ? "DirtyAccentBrush" : "AccentCyanBrush");
        var titleBrush = dirty
            ? accent
            : (Brush)FindResource(active ? "PrimaryForeBrush" : "MutedForeBrush");

        foreach (var child in dock.Children)
        {
            if (child is Border underline && underline.Height == 2)
            {
                underline.Background = active ? accent : Brushes.Transparent;
                underline.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (child is Grid grid
                && grid.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
            {
                title.Text = session.TabTitle;
                title.Foreground = titleBrush;
            }
        }
    }

    private bool OfferSaveAllDirty()
    {
        foreach (var session in _sessions.ToArray())
        {
            if (!OfferSaveIfDirty(session))
            {
                return false;
            }
        }

        return true;
    }
}
