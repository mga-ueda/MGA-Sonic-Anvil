using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>WAVE / AIFF のコンテナ構造。プレイリスト右のヘッダ検査用。</summary>
internal enum WaveStructureContentKind
{
    Empty,
    Text,
    Binary,
    Xml,
}

internal sealed class WaveStructureNode
{
    public WaveStructureNode(
        string id,
        string label,
        long offset,
        long size,
        WaveStructureContentKind kind,
        string content,
        bool contentTruncated,
        bool isInvalid,
        string? error,
        IReadOnlyList<WaveStructureNode>? children = null,
        IReadOnlyList<string>? memberPaths = null,
        bool contentPending = false)
    {
        Id = id;
        Label = label;
        Offset = offset;
        Size = size;
        Kind = kind;
        Content = content;
        ContentTruncated = contentTruncated;
        IsInvalid = isInvalid;
        Error = error;
        Children = children ?? Array.Empty<WaveStructureNode>();
        MemberPaths = memberPaths ?? Array.Empty<string>();
        ContentPending = contentPending;
        IsCustom = !IsWellKnownChunkId(id);
        IsSpecialized = IsSpecializedChunkId(id);
    }

    public string Id { get; }
    public string Label { get; }
    public long Offset { get; }
    public long Size { get; }
    public WaveStructureContentKind Kind { get; }
    public string Content { get; }
    public bool ContentTruncated { get; }
    public bool IsInvalid { get; }
    public string? Error { get; }
    /// <summary>true のとき Content 未読。チャンク選択時に <see cref="WaveContainerStructure.EnsureContent"/>。</summary>
    public bool ContentPending { get; }
    /// <summary>RIFF/AIFF で一般に定義されていない独自 FourCC。</summary>
    public bool IsCustom { get; }
    /// <summary>標準コアではないがゴミではない専門メタ（iXML / bext 等）。</summary>
    public bool IsSpecialized { get; }
    public IReadOnlyList<WaveStructureNode> Children { get; }
    /// <summary>和集合ノードが指すファイルパス。通常ノードは空。</summary>
    public IReadOnlyList<string> MemberPaths { get; }

    /// <summary>階層インデント（等幅スペース）。テンプレート側の字下げは使わない。</summary>
    internal const int TreeIndentSpaces = 2;

    /// <summary>子を持たない同一内容の行。コンテナ根をツリー親にしない表示用。</summary>
    public WaveStructureNode WithoutChildren() =>
        Children.Count == 0
            ? this
            : new WaveStructureNode(
                Id,
                Label,
                Offset,
                Size,
                Kind,
                Content,
                ContentTruncated,
                IsInvalid,
                Error,
                children: null,
                memberPaths: MemberPaths,
                contentPending: ContentPending);

    /// <summary>遅延読込後の本文を載せた同一チャンク行。子・Label は維持。</summary>
    public WaveStructureNode WithLoadedContent(
        WaveStructureContentKind kind,
        string content,
        bool contentTruncated,
        bool isInvalid,
        string? error) =>
        new(
            Id,
            Label,
            Offset,
            Size,
            kind,
            content,
            contentTruncated,
            isInvalid,
            error,
            Children,
            MemberPaths,
            contentPending: false);

    /// <summary>
    /// UI ツリー用のトップレベル行。コンテナ根（RIFF/FORM 等）は子付きの親にせず
    /// 1行だけ出し、その子をツリーのルートにする。
    /// </summary>
    internal static IReadOnlyList<WaveStructureNode> ForTreeDisplay(IReadOnlyList<WaveStructureNode> roots)
    {
        if (roots.Count == 0 || roots.All(static r => r.Children.Count == 0))
        {
            return roots;
        }

        var display = new List<WaveStructureNode>(roots.Count * 2);
        foreach (var root in roots)
        {
            if (root.Children.Count == 0)
            {
                display.Add(root);
                continue;
            }

            display.Add(root.WithoutChildren());
            display.AddRange(root.Children);
        }

        return display;
    }

    /// <summary>ファイルをまたいで同じチャンクを探すキー。LIST 等は種別込みの Label。</summary>
    public string SelectionKey =>
        Id is "LIST" or "list" or "RIFF" or "RF64" or "BW64" or "FORM" or "RIFX"
            ? Label
            : Id;

    /// <summary>
    /// FourCC を4文字に整形。足りなければ末尾に空白を足す（左寄せ）。
    /// NUL 等の非印字は '?'。空名のツリー表示は <see cref="FormatDisplayFourCc"/>。
    /// 例: "fmt" → "fmt ", "data" → "data", "\0\0\0\0" → "????"
    /// </summary>
    internal static string FormatFourCc(string id)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = id is { Length: > 0 } && i < id.Length ? id[i] : ' ';
            // ASCII 印字可能のみ通す。それ以外は '?'（空に見える／コピー不能を防ぐ）。
            chars[i] = c is >= (char)0x20 and <= (char)0x7E ? c : '?';
        }

        return new string(chars);
    }

    /// <summary>
    /// FourCC に印字可能な非空白が無い（NUL×4・空白のみなど）。
    /// 正規の WAVE チャンク名ではない。
    /// </summary>
    internal static bool IsBlankFourCc(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return true;
        }

        var hasVisible = false;
        for (var i = 0; i < 4; i++)
        {
            var c = i < id.Length ? id[i] : ' ';
            if (c is >= (char)0x21 and <= (char)0x7E)
            {
                hasVisible = true;
            }
            else if (c is < (char)0x20 or > (char)0x7E)
            {
                return true;
            }
        }

        return !hasVisible;
    }

    /// <summary>
    /// リスト用の不正／空 FourCC・修復対象の表示（4文字）。
    /// 正規チャンク名に見えにくいよう <c>****</c>。情報欄の本文は "(empty)" のまま。
    /// </summary>
    internal const string BlankFourCcListLabel = "****";

    /// <summary>
    /// 和集合キー用。空 FourCC および修復対象 WAVE（切り詰め等）を <c>****</c> 行へ畳む。
    /// </summary>
    internal const string BlankFourCcCanonicalId = "\0\0\0\0";

    /// <summary>ツリー／コピー用。空／不正 FourCC は <see cref="BlankFourCcListLabel"/>。</summary>
    internal string FormatDisplayFourCc() =>
        IsBlankFourCc(Id) ? BlankFourCcListLabel : FormatFourCc(Id);

    /// <summary>和集合のマージキー。空／不正 FourCC はすべて <see cref="BlankFourCcCanonicalId"/>。</summary>
    internal static string UnionChunkKey(string? id) =>
        IsBlankFourCc(id) ? BlankFourCcCanonicalId : (id ?? string.Empty);

    /// <summary>
    /// インデント込みで名前欄を固定幅にし、区切りとサイズを縦揃えする。
    /// 例: "RIFF    - 26,993,354 B" / "  fmt  -         16 B"
    /// </summary>
    internal string FormatTreeCaption(int depth, int nameFieldWidth, int sizeFieldWidth)
    {
        var name = (new string(' ', Math.Max(0, depth) * TreeIndentSpaces) + FormatDisplayFourCc())
            .PadRight(Math.Max(nameFieldWidth, 4));
        if (Size <= 0)
        {
            return name;
        }

        var width = Math.Max(sizeFieldWidth, FormatSize(Size).Length);
        return $"{name} - {FormatSize(Size).PadLeft(width)}";
    }

    /// <summary>
    /// 和集合一覧用。Size には「そのチャンクを持つファイル数」を入れる。
    /// 例: "fmt  -  2"
    /// </summary>
    internal string FormatUnionTreeCaption(int nameFieldWidth, int countFieldWidth)
    {
        var name = FormatDisplayFourCc().PadRight(Math.Max(nameFieldWidth, 4));
        var count = Size.ToString("#,0", CultureInfo.InvariantCulture);
        var width = Math.Max(countFieldWidth, count.Length);
        return $"{name} - {count.PadLeft(width)}";
    }

    internal static int MaxDepth(IEnumerable<WaveStructureNode> roots)
    {
        var max = 0;
        void Walk(IEnumerable<WaveStructureNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                max = Math.Max(max, depth);
                if (node.Children.Count > 0)
                {
                    Walk(node.Children, depth + 1);
                }
            }
        }

        Walk(roots, 0);
        return max;
    }

    internal static int NameFieldWidth(int maxDepth) =>
        (Math.Max(0, maxDepth) * TreeIndentSpaces) + 4;

    internal static int MaxSizeFieldWidth(IEnumerable<WaveStructureNode> roots)
    {
        var max = 0;
        void Walk(IEnumerable<WaveStructureNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Size > 0)
                {
                    max = Math.Max(max, FormatSize(node.Size).Length);
                }

                if (node.Children.Count > 0)
                {
                    Walk(node.Children);
                }
            }
        }

        Walk(roots);
        return Math.Max(max, FormatSize(0).Length);
    }

    internal static string FormatSize(long bytes) =>
        bytes.ToString("#,0", CultureInfo.InvariantCulture) + " B";

    /// <summary>
    /// WAVE / AIFF で一般に知られる FourCC。ここに無いものは独自チャンク。
    /// </summary>
    internal static bool IsWellKnownChunkId(string id) =>
        IsCoreChunkId(id) || IsSpecializedChunkId(id);

    /// <summary>コアな RIFF/AIFF チャンク（通常色）。</summary>
    internal static bool IsCoreChunkId(string id) =>
        id is
            "RIFF" or "RF64" or "BW64" or "RIFX" or "WAVE" or "FORM" or "AIFF" or "AIFC"
            or "fmt " or "fact" or "data" or "wavl" or "LIST" or "list" or "JUNK" or "PAD " or "FLLR" or "ds64"
            or "cue " or "smpl" or "plst" or "adtl" or "inst" or "DISP"
            or "labl" or "note" or "ltxt"
            or "INAM" or "IART" or "IPRD" or "IGNR" or "ICMT" or "ICRD" or "ITRK" or "IPRT"
            or "IMUS" or "IENG" or "ISFT" or "ICOP" or "ISBJ" or "IKEY" or "ITCH" or "IARL"
            or "ISRC" or "ICRT" or "IEDT" or "ISRF" or "INFO" or "info"
            or "COMM" or "SSND" or "NAME" or "AUTH" or "ANNO" or "(c) " or "COMT"
            or "MARK" or "INST" or "AESD" or "MIDI" or "APPL" or "FVER";

    /// <summary>専門メタ（シアン）。一般的ではないが正当なデータ。</summary>
    internal static bool IsSpecializedChunkId(string id) =>
        id is "bext" or "iXML" or "axml" or "AXML" or "_PMX" or "cart" or "CART"
            or "PEAK" or "levl" or "Chna" or "acid" or "CSET" or "ID3 " or "id3 "
            or "umid" or "minf" or "elm1" or "regn";

    /// <summary>キー／値を揃えた表。数値の値は右寄せ。</summary>
    internal static string FormatKeyValueTable(
        IReadOnlyList<(string Key, string Value)> rows,
        int minKeyWidth = 0)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var keyWidth = Math.Max(minKeyWidth, rows.Max(r => r.Key.Length));

        var sb = new StringBuilder();
        foreach (var (key, value) in rows)
        {
            var lines = SplitLines(value);
            // キー列は │ に向かって右寄せ。値列は左寄せ。
            sb.Append(key.PadLeft(keyWidth));
            sb.Append("  │ ");
            var first = lines.Length == 0 ? string.Empty : lines[0];
            sb.AppendLine(first);
            for (var i = 1; i < lines.Length; i++)
            {
                sb.Append(new string(' ', keyWidth));
                sb.Append("  │ ");
                sb.AppendLine(lines[i]);
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 選択チャンクの情報本文。Offset／Size と Content（不正時は Error も）。
    /// プレイヤー情報列・エディタ詳細と共通。
    /// </summary>
    internal static string FormatNodeDetail(WaveStructureNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var metaRows = new (string Key, string Value)[]
        {
            ("Offset", string.Create(CultureInfo.InvariantCulture, $"0x{node.Offset:X} ({node.Offset})")),
            ("Size", FormatSize(node.Size)),
        };

        var body = node.Content ?? string.Empty;
        if (node.IsInvalid
            && !string.IsNullOrWhiteSpace(node.Error)
            && !body.Contains(node.Error, StringComparison.Ordinal))
        {
            body = node.Error + Environment.NewLine + Environment.NewLine + body;
        }

        return FormatDetailWithMeta(metaRows, body);
    }

    /// <summary>メタ行と本文を結合し、段が分かれても │ 列を揃える。</summary>
    internal static string FormatDetailWithMeta(
        IReadOnlyList<(string Key, string Value)> metaRows,
        string body)
    {
        var keyWidth = Math.Max(
            metaRows.Count == 0 ? 0 : metaRows.Max(r => r.Key.Length),
            MeasureKeyValueKeyWidth(body));
        var meta = FormatKeyValueTable(metaRows, keyWidth);
        var alignedBody = AlignKeyValueTable(body, keyWidth);
        if (string.IsNullOrEmpty(meta))
        {
            return alignedBody;
        }

        if (string.IsNullOrEmpty(alignedBody))
        {
            return meta;
        }

        return meta + Environment.NewLine + Environment.NewLine + alignedBody;
    }

    internal static int MeasureKeyValueKeyWidth(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var max = 0;
        foreach (var line in SplitLines(text))
        {
            var idx = line.IndexOf('│');
            if (idx >= 2)
            {
                max = Math.Max(max, idx - 2);
            }
        }

        return max;
    }

    internal static string AlignKeyValueTable(string text, int keyWidth)
    {
        if (string.IsNullOrEmpty(text) || keyWidth <= 0)
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var line in SplitLines(text))
        {
            var idx = line.IndexOf('│');
            if (idx < 0)
            {
                sb.AppendLine(line);
                continue;
            }

            var key = line[..idx].TrimEnd();
            var value = idx + 1 < line.Length ? line[(idx + 1)..].TrimStart() : string.Empty;
            sb.Append(key.PadLeft(keyWidth));
            sb.Append("  │ ");
            sb.AppendLine(value);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>列揃えの表（ヘッダ＋本文）。数値列は右寄せ。</summary>
    internal static string FormatGrid(string[] headers, IReadOnlyList<string[]> body)
    {
        var cols = headers.Length;
        var widths = new int[cols];
        var rightAlign = new bool[cols];
        for (var c = 0; c < cols; c++)
        {
            widths[c] = headers[c].Length;
        }

        foreach (var row in body)
        {
            for (var c = 0; c < cols; c++)
            {
                var cell = c < row.Length ? row[c] ?? string.Empty : string.Empty;
                widths[c] = Math.Max(widths[c], cell.Length);
            }
        }

        for (var c = 0; c < cols; c++)
        {
            if (body.Count == 0)
            {
                continue;
            }

            var numeric = 0;
            foreach (var row in body)
            {
                var cell = c < row.Length ? row[c] ?? string.Empty : string.Empty;
                if (LooksNumericCell(cell))
                {
                    numeric++;
                }
            }

            rightAlign[c] = numeric * 2 >= body.Count;
        }

        var sb = new StringBuilder();
        void AppendRow(IReadOnlyList<string> cells, bool alignNumbers)
        {
            for (var c = 0; c < cols; c++)
            {
                if (c > 0)
                {
                    sb.Append("  ");
                }

                var cell = c < cells.Count ? cells[c] ?? string.Empty : string.Empty;
                if (alignNumbers && rightAlign[c])
                {
                    sb.Append(cell.PadLeft(widths[c]));
                }
                else
                {
                    sb.Append(c == cols - 1 ? cell : cell.PadRight(widths[c]));
                }
            }

            sb.AppendLine();
        }

        AppendRow(headers, alignNumbers: true);
        for (var c = 0; c < cols; c++)
        {
            if (c > 0)
            {
                sb.Append("  ");
            }

            sb.Append(new string('─', Math.Max(1, widths[c])));
        }

        sb.AppendLine();
        foreach (var row in body)
        {
            AppendRow(row, alignNumbers: true);
        }

        return sb.ToString().TrimEnd();
    }

    private static string[] SplitLines(string? text) =>
        (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

    /// <summary>表セルが数値（整数／小数／0x…）か。</summary>
    internal static bool LooksNumericCell(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("0X", StringComparison.Ordinal))
        {
            var hex = t[2..];
            return hex.Length > 0 && hex.All(static ch => Uri.IsHexDigit(ch));
        }

        var compact = t.Replace(",", "", StringComparison.Ordinal);
        return long.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || double.TryParse(compact, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>改行コードを記号で見える化する（行の区切り自体は残す）。</summary>
    internal static string FormatVisibleNewlines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        const string crlfToken = "\uE000";
        return text
            .Replace("\r\n", crlfToken, StringComparison.Ordinal)
            .Replace("\n", "␊\n", StringComparison.Ordinal)
            .Replace("\r", "␍\n", StringComparison.Ordinal)
            .Replace(crlfToken, "␍␊\n", StringComparison.Ordinal);
    }
}

internal sealed class WaveStructureDocument
{
    public const string ChunkUnionKind = "UNION";

    public WaveStructureDocument(
        string containerKind,
        IReadOnlyList<WaveStructureNode> roots,
        IReadOnlyList<string> errors,
        string? unsupportedMessage = null)
    {
        ContainerKind = containerKind;
        Roots = roots;
        Errors = errors;
        UnsupportedMessage = unsupportedMessage;
    }

    public string ContainerKind { get; }
    public IReadOnlyList<WaveStructureNode> Roots { get; }
    public IReadOnlyList<string> Errors { get; }
    public string? UnsupportedMessage { get; }
    public bool HasErrors => Errors.Count > 0;
    public bool IsUnsupported => UnsupportedMessage is not null;
    /// <summary>複数ファイル選択時のチャンク FourCC 和集合（フラット一覧）。</summary>
    public bool IsChunkUnion => ContainerKind == ChunkUnionKind;
}

/// <summary>
/// RIFF / FORM を走査しチャンクツリーを作る。PCM は展開しない。
/// 走査時は ID／Offset／Size（と LIST 階層）のみ。本文は <see cref="EnsureContent"/>。
/// </summary>
internal static class WaveContainerStructure
{
    /// <summary>data / SSND / wavl のプレビュー長（1 ページ分）。続きは読まない。</summary>
    public const int DataPreviewPageBytes = 1536;

    /// <summary>サンプル以外のバイナリ全文表示の上限（異常に巨大な JUNK 等の保険）。</summary>
    public const int BinaryFullPreviewMaxBytes = 4 * 1024 * 1024;

    public static WaveStructureDocument TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new WaveStructureDocument(
                string.Empty,
                Array.Empty<WaveStructureNode>(),
                [UiStrings.WaveStructureFileMissing]);
        }

        try
        {
            var kind = AudioCodec.DetectKind(path);
            using var stream = File.OpenRead(path);
            return kind switch
            {
                AudioFileKind.Aiff => ReadAiff(stream),
                AudioFileKind.Wave => ReadRiffWave(stream),
                _ => Unsupported(kind),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new WaveStructureDocument(
                string.Empty,
                Array.Empty<WaveStructureNode>(),
                [UiStrings.WaveStructureReadFailed(ex.Message)]);
        }
    }

    /// <summary>
    /// チャンク選択時に本文を読む。既に読込済みならそのまま返す。
    /// LIST は Label／子数から要約のみ（子ペイロードは読まない）。
    /// </summary>
    public static WaveStructureNode EnsureContent(string path, WaveStructureNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.ContentPending)
        {
            return node;
        }

        if (node.Id is "LIST" or "list")
        {
            return LoadListSummaryContent(node);
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return node.WithLoadedContent(
                WaveStructureContentKind.Text,
                UiStrings.WaveStructureFileMissing,
                contentTruncated: false,
                isInvalid: true,
                error: UiStrings.WaveStructureFileMissing);
        }

        try
        {
            var fileKind = AudioCodec.DetectKind(path);
            if (fileKind is not (AudioFileKind.Wave or AudioFileKind.Aiff))
            {
                return node.WithLoadedContent(
                    WaveStructureContentKind.Text,
                    UiStrings.WaveStructureUnsupported(fileKind.ToString().ToUpperInvariant()),
                    contentTruncated: false,
                    isInvalid: true,
                    error: UiStrings.WaveStructureUnsupported(fileKind.ToString().ToUpperInvariant()));
            }

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            var dataStart = node.Offset + 8;
            if (node.Offset < 0 || dataStart > stream.Length)
            {
                var err = UiStrings.WaveStructureChunkTruncated(node.Id, node.Size, 0);
                return node.WithLoadedContent(
                    WaveStructureContentKind.Text,
                    err,
                    contentTruncated: false,
                    isInvalid: true,
                    error: err);
            }

            var size = (uint)Math.Min(node.Size, uint.MaxValue);
            var loaded = fileKind == AudioFileKind.Aiff
                ? ReadAiffChunk(reader, node.Id, node.Offset, dataStart, size)
                : ReadWaveChunk(reader, node.Id, node.Offset, dataStart, size);
            var kind = loaded.Kind;
            var content = loaded.Content;
            // INFO 子は走査時に Label へフィールド名を付けている。本文も従来の表形式に揃える。
            if (!loaded.IsInvalid
                && !string.Equals(node.Label, node.Id, StringComparison.Ordinal)
                && node.Label.StartsWith(node.Id, StringComparison.Ordinal))
            {
                var text = string.IsNullOrWhiteSpace(loaded.Content)
                    ? UiStrings.WaveStructureEmptyPayload
                    : loaded.Content;
                content = WaveStructureNode.FormatKeyValueTable(
                [
                    ("Field", InfoFieldName(node.Id)),
                    ("FourCC", WaveStructureNode.FormatFourCc(node.Id)),
                    ("Text", text),
                ]);
                kind = WaveStructureContentKind.Text;
            }

            return node.WithLoadedContent(
                kind,
                content,
                loaded.ContentTruncated,
                loaded.IsInvalid,
                loaded.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var err = UiStrings.WaveStructureReadFailed(ex.Message);
            return node.WithLoadedContent(
                WaveStructureContentKind.Text,
                err,
                contentTruncated: false,
                isInvalid: true,
                error: err);
        }
    }

    private static WaveStructureNode LoadListSummaryContent(WaveStructureNode node)
    {
        var listType = "????";
        var sep = node.Label.IndexOf(" / ", StringComparison.Ordinal);
        if (sep >= 0 && sep + 3 < node.Label.Length)
        {
            listType = node.Label[(sep + 3)..];
        }

        var content = WaveStructureNode.FormatKeyValueTable(
        [
            ("List type", listType),
            ("Children", node.Children.Count.ToString(CultureInfo.InvariantCulture)),
            ("Summary", UiStrings.WaveStructureListSummary(listType, node.Children.Count)),
        ]);
        return node.WithLoadedContent(
            WaveStructureContentKind.Text,
            content,
            contentTruncated: false,
            isInvalid: false,
            error: null);
    }

    private static WaveStructureNode PendingChunk(
        string id,
        string label,
        long offset,
        long size,
        IReadOnlyList<WaveStructureNode>? children = null)
    {
        // NUL×4 等はチャンク名として無効。説明文は出さず、(empty)＋不正色のみ。
        if (WaveStructureNode.IsBlankFourCc(id))
        {
            return new WaveStructureNode(
                id,
                UiStrings.WaveStructureEmptyPayload,
                offset,
                size,
                WaveStructureContentKind.Text,
                UiStrings.WaveStructureEmptyPayload,
                contentTruncated: false,
                isInvalid: true,
                error: null,
                children,
                contentPending: false);
        }

        return new WaveStructureNode(
            id,
            label,
            offset,
            size,
            GuessContentKind(id),
            content: string.Empty,
            contentTruncated: false,
            isInvalid: false,
            error: null,
            children,
            contentPending: true);
    }

    private static WaveStructureContentKind GuessContentKind(string id) =>
        id is "iXML" or "axml" or "AXML" or "_PMX"
            ? WaveStructureContentKind.Xml
            : id is "data" or "wavl" or "SSND"
                ? WaveStructureContentKind.Binary
                : WaveStructureContentKind.Text;

    /// <summary>
    /// 複数ファイルのチャンク FourCC 和集合をフラット一覧にする。
    /// 非対応形式は飛ばし、親子構造は持たない。
    /// 各ノードの Size は「そのチャンクを持つファイル数」。
    /// 修復が必要な WAVE（切り詰め・空 FourCC など）は先頭の <c>****</c> 行にも載せる。
    /// </summary>
    public static WaveStructureDocument BuildChunkUnion(IEnumerable<string> paths)
    {
        var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            // プレイリスト選択との照合用にフルパスへ正規化（共有ドライブ等）。
            var memberPath = PathUtil.TryGetFullPathTrimmed(path, out var full) ? full : path;
            var doc = TryRead(path);
            if (doc.IsUnsupported)
            {
                continue;
            }

            var fileIds = new HashSet<string>(StringComparer.Ordinal);
            if (doc.Roots.Count > 0)
            {
                CollectChunkIds(doc.Roots, fileIds);
            }

            // 切り詰め data などは FourCC が普通のままなので、修復対象を **** に集約する。
            if (WaveHeaderRepair.NeedsRepair(doc))
            {
                fileIds.Add(WaveStructureNode.BlankFourCcCanonicalId);
            }

            if (fileIds.Count == 0)
            {
                continue;
            }

            foreach (var id in fileIds)
            {
                if (!members.TryGetValue(id, out var list))
                {
                    list = [];
                    members[id] = list;
                }

                list.Add(memberPath);
            }
        }

        var roots = new List<WaveStructureNode>(members.Count);
        // ****（修復／空 FourCC）を常に先頭。ほかは FourCC 昇順。
        foreach (var id in members.Keys
                     .OrderBy(static x => WaveStructureNode.IsBlankFourCc(x) ? 0 : 1)
                     .ThenBy(static x => x, StringComparer.Ordinal))
        {
            var list = members[id];
            var blank = WaveStructureNode.IsBlankFourCc(id);
            roots.Add(new WaveStructureNode(
                id,
                blank ? WaveStructureNode.BlankFourCcListLabel : id,
                offset: 0,
                size: list.Count,
                WaveStructureContentKind.Empty,
                content: string.Empty,
                contentTruncated: false,
                isInvalid: blank,
                error: null,
                memberPaths: list));
        }

        return new WaveStructureDocument(
            WaveStructureDocument.ChunkUnionKind,
            roots,
            Array.Empty<string>());
    }

    private static void CollectChunkIds(
        IEnumerable<WaveStructureNode> nodes,
        ISet<string> ids)
    {
        foreach (var node in nodes)
        {
            // 空／不正 FourCC（NUL・空白のみ等）は表示が同じなので1キーに畳む。
            if (WaveStructureNode.IsBlankFourCc(node.Id))
            {
                ids.Add(WaveStructureNode.BlankFourCcCanonicalId);
            }
            else if (!string.IsNullOrEmpty(node.Id))
            {
                ids.Add(node.Id);
            }

            if (node.Children.Count > 0)
            {
                CollectChunkIds(node.Children, ids);
            }
        }
    }

    private static WaveStructureDocument Unsupported(AudioFileKind kind) =>
        new(
            string.Empty,
            Array.Empty<WaveStructureNode>(),
            Array.Empty<string>(),
            UiStrings.WaveStructureUnsupported(kind.ToString().ToUpperInvariant()));

    private static WaveStructureDocument ReadRiffWave(Stream stream)
    {
        var errors = new List<string>();
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (stream.Length < 12)
        {
            errors.Add(UiStrings.WaveStructureTooShort);
            return new WaveStructureDocument("WAVE", Array.Empty<WaveStructureNode>(), errors);
        }

        var container = ReadFourCc(reader);
        if (container is not ("RIFF" or "RF64" or "BW64"))
        {
            errors.Add(UiStrings.WaveStructureBadContainer(container));
            return new WaveStructureDocument("WAVE", Array.Empty<WaveStructureNode>(), errors);
        }

        var declaredSize = reader.ReadUInt32();
        var form = ReadFourCc(reader);
        if (form != "WAVE")
        {
            errors.Add(UiStrings.WaveStructureBadForm(form));
            return new WaveStructureDocument(form, Array.Empty<WaveStructureNode>(), errors);
        }

        var roots = new List<WaveStructureNode>
        {
            new(
                container,
                $"{container} / {form}",
                0,
                Math.Min(stream.Length, 8L + declaredSize),
                WaveStructureContentKind.Text,
                BuildContainerSummary(container, form, declaredSize, stream.Length),
                contentTruncated: false,
                isInvalid: false,
                error: null),
        };

        var expectedEnd = 8L + declaredSize;
        if (declaredSize > 0 && expectedEnd != stream.Length && container == "RIFF")
        {
            // RF64/BW64 は ds64 で実サイズを持つことが多いので RIFF だけ警告。
            if (expectedEnd > stream.Length)
            {
                errors.Add(UiStrings.WaveStructureSizeExceedsFile(declaredSize, stream.Length));
            }
            else if (stream.Length - expectedEnd > 1)
            {
                errors.Add(UiStrings.WaveStructureTrailingBytes(stream.Length - expectedEnd));
            }
        }

        var chunks = new List<WaveStructureNode>();
        var end = stream.Length;
        while (stream.Position + 8 <= end)
        {
            var chunkOffset = stream.Position;
            var id = ReadFourCc(reader);
            var size = reader.ReadUInt32();
            var dataStart = stream.Position;
            var remaining = end - dataStart;
            var truncated = size > remaining;
            if (truncated)
            {
                errors.Add(UiStrings.WaveStructureChunkTruncated(id, size, remaining));
                chunks.Add(new WaveStructureNode(
                    id,
                    id,
                    chunkOffset,
                    size,
                    WaveStructureContentKind.Text,
                    UiStrings.WaveStructureChunkTruncated(id, size, remaining),
                    contentTruncated: false,
                    isInvalid: true,
                    error: UiStrings.WaveStructureChunkTruncated(id, size, remaining)));
                break;
            }

            chunks.Add(
                id is "LIST" or "list"
                    ? ScanListChunk(reader, id, chunkOffset, dataStart, size, littleEndian: true, errors)
                    : PendingChunk(id, id, chunkOffset, size));
            var next = dataStart + size + (size & 1);
            if (next < stream.Position || next > end)
            {
                errors.Add(UiStrings.WaveStructureBadPadding(id));
                break;
            }

            stream.Position = next;
        }

        if (chunks.Count > 0)
        {
            roots[0] = new WaveStructureNode(
                roots[0].Id,
                roots[0].Label,
                roots[0].Offset,
                roots[0].Size,
                roots[0].Kind,
                roots[0].Content,
                roots[0].ContentTruncated,
                roots[0].IsInvalid,
                roots[0].Error,
                chunks);
        }

        return new WaveStructureDocument("WAVE", roots, errors);
    }

    private static WaveStructureNode ReadWaveChunk(
        BinaryReader reader,
        string id,
        long chunkOffset,
        long dataStart,
        uint size)
    {
        var stream = reader.BaseStream;
        stream.Position = dataStart;

        // LIST 本文は EnsureContent → LoadListSummaryContent（ここへは来ない）。
        if (id == "fmt ")
        {
            return DecodeFmt(reader, chunkOffset, size);
        }

        if (id == "fact")
        {
            return DecodeFact(reader, chunkOffset, size);
        }

        if (id == "bext")
        {
            return DecodeBext(reader, chunkOffset, size);
        }

        if (id is "data" or "wavl")
        {
            return DecodeBinaryPreview(reader, id, chunkOffset, size, label: id);
        }

        if (id is "iXML" or "axml" or "AXML" or "_PMX")
        {
            return DecodeXmlChunk(reader, id, chunkOffset, size);
        }

        if (id is "cue ")
        {
            return DecodeCue(reader, chunkOffset, size);
        }

        if (id == "smpl")
        {
            return DecodeSmpl(reader, chunkOffset, size);
        }

        if (id is "labl" or "note" or "ltxt")
        {
            return DecodeAdtlText(reader, id, chunkOffset, size);
        }

        if (IsLikelyTextChunk(id) && size <= 64 * 1024)
        {
            var text = ReadCString(reader, (int)size);
            var shown = string.IsNullOrWhiteSpace(text)
                ? UiStrings.WaveStructureEmptyPayload
                : WaveStructureNode.FormatVisibleNewlines(text);
            return new WaveStructureNode(
                id,
                id,
                chunkOffset,
                size,
                WaveStructureContentKind.Text,
                shown,
                contentTruncated: false,
                isInvalid: false,
                error: null);
        }

        // 未知チャンクは全文（巨大すぎる場合のみ上限で切る。続きメッセージは data 系のみ）。
        return DecodeBinaryPreview(reader, id, chunkOffset, size, label: id);
    }

    /// <summary>LIST 階層だけ辿る。子ペイロードは読まず Pending。</summary>
    private static WaveStructureNode ScanListChunk(
        BinaryReader reader,
        string id,
        long chunkOffset,
        long dataStart,
        uint size,
        bool littleEndian,
        List<string> errors)
    {
        var stream = reader.BaseStream;
        if (size < 4)
        {
            errors.Add(UiStrings.WaveStructureChunkTruncated(id, size, size));
            return new WaveStructureNode(
                id,
                id,
                chunkOffset,
                size,
                WaveStructureContentKind.Text,
                UiStrings.WaveStructureEmptyPayload,
                contentTruncated: false,
                isInvalid: true,
                error: UiStrings.WaveStructureListTooSmall);
        }

        var listType = ReadFourCc(reader);
        var children = new List<WaveStructureNode>();
        var end = dataStart + size;
        while (stream.Position + 8 <= end)
        {
            var childOffset = stream.Position;
            var childId = ReadFourCc(reader);
            var childSize = littleEndian
                ? reader.ReadUInt32()
                : (uint)BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            var childData = stream.Position;
            if (childData + childSize > end)
            {
                errors.Add(UiStrings.WaveStructureChunkTruncated(childId, childSize, end - childData));
                children.Add(new WaveStructureNode(
                    childId,
                    childId,
                    childOffset,
                    childSize,
                    WaveStructureContentKind.Text,
                    UiStrings.WaveStructureChunkTruncated(childId, childSize, end - childData),
                    contentTruncated: false,
                    isInvalid: true,
                    error: UiStrings.WaveStructureChunkTruncated(childId, childSize, end - childData)));
                break;
            }

            if (childId is "LIST" or "list")
            {
                children.Add(ScanListChunk(
                    reader,
                    childId,
                    childOffset,
                    childData,
                    childSize,
                    littleEndian,
                    errors));
            }
            else
            {
                var label = listType is "INFO" or "info"
                    ? $"{childId}  ({InfoFieldName(childId)})"
                    : childId;
                children.Add(PendingChunk(childId, label, childOffset, childSize));
            }

            stream.Position = childData + childSize + (childSize & 1);
        }

        stream.Position = end;
        return PendingChunk(id, $"{id} / {listType}", chunkOffset, size, children);
    }

    private static WaveStructureDocument ReadAiff(Stream stream)
    {
        var errors = new List<string>();
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (stream.Length < 12)
        {
            errors.Add(UiStrings.WaveStructureTooShort);
            return new WaveStructureDocument("AIFF", Array.Empty<WaveStructureNode>(), errors);
        }

        var container = ReadFourCc(reader);
        if (container != "FORM")
        {
            errors.Add(UiStrings.WaveStructureBadContainer(container));
            return new WaveStructureDocument("AIFF", Array.Empty<WaveStructureNode>(), errors);
        }

        var formSize = BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
        var form = ReadFourCc(reader);
        if (form is not ("AIFF" or "AIFC"))
        {
            errors.Add(UiStrings.WaveStructureBadForm(form));
            return new WaveStructureDocument(form, Array.Empty<WaveStructureNode>(), errors);
        }

        var formEnd = Math.Min(stream.Length, 8L + formSize);
        if (8L + formSize > stream.Length)
        {
            errors.Add(UiStrings.WaveStructureSizeExceedsFile(formSize, stream.Length));
        }

        var chunks = new List<WaveStructureNode>();
        while (stream.Position + 8 <= formEnd)
        {
            var chunkOffset = stream.Position;
            var id = ReadFourCc(reader);
            var size = (uint)BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            var dataStart = stream.Position;
            var remaining = formEnd - dataStart;
            if (size > remaining)
            {
                errors.Add(UiStrings.WaveStructureChunkTruncated(id, size, remaining));
                chunks.Add(new WaveStructureNode(
                    id,
                    id,
                    chunkOffset,
                    size,
                    WaveStructureContentKind.Text,
                    UiStrings.WaveStructureChunkTruncated(id, size, remaining),
                    contentTruncated: false,
                    isInvalid: true,
                    error: UiStrings.WaveStructureChunkTruncated(id, size, remaining)));
                break;
            }

            chunks.Add(
                id is "LIST" or "list"
                    ? ScanListChunk(reader, id, chunkOffset, dataStart, size, littleEndian: false, errors)
                    : PendingChunk(id, id, chunkOffset, size));
            stream.Position = dataStart + size + (size & 1);
        }

        var root = new WaveStructureNode(
            container,
            $"{container} / {form}",
            0,
            Math.Min(stream.Length, 8L + formSize),
            WaveStructureContentKind.Text,
            BuildContainerSummary(container, form, (uint)Math.Max(0, formSize), stream.Length),
            contentTruncated: false,
            isInvalid: false,
            error: null,
            chunks);
        return new WaveStructureDocument(form, [root], errors);
    }

    private static WaveStructureNode ReadAiffChunk(
        BinaryReader reader,
        string id,
        long chunkOffset,
        long dataStart,
        uint size)
    {
        reader.BaseStream.Position = dataStart;
        if (id == "COMM" && size >= 18)
        {
            var channels = BinaryPrimitives.ReadInt16BigEndian(reader.ReadBytes(2));
            var frames = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            var bits = BinaryPrimitives.ReadInt16BigEndian(reader.ReadBytes(2));
            var rate = ReadIeee80(reader.ReadBytes(10));
            var rows = new List<(string, string)>
            {
                ("Channels", channels.ToString(CultureInfo.InvariantCulture)),
                ("Sample frames", frames.ToString(CultureInfo.InvariantCulture)),
                ("Bits/sample", bits.ToString(CultureInfo.InvariantCulture)),
                ("Sample rate", rate.ToString(CultureInfo.InvariantCulture)),
            };
            if (size > 18)
            {
                rows.Add(("Compression", ReadFourCc(reader)));
            }

            return TextNode(id, chunkOffset, size, WaveStructureNode.FormatKeyValueTable(rows));
        }

        if (id == "MARK" && size >= 2)
        {
            return DecodeAiffMark(reader, chunkOffset, size);
        }

        if (id == "CHAN" && size >= 12)
        {
            return DecodeAiffChan(reader, chunkOffset, size);
        }

        if (id is "SSND" or "APPL")
        {
            return DecodeBinaryPreview(reader, id, chunkOffset, size, label: id);
        }

        if (id is "NAME" or "AUTH" or "ANNO" or "(c) ")
        {
            var text = ReadCString(reader, (int)size);
            return TextNode(id, chunkOffset, size, string.IsNullOrWhiteSpace(text)
                ? UiStrings.WaveStructureEmptyPayload
                : WaveStructureNode.FormatVisibleNewlines(text));
        }

        if (IsLikelyTextChunk(id) && size <= 64 * 1024)
        {
            var text = ReadCString(reader, (int)size);
            return TextNode(id, chunkOffset, size, string.IsNullOrWhiteSpace(text)
                ? UiStrings.WaveStructureEmptyPayload
                : WaveStructureNode.FormatVisibleNewlines(text));
        }

        return DecodeBinaryPreview(reader, id, chunkOffset, size, label: id);
    }

    private static WaveStructureNode DecodeFmt(BinaryReader reader, long chunkOffset, uint size)
    {
        if (size < 16)
        {
            return InvalidText(id: "fmt ", chunkOffset, size, UiStrings.WaveStructureFmtTooSmall);
        }

        var formatTag = reader.ReadUInt16();
        var channels = reader.ReadUInt16();
        var sampleRate = reader.ReadInt32();
        var byteRate = reader.ReadInt32();
        var blockAlign = reader.ReadUInt16();
        var bits = reader.ReadUInt16();
        var rows = new List<(string, string)>
        {
            ("Format tag", $"{formatTag} ({FormatTagName(formatTag)})"),
            ("Channels", channels.ToString(CultureInfo.InvariantCulture)),
            ("Sample rate", sampleRate.ToString(CultureInfo.InvariantCulture)),
            ("Byte rate", byteRate.ToString(CultureInfo.InvariantCulture)),
            ("Block align", blockAlign.ToString(CultureInfo.InvariantCulture)),
            ("Bits/sample", bits.ToString(CultureInfo.InvariantCulture)),
        };
        if (size >= 18)
        {
            var cbSize = reader.ReadUInt16();
            rows.Add(("cbSize", cbSize.ToString(CultureInfo.InvariantCulture)));
            if (formatTag == 0xFFFE && size >= 40)
            {
                var validBits = reader.ReadUInt16();
                var channelMask = reader.ReadUInt32();
                var guid = reader.ReadBytes(16);
                rows.Add(("Valid bits", validBits.ToString(CultureInfo.InvariantCulture)));
                rows.Add(("Channel mask", ChannelMaskBits.FormatMaskDisplay(unchecked((int)channelMask))));
                rows.Add(("SubFormat", new Guid(guid).ToString()));
            }
        }

        return TextNode("fmt ", chunkOffset, size, WaveStructureNode.FormatKeyValueTable(rows));
    }

    private static WaveStructureNode DecodeAiffChan(BinaryReader reader, long chunkOffset, uint size)
    {
        var tag = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        var bitmap = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        var descriptions = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
        var rows = new List<(string, string)>
        {
            ("Layout tag", tag == ChannelMaskBits.UseChannelBitmapTag
                ? $"0x{tag:X8} (UseChannelBitmap)"
                : $"0x{tag:X8}"),
            ("Channel bitmap", ChannelMaskBits.FormatMaskDisplay(unchecked((int)bitmap))),
            ("Descriptions", descriptions.ToString(CultureInfo.InvariantCulture)),
        };
        return TextNode("CHAN", chunkOffset, size, WaveStructureNode.FormatKeyValueTable(rows));
    }

    private static WaveStructureNode DecodeFact(BinaryReader reader, long chunkOffset, uint size)
    {
        if (size < 4)
        {
            return InvalidText("fact", chunkOffset, size, UiStrings.WaveStructureEmptyPayload);
        }

        var samples = reader.ReadUInt32();
        return TextNode(
            "fact",
            chunkOffset,
            size,
            WaveStructureNode.FormatKeyValueTable(
            [
                ("Sample length", samples.ToString(CultureInfo.InvariantCulture)),
            ]));
    }

    private static WaveStructureNode DecodeBext(BinaryReader reader, long chunkOffset, uint size)
    {
        if (size < 602)
        {
            return DecodeBinaryPreview(reader, "bext", chunkOffset, size, label: "bext");
        }

        var description = ReadFixed(reader, 256);
        var originator = ReadFixed(reader, 32);
        var originatorReference = ReadFixed(reader, 32);
        var originationDate = ReadFixed(reader, 10);
        var originationTime = ReadFixed(reader, 8);
        var timeReferenceLow = reader.ReadUInt32();
        var timeReferenceHigh = reader.ReadUInt32();
        var version = reader.ReadUInt16();
        var umid = reader.ReadBytes(64);
        // 412: Reserved(190) / v2 は Loudness(10)+Reserved(180)。CodingHistory は 602 から。
        short loudnessValue = 0;
        short loudnessRange = 0;
        short maxTruePeak = 0;
        short maxMomentary = 0;
        short maxShortTerm = 0;
        if (version >= 2 && size >= 422)
        {
            loudnessValue = reader.ReadInt16();
            loudnessRange = reader.ReadInt16();
            maxTruePeak = reader.ReadInt16();
            maxMomentary = reader.ReadInt16();
            maxShortTerm = reader.ReadInt16();
        }

        const int historyOffset = 602;
        var history = string.Empty;
        if (size > historyOffset)
        {
            reader.BaseStream.Position = chunkOffset + 8 + historyOffset;
            history = ReadCString(reader, (int)(size - historyOffset));
        }

        var rows = new List<(string, string)>
        {
            ("Description", WaveStructureNode.FormatVisibleNewlines(description)),
            ("Originator", originator),
            ("OriginatorReference", originatorReference),
            ("OriginationDate", originationDate),
            ("OriginationTime", originationTime),
            ("TimeReference", (((ulong)timeReferenceHigh << 32) | timeReferenceLow)
                .ToString(CultureInfo.InvariantCulture)),
            ("Version", version.ToString(CultureInfo.InvariantCulture)),
            ("UMID", Convert.ToHexString(umid.AsSpan(0, Math.Min(32, umid.Length))) + "…"),
        };
        if (version >= 2)
        {
            rows.Add(("LoudnessValue", $"{loudnessValue / 100.0:0.00} LUFS"));
            rows.Add(("LoudnessRange", $"{loudnessRange / 100.0:0.00} LU"));
            rows.Add(("MaxTruePeakLevel", $"{maxTruePeak / 100.0:0.00} dBTP"));
            rows.Add(("MaxMomentaryLoudness", $"{maxMomentary / 100.0:0.00} LUFS"));
            rows.Add(("MaxShortTermLoudness", $"{maxShortTerm / 100.0:0.00} LUFS"));
        }

        if (!string.IsNullOrWhiteSpace(history))
        {
            rows.Add(("CodingHistory", WaveStructureNode.FormatVisibleNewlines(history)));
        }

        return TextNode("bext", chunkOffset, size, WaveStructureNode.FormatKeyValueTable(rows));
    }

    private static WaveStructureNode DecodeCue(BinaryReader reader, long chunkOffset, uint size)
    {
        if (size < 4)
        {
            return InvalidText("cue ", chunkOffset, size, UiStrings.WaveStructureEmptyPayload);
        }

        var count = reader.ReadUInt32();
        var available = (size - 4) / 24;
        var n = (int)Math.Min(count, available);
        var rows = new List<string[]>(n);
        for (var i = 0; i < n; i++)
        {
            var id = reader.ReadUInt32();
            var position = reader.ReadUInt32();
            var fccChunk = ReadFourCc(reader);
            var chunkStart = reader.ReadUInt32();
            var blockStart = reader.ReadUInt32();
            var sampleOffset = reader.ReadUInt32();
            rows.Add(
            [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                id.ToString(CultureInfo.InvariantCulture),
                position.ToString(CultureInfo.InvariantCulture),
                fccChunk,
                chunkStart.ToString(CultureInfo.InvariantCulture),
                blockStart.ToString(CultureInfo.InvariantCulture),
                sampleOffset.ToString(CultureInfo.InvariantCulture),
            ]);
        }

        var header = WaveStructureNode.FormatKeyValueTable(
        [
            ("Cue points", count.ToString(CultureInfo.InvariantCulture)),
            ("Shown", n.ToString(CultureInfo.InvariantCulture)),
        ]);
        if (rows.Count == 0)
        {
            return TextNode("cue ", chunkOffset, size, header);
        }

        var grid = WaveStructureNode.FormatGrid(
            ["#", "CueId", "Position", "Chunk", "ChunkStart", "Block", "Sample"],
            rows);
        return TextNode("cue ", chunkOffset, size, header + Environment.NewLine + Environment.NewLine + grid);
    }

    private static WaveStructureNode DecodeSmpl(BinaryReader reader, long chunkOffset, uint size)
    {
        if (size < 36)
        {
            return DecodeBinaryPreview(reader, "smpl", chunkOffset, size, label: "smpl");
        }

        var manufacturer = reader.ReadUInt32();
        var product = reader.ReadUInt32();
        var samplePeriod = reader.ReadUInt32();
        var midiUnity = reader.ReadUInt32();
        var midiPitch = reader.ReadUInt32();
        var smpteFormat = reader.ReadUInt32();
        var smpteOffset = reader.ReadUInt32();
        var sampleLoops = reader.ReadUInt32();
        var samplerData = reader.ReadUInt32();
        var header = WaveStructureNode.FormatKeyValueTable(
        [
            ("Manufacturer", manufacturer.ToString(CultureInfo.InvariantCulture)),
            ("Product", product.ToString(CultureInfo.InvariantCulture)),
            ("Sample period", samplePeriod.ToString(CultureInfo.InvariantCulture)),
            ("MIDI unity note", midiUnity.ToString(CultureInfo.InvariantCulture)),
            ("MIDI pitch fraction", midiPitch.ToString(CultureInfo.InvariantCulture)),
            ("SMPTE format", smpteFormat.ToString(CultureInfo.InvariantCulture)),
            ("SMPTE offset", smpteOffset.ToString(CultureInfo.InvariantCulture)),
            ("Sample loops", sampleLoops.ToString(CultureInfo.InvariantCulture)),
            ("Sampler data", samplerData.ToString(CultureInfo.InvariantCulture)),
        ]);
        var loops = (int)Math.Min(sampleLoops, (size - 36) / 24);
        if (loops <= 0)
        {
            return TextNode("smpl", chunkOffset, size, header);
        }

        var rows = new List<string[]>(loops);
        for (var i = 0; i < loops; i++)
        {
            var cueId = reader.ReadUInt32();
            var type = reader.ReadUInt32();
            var start = reader.ReadUInt32();
            var end = reader.ReadUInt32();
            var fraction = reader.ReadUInt32();
            var playCount = reader.ReadUInt32();
            rows.Add(
            [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                cueId.ToString(CultureInfo.InvariantCulture),
                type.ToString(CultureInfo.InvariantCulture),
                start.ToString(CultureInfo.InvariantCulture),
                end.ToString(CultureInfo.InvariantCulture),
                fraction.ToString(CultureInfo.InvariantCulture),
                playCount.ToString(CultureInfo.InvariantCulture),
            ]);
        }

        var grid = WaveStructureNode.FormatGrid(
            ["#", "CueId", "Type", "Start", "End", "Fraction", "Play"],
            rows);
        return TextNode("smpl", chunkOffset, size, header + Environment.NewLine + Environment.NewLine + grid);
    }

    private static WaveStructureNode DecodeAdtlText(
        BinaryReader reader,
        string id,
        long chunkOffset,
        uint size)
    {
        if (size < 4)
        {
            return InvalidText(id, chunkOffset, size, UiStrings.WaveStructureEmptyPayload);
        }

        var cueId = reader.ReadUInt32();
        if (id == "ltxt")
        {
            if (size < 20)
            {
                return InvalidText(id, chunkOffset, size, UiStrings.WaveStructureEmptyPayload);
            }

            var sampleLength = reader.ReadUInt32();
            var purpose = ReadFourCc(reader);
            var country = reader.ReadUInt16();
            var language = reader.ReadUInt16();
            var dialect = reader.ReadUInt16();
            var codePage = reader.ReadUInt16();
            var text = size > 20
                ? ReadCString(reader, (int)(size - 20))
                : string.Empty;
            var shown = string.IsNullOrEmpty(text)
                ? UiStrings.WaveStructureEmptyPayload
                : WaveStructureNode.FormatVisibleNewlines(text);
            return TextNode(
                id,
                chunkOffset,
                size,
                WaveStructureNode.FormatKeyValueTable(
                [
                    ("Marker #", cueId.ToString(CultureInfo.InvariantCulture)),
                    ("Cue ID", cueId.ToString(CultureInfo.InvariantCulture)),
                    ("Sample length", sampleLength.ToString(CultureInfo.InvariantCulture)),
                    ("Purpose", purpose),
                    ("Country", country.ToString(CultureInfo.InvariantCulture)),
                    ("Language", language.ToString(CultureInfo.InvariantCulture)),
                    ("Dialect", dialect.ToString(CultureInfo.InvariantCulture)),
                    ("Code page", codePage.ToString(CultureInfo.InvariantCulture)),
                    ("Text", shown),
                ]));
        }

        var label = size > 4
            ? ReadCString(reader, (int)(size - 4))
            : string.Empty;
        var labelShown = string.IsNullOrEmpty(label)
            ? UiStrings.WaveStructureEmptyPayload
            : WaveStructureNode.FormatVisibleNewlines(label);
        var kind = id == "note" ? "Note" : "Label";
        return TextNode(
            id,
            chunkOffset,
            size,
            WaveStructureNode.FormatKeyValueTable(
            [
                ("Marker #", cueId.ToString(CultureInfo.InvariantCulture)),
                ("Cue ID", cueId.ToString(CultureInfo.InvariantCulture)),
                (kind, labelShown),
            ]));
    }

    private static WaveStructureNode DecodeAiffMark(BinaryReader reader, long chunkOffset, uint size)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
        var rows = new List<string[]>();
        var end = reader.BaseStream.Position + (size - 2);
        for (var i = 0; i < count && reader.BaseStream.Position + 6 <= end; i++)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2));
            var position = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            var name = ReadPString(reader, end);
            var shown = string.IsNullOrEmpty(name)
                ? UiStrings.WaveStructureEmptyPayload
                : WaveStructureNode.FormatVisibleNewlines(name);
            rows.Add(
            [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                id.ToString(CultureInfo.InvariantCulture),
                position.ToString(CultureInfo.InvariantCulture),
                shown.Replace("\r\n", " / ", StringComparison.Ordinal)
                    .Replace("\n", " / ", StringComparison.Ordinal),
            ]);
        }

        var header = WaveStructureNode.FormatKeyValueTable(
        [
            ("Markers", count.ToString(CultureInfo.InvariantCulture)),
            ("Shown", rows.Count.ToString(CultureInfo.InvariantCulture)),
        ]);
        if (rows.Count == 0)
        {
            return TextNode("MARK", chunkOffset, size, header);
        }

        // 表セルは1行に畳む。詳細の改行記号は Marker 名の全文表示用に下へも出す。
        var grid = WaveStructureNode.FormatGrid(
            ["#", "MarkerId", "Position", "Name"],
            rows);
        return TextNode(
            "MARK",
            chunkOffset,
            size,
            header + Environment.NewLine + Environment.NewLine + grid);
    }

    private static string ReadPString(BinaryReader reader, long endExclusive)
    {
        if (reader.BaseStream.Position >= endExclusive)
        {
            return string.Empty;
        }

        var len = reader.ReadByte();
        var remain = (int)Math.Min(len, endExclusive - reader.BaseStream.Position);
        var bytes = remain > 0 ? reader.ReadBytes(remain) : [];
        // Pascal string は偶数境界へパディングされることが多い。
        if (((1 + remain) & 1) != 0 && reader.BaseStream.Position < endExclusive)
        {
            reader.ReadByte();
        }

        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static WaveStructureNode DecodeXmlChunk(
        BinaryReader reader,
        string id,
        long chunkOffset,
        uint size)
    {
        var raw = ReadCString(reader, (int)Math.Min(size, int.MaxValue));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return TextNode(id, chunkOffset, size, UiStrings.WaveStructureEmptyPayload);
        }

        try
        {
            var doc = XDocument.Parse(raw, LoadOptions.None);
            return new WaveStructureNode(
                id,
                id,
                chunkOffset,
                size,
                WaveStructureContentKind.Xml,
                FormatXml(doc),
                contentTruncated: false,
                isInvalid: false,
                error: null);
        }
        catch (Exception)
        {
            return new WaveStructureNode(
                id,
                id,
                chunkOffset,
                size,
                WaveStructureContentKind.Xml,
                raw,
                contentTruncated: false,
                isInvalid: true,
                error: UiStrings.WaveStructureXmlInvalid);
        }
    }

    /// <summary>表示用 XML。タブ相当のインデントはスペース4。</summary>
    internal static string FormatXml(XDocument document)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "    ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = document.Declaration is null,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, settings))
        {
            document.Save(writer);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>PCM 本体など、先頭プレビューのみ読むチャンク。</summary>
    private static bool IsSamplePayloadChunk(string id) =>
        id is "data" or "wavl" or "SSND";

    private static WaveStructureNode DecodeBinaryPreview(
        BinaryReader reader,
        string id,
        long chunkOffset,
        uint size,
        string label)
    {
        var samplePayload = IsSamplePayloadChunk(id);
        var limit = samplePayload ? DataPreviewPageBytes : BinaryFullPreviewMaxBytes;
        var take = (int)Math.Min(size, limit);
        var bytes = take > 0 ? reader.ReadBytes(take) : [];
        // 「続きあり／残り未読」はサンプル本体だけ。他は全文（保険上限超えは黙って切る）。
        var truncated = samplePayload && size > take;
        var hex = FormatHexDump(bytes, absoluteOffset: 0);
        if (truncated)
        {
            hex = string.IsNullOrEmpty(hex)
                ? UiStrings.WaveStructureDataTruncated(size, take)
                : hex + Environment.NewLine + Environment.NewLine
                  + UiStrings.WaveStructureDataTruncated(size, take);
        }

        return new WaveStructureNode(
            id,
            label,
            chunkOffset,
            size,
            WaveStructureContentKind.Binary,
            string.IsNullOrEmpty(hex) ? UiStrings.WaveStructureEmptyPayload : hex,
            contentTruncated: truncated,
            isInvalid: false,
            error: null);
    }

    private static WaveStructureNode TextNode(string id, long offset, long size, string content) =>
        new(id, id, offset, size, WaveStructureContentKind.Text, content, false, false, null);

    private static WaveStructureNode InvalidText(string id, long offset, long size, string error) =>
        new(id, id, offset, size, WaveStructureContentKind.Text, error, false, true, error);

    private static string BuildContainerSummary(
        string container,
        string form,
        uint declaredSize,
        long fileLength) =>
        WaveStructureNode.FormatKeyValueTable(
        [
            ("Container", container),
            ("Form", form),
            ("Declared size", declaredSize.ToString(CultureInfo.InvariantCulture)),
            ("File length", fileLength.ToString(CultureInfo.InvariantCulture)),
        ]);

    private static string FormatTagName(ushort tag) => tag switch
    {
        0x0001 => "PCM",
        0x0003 => "IEEE float",
        0x0006 => "A-law",
        0x0007 => "μ-law",
        0xFFFE => "EXTENSIBLE",
        _ => "unknown",
    };

    private static string InfoFieldName(string id) => id switch
    {
        "INAM" => "Title",
        "IART" => "Artist",
        "IPRD" => "Album",
        "IGNR" => "Genre",
        "ICMT" => "Comment",
        "ICRD" => "Date",
        "ITRK" or "IPRT" => "Track",
        "IMUS" or "IENG" => "Composer/Engineer",
        "ISFT" => "Software",
        "ICOP" => "Copyright",
        _ => "INFO",
    };

    private static bool IsLikelyTextChunk(string id) =>
        id is "INAM" or "IART" or "IPRD" or "IGNR" or "ICMT" or "ICRD" or "ITRK" or "IPRT"
            or "IMUS" or "IENG" or "ISFT" or "ICOP" or "ISBJ" or "IKEY" or "ITCH" or "IARL"
            or "DISP" or "NAME" or "AUTH" or "ANNO" or "(c) ";

    internal static string FormatHexDump(ReadOnlySpan<byte> bytes, int absoluteOffset)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder((bytes.Length / 16 + 1) * 80);
        for (var i = 0; i < bytes.Length; i += 16)
        {
            var line = bytes.Slice(i, Math.Min(16, bytes.Length - i));
            sb.Append((absoluteOffset + i).ToString("X8", CultureInfo.InvariantCulture));
            sb.Append("  ");
            for (var c = 0; c < 16; c++)
            {
                if (c == 8)
                {
                    sb.Append(' ');
                }

                if (c < line.Length)
                {
                    sb.Append(line[c].ToString("X2", CultureInfo.InvariantCulture));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }
            }

            sb.Append(' ');
            for (var c = 0; c < line.Length; c++)
            {
                var b = line[c];
                sb.Append(b is >= 32 and <= 126 ? (char)b : '.');
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string ReadFourCc(BinaryReader reader) =>
        Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static string ReadCString(BinaryReader reader, int size)
    {
        if (size <= 0)
        {
            return string.Empty;
        }

        var bytes = reader.ReadBytes(size);
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        if (length <= 0)
        {
            return string.Empty;
        }

        // UTF-8 を試し、だめなら Latin1（INFO 等の可変長テキストで一般的）。
        try
        {
            var utf8 = Encoding.UTF8.GetString(bytes, 0, length);
            if (!utf8.Contains('\uFFFD', StringComparison.Ordinal))
            {
                return utf8.Trim();
            }
        }
        catch (DecoderFallbackException)
        {
        }

        return Encoding.Latin1.GetString(bytes, 0, length).Trim();
    }

    private static string ReadFixed(BinaryReader reader, int length)
    {
        var bytes = reader.ReadBytes(length);
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] is 0 or 0x20)
        {
            end--;
        }

        // BWF 固定欄は ASCII 規定。Latin1 なら非 ASCII も '?' 置換せず 1 バイト＝1 文字で見せられる。
        return end <= 0 ? string.Empty : Encoding.Latin1.GetString(bytes, 0, end).Trim();
    }

    private static int ReadIeee80(byte[] bytes)
    {
        if (bytes.Length < 10)
        {
            return 0;
        }

        var exponent = ((bytes[0] & 0x7F) << 8) | bytes[1];
        var hi = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(2, 4));
        var lo = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(6, 4));
        var fraction = ((ulong)hi << 32) | lo;
        if (exponent == 0 && fraction == 0)
        {
            return 0;
        }

        var value = fraction * Math.Pow(2, exponent - 16383 - 63);
        if ((bytes[0] & 0x80) != 0)
        {
            value = -value;
        }

        return (int)Math.Round(value);
    }
}
