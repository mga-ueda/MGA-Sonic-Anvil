using System.IO;
using System.Xml;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// Cubase / Nuendo が工場問い合わせの結果を書き込んだ vst3plugins.xml。
/// オプションで有効にしたときだけ読み、種別などを埋める（無い環境と同じ経路を確認できるよう既定オフ）。
/// </summary>
internal sealed class Vst3SteinbergHostCache
{
    public sealed record Entry(string Name, string Vendor, string Category, string Version);

    private readonly Dictionary<string, Entry> _byBundle = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byBundle.Count;

    public static Vst3SteinbergHostCache Empty { get; } = new();

    /// <summary>%AppData%\Steinberg 配下の vst3plugins.xml をすべて読む。壊れても空。</summary>
    public static Vst3SteinbergHostCache Load(CancellationToken cancellationToken = default)
    {
        var cache = new Vst3SteinbergHostCache();
        foreach (var file in EnumerateCacheFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                cache.LoadFile(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException
                or ArgumentException or NotSupportedException)
            {
            }
        }

        return cache;
    }

    /// <summary>バンドル／バイナリのどちらのパスでも、.vst3 ルートで引く。</summary>
    public bool TryFind(string pluginPath, out Entry entry)
    {
        entry = null!;
        if (!TryGetBundleRoot(pluginPath, out var root))
        {
            return false;
        }

        return _byBundle.TryGetValue(root, out entry!);
    }

    /// <summary>テスト用。XML 文字列から構築する。</summary>
    internal static Vst3SteinbergHostCache FromXml(string xml)
    {
        var cache = new Vst3SteinbergHostCache();
        var settings = CreateXmlSettings();
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        cache.IngestDocument(doc);
        return cache;
    }

    /// <summary>
    /// `Foo.vst3` または `Foo.vst3\Contents\...\Foo.vst3` から外側のバンドル根を返す。
    /// </summary>
    internal static bool TryGetBundleRoot(string path, out string root)
    {
        root = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Trim();
        if (!PathUtil.TryGetFullPathTrimmedSeparators(normalized, out var full))
        {
            try
            {
                full = PathUtil.TrimDirectorySeparators(
                    Path.GetFullPath(normalized, Directory.GetCurrentDirectory()));
            }
            catch (Exception ex) when (PathUtil.IsPathFormatException(ex))
            {
                return false;
            }
        }

        // Contents\x86_64-win\Foo.vst3 のような内側ではなく、外側のバンドル根を取る。
        string? found = null;
        var current = full;
        while (!string.IsNullOrEmpty(current))
        {
            if (current.EndsWith(Vst3PluginScanner.Extension, StringComparison.OrdinalIgnoreCase))
            {
                found = PathUtil.TrimDirectorySeparators(current);
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        if (found is null)
        {
            return false;
        }

        root = found;
        return true;
    }

    private static IEnumerable<string> EnumerateCacheFiles()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Steinberg"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steinberg"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "vst3plugins.xml", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (file.Contains("VST3 Cache", StringComparison.OrdinalIgnoreCase)
                    && seen.Add(file))
                {
                    yield return file;
                }
            }
        }
    }

    private void LoadFile(string path)
    {
        var settings = CreateXmlSettings();
        using var reader = XmlReader.Create(path, settings);
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(reader);
        IngestDocument(doc);
    }

    private void IngestDocument(XmlDocument doc)
    {
        var plugins = doc.SelectNodes("//plugin");
        if (plugins is null)
        {
            return;
        }

        foreach (XmlNode plugin in plugins)
        {
            var path = plugin["path"]?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !TryGetBundleRoot(path, out var root))
            {
                continue;
            }

            var usable = new List<(string FactoryCategory, Entry Entry)>();
            foreach (XmlNode child in plugin.ChildNodes)
            {
                if (!string.Equals(child.Name, "class", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var factoryCategory = child["category"]?.InnerText?.Trim() ?? string.Empty;
                var sub = child["subCategories"]?.InnerText?.Trim() ?? string.Empty;
                if (!Vst3EffectClass.IsUsableEffectClass(factoryCategory, sub))
                {
                    continue;
                }

                usable.Add((
                    factoryCategory,
                    new Entry(
                        child["name"]?.InnerText?.Trim() ?? string.Empty,
                        FirstNonEmpty(child["vendor"]?.InnerText, plugin["vendor"]?.InnerText),
                        sub,
                        child["version"]?.InnerText?.Trim() ?? string.Empty)));
            }

            if (usable.Count == 0)
            {
                continue;
            }

            var preferred = usable
                .OrderBy(u => Vst3EffectClass.NamePreference(u.FactoryCategory))
                .ThenByDescending(u => u.Entry.Name.Length)
                .First();
            var merged = new Entry(
                preferred.Entry.Name,
                preferred.Entry.Vendor,
                Vst3EffectClass.MergeSubCategories(usable.Select(u => u.Entry.Category)),
                preferred.Entry.Version);

            if (!_byBundle.TryGetValue(root, out var existing)
                || merged.Category.Length > existing.Category.Length
                || (merged.Category.Length == existing.Category.Length
                    && merged.Name.Length > existing.Name.Length))
            {
                _byBundle[root] = merged;
            }
        }
    }

    private static string FirstNonEmpty(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a))
        {
            return a.Trim();
        }

        return string.IsNullOrWhiteSpace(b) ? string.Empty : b.Trim();
    }

    private static XmlReaderSettings CreateXmlSettings() =>
        new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
}
