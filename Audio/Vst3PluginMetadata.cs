using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MgaSonicAnvil.Audio;

/// <summary>スキャンで見つけた 1 件と、その付帯情報。</summary>
internal sealed record Vst3ScannedPlugin(Vst3PluginInfo Info, Vst3PluginMetadata Metadata);

/// <summary>スキャンの進み具合。Fraction は 0〜1。Current/Total は付帯情報を読み終えた件数（列挙中は 0/0）。</summary>
internal readonly record struct Vst3ScanProgress(double Fraction, int Current, int Total);

/// <summary>
/// VST3 の付帯情報（ブランド・種別・バージョン）。
/// 種別の本命は工場の subCategories。取得順は
/// moduleinfo.json →（オプション）Cubase/Nuendo の vst3plugins.xml →
/// 別プロセスの GetPluginFactory 問い合わせ → バイナリ埋め込み／名前推定。
/// ホストキャッシュは設定で明示オンしたときだけ使う（既定オフ）。
/// ブランドや種別が空なのは失敗ではない。FailureReason が入っているときだけ認識失敗。
/// </summary>
internal sealed record Vst3PluginMetadata(
    string Name,
    string Vendor,
    string Category,
    string Version,
    string? FailureReason,
    string? ClassId = null)
{
    /// <summary>パスが無くなっていた。</summary>
    public const string FailureMissing = "missing";

    /// <summary>バンドルにバイナリも moduleinfo.json も無い（空フォルダなど）。</summary>
    public const string FailureEmptyBundle = "empty-bundle";

    /// <summary>moduleinfo.json が壊れていて、バイナリも無い。</summary>
    public const string FailureModuleInfoCorrupt = "moduleinfo-corrupt";

    /// <summary>フォルダやファイルが読めない。</summary>
    public const string FailureUnreadable = "unreadable";

    public static readonly Vst3PluginMetadata Empty = new(string.Empty, string.Empty, string.Empty, string.Empty, null);

    public bool IsFailed => FailureReason is not null;

    /// <summary>
    /// Steinberg の大分類 Instrument（Instrument|Synth など含む）。
    /// Fx|Instrument はエフェクト側なので対象外。
    /// </summary>
    public static bool IsInstrumentCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return false;
        }

        var trimmed = category.Trim();
        return trimmed.Equals("Instrument", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Instrument|", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] BinaryFolders = ["x86_64-win", "arm64x-win", "arm64ec-win", "arm64-win", "x86-win"];

    /// <summary>巨大なバイナリでも 1 件あたりこれ以上は読まない。</summary>
    private const int MaxBinaryScanBytes = 32 * 1024 * 1024;

    private const int BinaryChunkBytes = 1024 * 1024;

    /// <summary>Steinberg の PlugType（ivstaudioprocessor.h）。'|' を含まないものは前後が NUL のときだけ採る。</summary>
    private static readonly string[] EmbeddedCategoryTokens =
    [
        "Fx|Reverb", "Fx|Delay", "Fx|EQ", "Fx|Filter", "Fx|Dynamics", "Fx|Distortion", "Fx|Modulation",
        "Fx|Pitch Shift", "Fx|Restoration", "Fx|Mastering", "Fx|Spatial", "Fx|Surround", "Fx|Generator",
        "Fx|Network", "Fx|Analyzer", "Fx|Tools", "Fx|Instrument",
        "Instrument|Synth", "Instrument|Drum", "Instrument|Sampler", "Instrument|External",
        "Instrument", "Analyzer", "Spatial", "OnlyARA", "Fx",
    ];

    private sealed record BinaryPattern(string Token, byte[] Bytes, int UnitBytes, bool Strict);

    private static readonly BinaryPattern[] EmbeddedPatterns = EmbeddedCategoryTokens
        .SelectMany(token => new[]
        {
            new BinaryPattern(token, Encoding.ASCII.GetBytes(token), 1, !token.Contains('|')),
            new BinaryPattern(token, Encoding.Unicode.GetBytes(token), 2, !token.Contains('|')),
        })
        .ToArray();

    /// <summary>チャンク境界をまたぐ一致も拾えるよう、前後の境界文字ぶんも重ねて読む。</summary>
    private static readonly int ChunkOverlap = EmbeddedPatterns.Max(p => p.Bytes.Length) + 4;

    private sealed record NameRule(string Category, string[] Prefixes, string[] Exact);

    /// <summary>先に並んだものを優先。エフェクト名に楽器の語が混じる（Drum Bus など）ので Fx を先に見る。</summary>
    private static readonly NameRule[] NameRules =
    [
        new("Fx|Restoration", ["denois", "declick", "declip", "decrackl", "dehum", "restor", "noisereduc"], []),
        new("Fx|Pitch Shift", ["pitch", "harmoniz", "harmonis", "autotune", "octaver", "transpos"], []),
        new("Fx|Reverb", ["reverb", "verb"], []),
        new("Fx|Delay", ["delay", "echo"], []),
        new("Fx|Dynamics", ["compress", "limiter", "limiting", "gate", "expander", "deess", "transient", "maximiz", "maximis", "leveler", "leveller", "dynamic"], ["comp"]),
        new("Fx|EQ", ["equaliz", "equalis"], ["eq"]),
        new("Fx|Modulation", ["chorus", "flang", "phaser", "phasing", "tremolo", "vibrato", "rotary"], []),
        new("Fx|Distortion", ["distort", "saturat", "overdriv", "fuzz", "bitcrush", "crusher", "decimat"], []),
        new("Fx|Filter", ["filter"], ["wah"]),
        new("Fx|Analyzer", ["analyz", "analys", "spectrum", "spectrogram", "oscilloscope", "meter", "loudness"], []),
        new("Fx|Spatial", ["imager", "widen", "stereowidth", "stereoiz", "stereois", "panner", "binaural"], []),
        new("Fx|Mastering", ["mastering"], []),
        new("Instrument|Drum", ["drum", "percussion"], ["808", "909"]),
        new("Instrument|Sampler", ["sampler", "rompler"], []),
        new("Instrument|Synth", ["synth"], ["vsti"]),
    ];

    /// <summary>名前に入っていても種別を表さない語（ブランド名など）。</summary>
    private static readonly string[] NameNoise = ["FabFilter"];

    /// <summary>これだけで出来ている FileDescription／ProductName は名前として使わない。</summary>
    private static readonly HashSet<string> GenericNameWords = new(StringComparer.Ordinal)
    {
        "vst", "plugin", "plug", "in", "dll", "module", "audio", "effect", "fx", "x",
        "bit", "win", "windows", "library", "version", "the", "a", "for", "steinberg",
    };

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        // 実物の moduleinfo.json は JSON5 寄り（末尾カンマ・コメント）のことがある。
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>工場プローブの待ち時間（1 プラグイン）。落ちる／固まるプラグイン用。</summary>
    private static readonly TimeSpan FactoryProbeTimeout = TimeSpan.FromSeconds(8);

    /// <summary>1 件読むごとに progress へ報告する（Fraction は読み終えた割合）。</summary>
    public static IReadOnlyList<Vst3ScannedPlugin> Enrich(
        IEnumerable<Vst3PluginInfo> plugins,
        IProgress<Vst3ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool useSteinbergHostCache = false) =>
        Enrich(
            plugins,
            useSteinbergHostCache ? Vst3SteinbergHostCache.Load(cancellationToken) : Vst3SteinbergHostCache.Empty,
            progress,
            cancellationToken);

    /// <summary>ホストキャッシュを渡す版（スキャン中は 1 回ロードしたものを使い回す）。</summary>
    public static IReadOnlyList<Vst3ScannedPlugin> Enrich(
        IEnumerable<Vst3PluginInfo> plugins,
        Vst3SteinbergHostCache hostCache,
        IProgress<Vst3ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostCache);
        var list = plugins as IReadOnlyList<Vst3PluginInfo> ?? plugins.ToArray();
        var result = new List<Vst3ScannedPlugin>(list.Count);
        progress?.Report(new Vst3ScanProgress(list.Count == 0 ? 1 : 0, 0, list.Count));
        for (var i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = list[i];
            var meta = Read(info.Path, hostCache, cancellationToken);
            if (!meta.IsFailed && Vst3FactoryProbe.IsWaveShellPath(info.Path))
            {
                // メニュー表示時ではなくスキャン時に Shell を展開する
                var expanded = ExpandWaveShellAtScan(info, meta);
                if (expanded.Count > 0)
                {
                    result.AddRange(expanded);
                }
                else
                {
                    result.Add(new Vst3ScannedPlugin(info, meta));
                }
            }
            else
            {
                result.Add(new Vst3ScannedPlugin(info, meta));
            }

            progress?.Report(new Vst3ScanProgress((i + 1) / (double)list.Count, i + 1, list.Count));
        }

        return result;
    }

    /// <summary>WaveShell を setHostContext 後のクラス一覧へ展開（ClassId 付き）。</summary>
    internal static List<Vst3ScannedPlugin> ExpandWaveShellAtScan(
        Vst3PluginInfo shellInfo,
        Vst3PluginMetadata shellMeta)
    {
        var result = new List<Vst3ScannedPlugin>();
        try
        {
            var module = Vst3Module.TryAcquire(shellInfo.Path);
            if (module is null)
            {
                return result;
            }

            try
            {
                foreach (var cls in module.Classes)
                {
                    var vendor = string.IsNullOrWhiteSpace(cls.Vendor)
                        ? (string.IsNullOrWhiteSpace(shellMeta.Vendor) ? "Waves" : shellMeta.Vendor)
                        : cls.Vendor;
                    if (vendor.Contains("WaveShell", StringComparison.OrdinalIgnoreCase))
                    {
                        vendor = "Waves";
                    }

                    var meta = new Vst3PluginMetadata(
                        cls.Name,
                        vendor,
                        string.IsNullOrWhiteSpace(cls.SubCategories) ? shellMeta.Category : cls.SubCategories,
                        string.IsNullOrWhiteSpace(cls.Version) ? shellMeta.Version : cls.Version,
                        FailureReason: null,
                        ClassId: Vst3HostNative.ClassIdToBase64(cls.Cid));
                    result.Add(new Vst3ScannedPlugin(shellInfo, meta));
                }
            }
            finally
            {
                module.Dispose();
            }
        }
        catch
        {
            result.Clear();
        }

        return result;
    }

    /// <summary>キャンセル以外の例外は投げない。読めないものは FailureReason 付きで返す。</summary>
    public static Vst3PluginMetadata Read(
        string path,
        CancellationToken cancellationToken = default,
        bool useSteinbergHostCache = false) =>
        Read(
            path,
            useSteinbergHostCache ? Vst3SteinbergHostCache.Load(cancellationToken) : Vst3SteinbergHostCache.Empty,
            cancellationToken);

    /// <summary>ホストキャッシュを渡す版。</summary>
    public static Vst3PluginMetadata Read(
        string path,
        Vst3SteinbergHostCache hostCache,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostCache);
        try
        {
            if (Directory.Exists(path))
            {
                return ReadBundle(path, hostCache, cancellationToken);
            }

            if (File.Exists(path))
            {
                var info = ReadVersionInfo(path);
                var host = hostCache.TryFind(path, out var entry) ? entry : null;
                return AssembleMetadata(
                    path,
                    moduleCategory: null,
                    host,
                    binary: path,
                    versionInfo: info,
                    nameHints: [NameFromVersionInfo(info), Path.GetFileNameWithoutExtension(path)],
                    cancellationToken);
            }

            return Empty with { FailureReason = FailureMissing };
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return Empty with { FailureReason = FailureUnreadable };
        }
    }

    private static Vst3PluginMetadata ReadBundle(
        string bundle,
        Vst3SteinbergHostCache hostCache,
        CancellationToken cancellationToken)
    {
        var contents = Path.Combine(bundle, "Contents");
        var binary = FindBinary(contents);
        var versionInfo = binary is null ? VersionText.Empty : ReadVersionInfo(binary);
        var host = hostCache.TryFind(bundle, out var entry) ? entry : null;

        var moduleInfoPath = FindModuleInfo(contents);
        ModuleInfo? module = null;
        var corrupt = false;
        if (moduleInfoPath is not null)
        {
            module = TryParseModuleInfo(moduleInfoPath);
            corrupt = module is null;
        }

        if (module is null && binary is null && host is null)
        {
            return Empty with { FailureReason = corrupt ? FailureModuleInfoCorrupt : FailureEmptyBundle };
        }

        return AssembleMetadata(
            bundle,
            module?.Category,
            host,
            binary,
            versionInfo,
            nameHints:
            [
                module?.Name,
                host?.Name,
                NameFromVersionInfo(versionInfo),
                Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(bundle)),
            ],
            cancellationToken,
            moduleVendor: module?.Vendor,
            moduleVersion: module?.Version);
    }

    /// <summary>
    /// 名前・ブランド・種別・バージョンをまとめて埋める。
    /// Softube / Melda はクラス Vendor が空で、種別だけ取れてブランドが欠けることがあった。
    /// </summary>
    private static Vst3PluginMetadata AssembleMetadata(
        string pluginPath,
        string? moduleCategory,
        Vst3SteinbergHostCache.Entry? host,
        string? binary,
        VersionText versionInfo,
        string?[] nameHints,
        CancellationToken cancellationToken,
        string? moduleVendor = null,
        string? moduleVersion = null)
    {
        var name = host?.Name ?? string.Empty;
        foreach (var hint in nameHints)
        {
            name = FirstNonEmpty(name, hint);
        }

        var vendor = FirstNonEmpty(moduleVendor, host?.Vendor, versionInfo.Vendor);
        vendor = FirstNonEmpty(vendor, GuessVendorFromPath(pluginPath));
        var version = FirstNonEmpty(moduleVersion, host?.Version, versionInfo.Version);
        var category = FirstNonEmpty(moduleCategory, host?.Category);

        // WaveShell は子プロセス探針を飛ばす。メニュー側でクラス展開する前提で認識させる。
        if (Vst3FactoryProbe.IsWaveShellPath(pluginPath)
            || (binary is not null && Vst3FactoryProbe.IsWaveShellPath(binary)))
        {
            name = FirstNonEmpty(name, "WaveShell");
            vendor = FirstNonEmpty(vendor, "Waves");
            category = FirstNonEmpty(category, "Fx");
            return new Vst3PluginMetadata(name, vendor, category, version, null);
        }

        // 種別またはブランドが欠けていれば工場を 1 回だけ聞く（両方埋める）。
        Vst3FactoryProbe.ProbeResult? probed = null;
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(vendor))
        {
            probed = Vst3FactoryProbe.ProbeOutOfProcess(pluginPath, FactoryProbeTimeout, cancellationToken);
            name = FirstNonEmpty(name, probed?.Name);
            vendor = FirstNonEmpty(vendor, probed?.Vendor);
            version = FirstNonEmpty(version, probed?.Version);
            category = FirstNonEmpty(category, probed?.Category);
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            category = GuessCategory(
                binary,
                versionInfo with { Vendor = vendor },
                nameHints,
                cancellationToken);
        }

        return new Vst3PluginMetadata(name, vendor, category, version, null);
    }

    /// <summary>パスから分かるベンダー（工場を叩かずに Softube / Melda を埋める）。</summary>
    internal static string GuessVendorFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (path.Contains("MeldaProduction", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}Melda{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return "MeldaProduction";
        }

        try
        {
            var root = path;
            if (File.Exists(path))
            {
                root = Path.GetDirectoryName(path) ?? path;
            }

            // Softube バンドルは Contents\Resources\PluginInfo.spi を持つ。
            var spi = Path.Combine(root, "Contents", "Resources", "PluginInfo.spi");
            if (File.Exists(spi))
            {
                return "Softube";
            }

            // 単体ファイルのとき、親が .vst3 バンドル根ならそこを見る。
            if (File.Exists(path))
            {
                var parent = Path.GetDirectoryName(path);
                while (!string.IsNullOrEmpty(parent))
                {
                    if (parent.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
                    {
                        spi = Path.Combine(parent, "Contents", "Resources", "PluginInfo.spi");
                        if (File.Exists(spi))
                        {
                            return "Softube";
                        }

                        break;
                    }

                    var next = Path.GetDirectoryName(parent);
                    if (string.IsNullOrEmpty(next) || next == parent)
                    {
                        break;
                    }

                    parent = next;
                }
            }
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
        }

        return string.Empty;
    }

    private static string? FindModuleInfo(string contents)
    {
        var resources = Path.Combine(contents, "Resources");
        foreach (var candidate in new[]
        {
            Path.Combine(resources, "moduleinfo.json"),
            Path.Combine(contents, "moduleinfo.json"),
        })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (!Directory.Exists(resources))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateDirectories(resources)
                .Select(dir => Path.Combine(dir, "moduleinfo.json"))
                .FirstOrDefault(File.Exists);
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return null;
        }
    }

    private static string? FindBinary(string contents)
    {
        if (!Directory.Exists(contents))
        {
            return null;
        }

        foreach (var folder in BinaryFolders)
        {
            var dir = Path.Combine(contents, folder);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var found = Directory.EnumerateFiles(dir, "*.vst3").FirstOrDefault()
                ?? Directory.EnumerateFiles(dir, "*.dll").FirstOrDefault();
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private sealed record VersionText(string Vendor, string Version, string Description, string Product, string Comments)
    {
        public static readonly VersionText Empty = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private static VersionText ReadVersionInfo(string file)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            return new VersionText(
                info.CompanyName?.Trim() ?? string.Empty,
                FirstNonEmpty(info.ProductVersion?.Trim(), info.FileVersion?.Trim()),
                info.FileDescription?.Trim() ?? string.Empty,
                info.ProductName?.Trim() ?? string.Empty,
                info.Comments?.Trim() ?? string.Empty);
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return VersionText.Empty;
        }
    }

    /// <summary>FileDescription → ProductName。「VST3 Plug-in」のような中身の無い名前や会社名そのものは使わない。</summary>
    private static string NameFromVersionInfo(VersionText info)
    {
        foreach (var candidate in new[] { info.Description, info.Product })
        {
            if (string.IsNullOrWhiteSpace(candidate)
                || candidate.Equals(info.Vendor, StringComparison.OrdinalIgnoreCase)
                || SplitWords(candidate).All(word => GenericNameWords.Contains(word) || word.All(char.IsDigit)))
            {
                continue;
            }

            return candidate;
        }

        return string.Empty;
    }

    /// <summary>
    /// moduleinfo に Sub Categories が無いときの推定。バイナリの埋め込み文字列（Fx|Reverb など）を最優先。
    /// 埋め込みが Fx／Instrument のような大分類だけなら、キーワード推定が同じ大分類のときだけ細分類に置き換える。
    /// </summary>
    private static string GuessCategory(
        string? binary,
        VersionText info,
        string?[] names,
        CancellationToken cancellationToken)
    {
        string? generic = null;
        if (binary is not null)
        {
            var embedded = FindEmbeddedCategory(binary, cancellationToken);
            if (embedded is not null && embedded.Contains('|'))
            {
                return embedded;
            }

            generic = embedded;
        }

        string? guess = null;
        foreach (var text in new[] { info.Description, info.Product, info.Comments }.Concat(names))
        {
            guess = GuessCategoryFromName(text, info.Vendor);
            if (guess is not null)
            {
                break;
            }
        }

        if (generic is null)
        {
            return guess ?? string.Empty;
        }

        return guess is not null && guess.StartsWith(generic + "|", StringComparison.Ordinal) ? guess : generic;
    }

    /// <summary>
    /// 名前などの文字列から種別を推定する。CamelCase・数字・記号で語に分け、語の先頭一致（短い語は完全一致）で見る。
    /// vendor の語（4 文字以上）は先に取り除く（FabFilter の Filter などを拾わないため）。当てはまらなければ null。
    /// </summary>
    internal static string? GuessCategoryFromName(string? text, string? vendor = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var noise in NameNoise)
        {
            text = text.Replace(noise, " ", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(vendor))
        {
            foreach (var word in vendor.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var trimmed = word.Trim(',', '.', '(', ')');
                if (trimmed.Length >= 4)
                {
                    text = text.Replace(trimmed, " ", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        var words = SplitWords(text);
        if (words.Count == 0)
        {
            return null;
        }

        // 「De Esser」「Noise Gate」「Stereo Width」のように 2 語に分かれたものも 1 語として見る。
        var candidates = new List<string>(words.Count * 2);
        candidates.AddRange(words);
        for (var i = 0; i + 1 < words.Count; i++)
        {
            candidates.Add(words[i] + words[i + 1]);
        }

        foreach (var rule in NameRules)
        {
            foreach (var word in candidates)
            {
                if (rule.Exact.Contains(word)
                    || rule.Prefixes.Any(prefix => word.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    return rule.Category;
                }
            }
        }

        return null;
    }

    /// <summary>小文字の語に分ける。境界は英数字以外・小文字→大文字・大文字連続の終わり・英字⇔数字。</summary>
    private static List<string> SplitWords(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }

            if (current.Length > 0)
            {
                var prev = text[i - 1];
                var split = (char.IsLower(prev) && char.IsUpper(c))
                    || (char.IsUpper(prev) && char.IsUpper(c) && i + 1 < text.Length && char.IsLower(text[i + 1]))
                    || (char.IsDigit(prev) != char.IsDigit(c));
                if (split)
                {
                    Flush();
                }
            }

            current.Append(char.ToLowerInvariant(c));
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
    }

    /// <summary>
    /// バイナリに埋め込まれた種別文字列（UTF-8／UTF-16LE）を探す。PE ならデータセクションだけ、それ以外はファイル先頭から。
    /// 最大 <see cref="MaxBinaryScanBytes"/> まで。いちばん長い（細かい）一致を返す。読めなければ null。
    /// </summary>
    internal static string? FindEmbeddedCategory(string file, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.SequentialScan);
            var ranges = ReadDataSections(stream) ?? [(0L, stream.Length)];
            var buffer = ArrayPool<byte>.Shared.Rent(BinaryChunkBytes + ChunkOverlap);
            try
            {
                string? best = null;
                long budget = MaxBinaryScanBytes;
                foreach (var (offset, length) in ranges)
                {
                    if (budget <= 0)
                    {
                        break;
                    }

                    var take = Math.Min(length, budget);
                    budget -= take;
                    best = Longer(best, ScanRange(stream, buffer, offset, offset + take, cancellationToken));
                }

                return best;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return null;
        }
    }

    private static string? ScanRange(
        FileStream stream,
        byte[] buffer,
        long start,
        long end,
        CancellationToken cancellationToken)
    {
        string? best = null;
        var pos = start;
        while (pos < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var want = (int)Math.Min(BinaryChunkBytes + ChunkOverlap, end - pos);
            stream.Position = pos;
            var read = ReadFully(stream, buffer.AsSpan(0, want));
            if (read <= 0)
            {
                break;
            }

            var atEnd = read < want || pos + read >= end;
            best = Longer(best, MatchChunk(buffer.AsSpan(0, read), atStart: pos == start, atEnd));
            if (atEnd)
            {
                break;
            }

            pos += read - ChunkOverlap;
        }

        return best;
    }

    /// <summary>
    /// 範囲の端でない側にはみ出す境界は判定できないので見送る（重ね読みした隣のチャンクで拾える）。
    /// </summary>
    private static string? MatchChunk(ReadOnlySpan<byte> span, bool atStart, bool atEnd)
    {
        string? best = null;
        foreach (var pattern in EmbeddedPatterns)
        {
            if (best is not null && Specificity(pattern.Token) <= Specificity(best))
            {
                continue;
            }

            var from = 0;
            while (from < span.Length)
            {
                var hit = span[from..].IndexOf(pattern.Bytes);
                if (hit < 0)
                {
                    break;
                }

                hit += from;
                var before = hit - pattern.UnitBytes;
                var after = hit + pattern.Bytes.Length;
                if (IsBoundary(span, before, pattern, atStart) && IsBoundary(span, after, pattern, atEnd))
                {
                    best = pattern.Token;
                    break;
                }

                from = hit + 1;
            }
        }

        return best;
    }

    private static bool IsBoundary(ReadOnlySpan<byte> span, int index, BinaryPattern pattern, bool edgeOk)
    {
        if (index < 0 || index + pattern.UnitBytes > span.Length)
        {
            return edgeOk;
        }

        var unit = pattern.UnitBytes == 1 ? span[index] : BinaryPrimitives.ReadUInt16LittleEndian(span[index..]);
        if (pattern.Strict)
        {
            return unit == 0;
        }

        return unit >= 128 || !char.IsAsciiLetterOrDigit((char)unit);
    }

    private static string? Longer(string? current, string? next) =>
        next is not null && (current is null || Specificity(next) > Specificity(current)) ? next : current;

    /// <summary>'|' を含む細分類は、長さに関係なく大分類（Instrument など）より優先する。</summary>
    private static int Specificity(string token) =>
        (token.Contains('|') ? 1000 : 0) + token.Length;

    /// <summary>PE なら初期化済みデータで実行不可のセクション（.rdata・.data・.rsrc など）。PE でなければ null。</summary>
    private static List<(long Offset, long Length)>? ReadDataSections(FileStream stream)
    {
        const uint InitializedData = 0x00000040;
        const uint Executable = 0x20000000;
        var fileLength = stream.Length;
        if (fileLength < 64)
        {
            return null;
        }

        Span<byte> dos = stackalloc byte[64];
        stream.Position = 0;
        if (ReadFully(stream, dos) < dos.Length || dos[0] != 'M' || dos[1] != 'Z')
        {
            return null;
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos[0x3C..]);
        if (peOffset <= 0 || peOffset > fileLength - 24)
        {
            return null;
        }

        Span<byte> coff = stackalloc byte[24];
        stream.Position = peOffset;
        if (ReadFully(stream, coff) < coff.Length
            || coff[0] != 'P' || coff[1] != 'E' || coff[2] != 0 || coff[3] != 0)
        {
            return null;
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(coff[6..]);
        var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(coff[20..]);
        if (sectionCount == 0 || sectionCount > 96)
        {
            return null;
        }

        var table = new byte[sectionCount * 40];
        stream.Position = peOffset + 24L + optionalSize;
        if (ReadFully(stream, table) < table.Length)
        {
            return null;
        }

        var result = new List<(long Offset, long Length)>();
        for (var i = 0; i < sectionCount; i++)
        {
            var section = table.AsSpan(i * 40, 40);
            var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(section[16..]);
            var rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(section[20..]);
            var characteristics = BinaryPrimitives.ReadUInt32LittleEndian(section[36..]);
            if ((characteristics & InitializedData) == 0
                || (characteristics & Executable) != 0
                || rawSize == 0
                || rawPointer >= fileLength)
            {
                continue;
            }

            result.Add((rawPointer, Math.Min(rawSize, fileLength - rawPointer)));
        }

        return result.Count == 0 ? null : result;
    }

    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private sealed record ModuleInfo(string? Name, string? Vendor, string? Category, string? Version);

    /// <summary>壊れていたら null。キー名は大文字小文字・空白・記号の違いを無視して探す。</summary>
    private static ModuleInfo? TryParseModuleInfo(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream, JsonOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var factoryVendor = TryGet(root, "Factory Info", out var factory) && factory.ValueKind == JsonValueKind.Object
                ? GetString(factory, "Vendor")
                : null;
            var cls = PickClass(root);
            var name = cls is { } c1 ? GetString(c1, "Name") : null;
            var vendor = cls is { } c2 ? GetString(c2, "Vendor") : null;
            var version = cls is { } c3 ? GetString(c3, "Version") : null;
            var category = MergeModuleSubCategories(root);
            if (string.IsNullOrWhiteSpace(category) && cls is { } c4)
            {
                category = GetSubCategories(c4);
            }

            return new ModuleInfo(
                NullIfEmpty(FirstNonEmpty(name, GetString(root, "Name"))),
                NullIfEmpty(FirstNonEmpty(vendor, factoryVendor)),
                NullIfEmpty(category),
                NullIfEmpty(FirstNonEmpty(version, GetString(root, "Version"))));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return null;
        }
    }

    private static string? MergeModuleSubCategories(JsonElement root)
    {
        if (!TryGet(root, "Classes", out var classes) || classes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var subs = new List<string>();
        foreach (var cls in classes.EnumerateArray())
        {
            if (cls.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var category = GetString(cls, "Category") ?? string.Empty;
            var sub = GetSubCategories(cls);
            if (Vst3EffectClass.IsUsableEffectClass(category, sub) && sub is not null)
            {
                subs.Add(sub);
            }
        }

        var merged = Vst3EffectClass.MergeSubCategories(subs);
        return string.IsNullOrWhiteSpace(merged) ? null : merged;
    }

    /// <summary>
    /// 使える音のクラスを優先（Audio Module / Audio Mix Processor など。Controller は除外）。
    /// Sub Categories に Fx を含むものを優先し、無ければ Audio Module、その次に最初の usable。
    /// </summary>
    private static JsonElement? PickClass(JsonElement root)
    {
        if (!TryGet(root, "Classes", out var classes) || classes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? firstUsable = null;
        JsonElement? firstAudioModule = null;
        JsonElement? first = null;
        foreach (var cls in classes.EnumerateArray())
        {
            if (cls.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            first ??= cls;
            var category = GetString(cls, "Category") ?? string.Empty;
            var sub = GetSubCategories(cls) ?? string.Empty;
            if (!Vst3EffectClass.IsUsableEffectClass(category, sub)
                && Vst3EffectClass.IsSkippableCategory(category))
            {
                continue;
            }

            // sub が空でも Audio Module なら名前用に残す（種別は後段で補う）。
            var usable = Vst3EffectClass.IsUsableEffectClass(category, sub)
                || (!Vst3EffectClass.IsSkippableCategory(category)
                    && category.Contains("Audio", StringComparison.OrdinalIgnoreCase));
            if (!usable)
            {
                continue;
            }

            firstUsable ??= cls;
            if (category.Contains("Audio Module", StringComparison.OrdinalIgnoreCase))
            {
                firstAudioModule ??= cls;
            }

            if (sub.Split('|').Any(s => s.Trim().Equals("Fx", StringComparison.OrdinalIgnoreCase)))
            {
                return cls;
            }
        }

        return firstAudioModule ?? firstUsable ?? first;
    }

    /// <summary>配列（["Fx","Reverb"]）でも文字列（"Fx|Reverb"）でも Fx|Reverb の形にそろえる。</summary>
    private static string? GetSubCategories(JsonElement cls)
    {
        if (!TryGet(cls, "Sub Categories", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Array => string.Join(
                '|',
                value.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString()?.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))),
            _ => null,
        };
    }

    private static string? GetString(JsonElement obj, string key) =>
        TryGet(obj, key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool TryGet(JsonElement obj, string key, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            var wanted = LooseKey(key);
            foreach (var property in obj.EnumerateObject())
            {
                if (LooseKey(property.Name) == wanted)
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string LooseKey(string key) =>
        new(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a! : b ?? string.Empty;

    private static string FirstNonEmpty(string? a, string? b, string? c) =>
        FirstNonEmpty(FirstNonEmpty(a, b), c);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool IsSkippable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException;
}
