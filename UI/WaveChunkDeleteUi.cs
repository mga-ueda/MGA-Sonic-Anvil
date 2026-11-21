using System.Text;
using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>チャンクツリーの削除確認と削除プラン組み立て。</summary>
internal static class WaveChunkDeleteUi
{
    public readonly record struct Plan(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> PathToChunkIds,
        IReadOnlyList<string> ForbiddenIds,
        IReadOnlyList<ChunkEntry> Entries);

    public readonly record struct ChunkEntry(
        string Id,
        WaveChunkCleanVerdict Verdict,
        string Description);

    /// <summary>選択に削除可能なチャンクが1つでもあれば true（メニューの有効／グレー用）。</summary>
    public static bool HasDeletableSelection(IEnumerable<TreeViewItem> selectedItems)
    {
        foreach (var item in selectedItems)
        {
            if (item.Tag is WaveStructureNode node
                && WaveChunkCleanPolicy.CanDelete(node.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>テスト／判定用。ノード列に削除可能なものが含まれるか。</summary>
    internal static bool HasDeletableNodes(IEnumerable<WaveStructureNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (WaveChunkCleanPolicy.CanDelete(node.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>選択 TreeViewItem から削除プランを作る。singleFilePath は和集合以外の単一ファイル。</summary>
    public static Plan? BuildPlan(
        IEnumerable<TreeViewItem> selectedItems,
        string? singleFilePath)
    {
        var entriesById = new Dictionary<string, ChunkEntry>(StringComparer.Ordinal);
        var pathMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var forbidden = new List<string>();

        foreach (var item in selectedItems)
        {
            if (item.Tag is not WaveStructureNode node)
            {
                continue;
            }

            var id = node.Id;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var verdict = WaveChunkCleanPolicy.Classify(id);
            if (!entriesById.ContainsKey(id))
            {
                entriesById[id] = new ChunkEntry(id, verdict, WaveChunkCleanPolicy.Describe(id));
            }

            if (verdict == WaveChunkCleanVerdict.Forbidden)
            {
                if (!forbidden.Contains(id, StringComparer.Ordinal))
                {
                    forbidden.Add(id);
                }

                continue;
            }

            if (node.MemberPaths.Count > 0)
            {
                foreach (var path in node.MemberPaths)
                {
                    Add(pathMap, path, id);
                }
            }
            else if (!string.IsNullOrWhiteSpace(singleFilePath))
            {
                Add(pathMap, singleFilePath, id);
            }
        }

        if (entriesById.Count == 0)
        {
            return null;
        }

        var frozen = pathMap.ToDictionary(
            static kv => kv.Key,
            static kv => (IReadOnlyCollection<string>)kv.Value,
            StringComparer.OrdinalIgnoreCase);

        return new Plan(
            frozen,
            forbidden,
            entriesById.Values
                .OrderBy(static e => e.Verdict)
                .ThenBy(static e => e.Id, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// 確認ダイアログ。キャンセルなら false。
    /// 不可のみなら警告して false。
    /// </summary>
    public static bool Confirm(Window? owner, Plan plan)
    {
        var deletable = plan.Entries
            .Where(static e => e.Verdict != WaveChunkCleanVerdict.Forbidden)
            .ToArray();
        if (deletable.Length == 0)
        {
            OwnerCenteredMessageBox.Show(
                owner,
                UiStrings.WaveChunkDeleteForbiddenOnly,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        var hasProtected = deletable.Any(static e => e.Verdict == WaveChunkCleanVerdict.Protected);
        var hasCaution = deletable.Any(static e => e.Verdict == WaveChunkCleanVerdict.Caution);
        var answer = OwnerCenteredMessageBox.Show(
            owner,
            FormatConfirmMessage(plan),
            UiStrings.AppName,
            MessageBoxButton.YesNo,
            hasProtected || hasCaution ? MessageBoxImage.Warning : MessageBoxImage.Question,
            MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }

    /// <summary>確認文面（fact 注意の有無を含む）。</summary>
    internal static string FormatConfirmMessage(Plan plan)
    {
        var deletable = plan.Entries
            .Where(static e => e.Verdict != WaveChunkCleanVerdict.Forbidden)
            .ToArray();
        if (deletable.Length == 0)
        {
            return UiStrings.WaveChunkDeleteForbiddenOnly;
        }

        var sb = new StringBuilder();
        if (plan.ForbiddenIds.Count > 0)
        {
            sb.AppendLine(UiStrings.WaveChunkDeleteForbiddenSkipped(
                string.Join(
                    Environment.NewLine,
                    plan.ForbiddenIds.Select(WaveChunkCleanPolicy.FormatId))));
            sb.AppendLine();
        }

        foreach (var entry in deletable)
        {
            sb.Append(WaveChunkCleanPolicy.FormatId(entry.Id));
            sb.Append(" — ");
            sb.AppendLine(entry.Description);
        }

        var body = sb.ToString().TrimEnd();
        var hasProtected = deletable.Any(static e => e.Verdict == WaveChunkCleanVerdict.Protected);
        var hasCaution = deletable.Any(static e => e.Verdict == WaveChunkCleanVerdict.Caution);
        var message = hasProtected && hasCaution
            ? UiStrings.WaveChunkConfirmDeleteMixed(body)
            : hasProtected
                ? UiStrings.WaveChunkConfirmDeleteProtected(body)
                : hasCaution
                    ? UiStrings.WaveChunkConfirmDeleteCaution(body)
                    : UiStrings.WaveChunkConfirmDeleteRemovable(body);

        // 17bit 超／非 PCM の保存では AudioCodec が fact を書き戻す。
        if (deletable.Any(static e => e.Id == "fact"))
        {
            message = message + "\n\n" + UiStrings.WaveChunkDeleteFactMayReturn;
        }

        return message;
    }

    private static void Add(
        Dictionary<string, HashSet<string>> map,
        string path,
        string id)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!map.TryGetValue(path, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            map[path] = set;
        }

        set.Add(id);
    }
}
