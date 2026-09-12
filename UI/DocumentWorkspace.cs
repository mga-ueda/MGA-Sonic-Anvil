using System.IO;

namespace MgaSonicAnvil.UI;

internal readonly record struct ClosedTab(DocumentSession Session, int Index);

/// <summary>開いているタブと、閉じたタブの履歴だけを持つ。</summary>
internal sealed class DocumentWorkspace
{
    public const int ClosedTabLimit = 32;

    public List<DocumentSession> Sessions { get; } = [];

    public DocumentSession? Active { get; set; }

    public HashSet<DocumentSession> SelectedTabs { get; } = [];

    public DocumentSession? SelectionAnchor { get; set; }

    private readonly List<ClosedTab> _closedTabs = [];

    public IReadOnlyList<ClosedTab> ClosedTabs => _closedTabs;

    public void RememberClosed(DocumentSession session, int index)
    {
        _closedTabs.Add(new ClosedTab(session, index));
        if (_closedTabs.Count > ClosedTabLimit)
        {
            _closedTabs.RemoveAt(0);
        }
    }

    public bool TryTakeLastClosed(out ClosedTab closed)
    {
        if (_closedTabs.Count == 0)
        {
            closed = default;
            return false;
        }

        closed = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        return true;
    }

    public DocumentSession? FindByPath(string path)
    {
        if (!TryNormalizePath(path, out var full))
        {
            return null;
        }

        foreach (var session in Sessions)
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

    public static bool TryNormalizePath(string path, out string full)
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
}
