using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Config;

/// <summary>JSON では数値で保存する（並びを変えない）。</summary>
internal enum Vst3RecognitionKind
{
    /// <summary>認識する側。</summary>
    Recognized = 0,

    /// <summary>ユーザーが手動で認識させない側へ移した。</summary>
    ManualExcluded = 1,

    /// <summary>認識失敗。スキャン時に自動で認識させない側へ送る。</summary>
    Failed = 2,

    /// <summary>楽器（Instrument）。本アプリではスキャン時に認識させない側へ送る。</summary>
    InstrumentExcluded = 3,

    /// <summary>OnlyARA。本アプリでは ARA 非対応のためスキャン時に認識させない側へ送る。</summary>
    AraExcluded = 4,
}

internal sealed class Vst3PluginEntry
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public DateTimeOffset LastWriteUtc { get; set; }

    /// <summary>ブランド（メーカー）。分からなければ空。</summary>
    public string Vendor { get; set; } = string.Empty;

    /// <summary>種別。moduleinfo.json の Sub Categories をそのまま（例: Fx|Reverb）。</summary>
    public string Category { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>VST3 クラス ID（Base64）。WaveShell 展開後は必須。</summary>
    public string? ClassId { get; set; }

    public Vst3RecognitionKind Recognition { get; set; } = Vst3RecognitionKind.Recognized;

    /// <summary>認識失敗の理由コード（<see cref="Vst3PluginMetadata"/> の Failure*）。</summary>
    public string? FailureReason { get; set; }

    [JsonIgnore]
    public bool IsRecognized => Recognition == Vst3RecognitionKind.Recognized;
}

internal sealed class Vst3PluginListDocument
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>最後にスキャンした時刻。未スキャンは null。</summary>
    public DateTimeOffset? ScannedAtUtc { get; set; }

    public Vst3PluginEntry[] Plugins { get; set; } = [];
}

/// <summary>
/// スキャン結果と認識する／しないの振り分け（vst-plugins.json）。settings.json とは別ファイル。
/// 次回起動時はここを読むだけでスキャンし直さない。失敗してもアプリは止めない。
/// </summary>
internal static class Vst3PluginList
{
    public const string FileName = "vst-plugins.json";

    private static string? _directoryOverride;

    public static string FilePath => System.IO.Path.Combine(
        _directoryOverride ?? AppStorage.RootDirectory,
        FileName);

    /// <summary>テスト用。指定フォルダのファイルを使う。</summary>
    internal static void UseDirectoryForTests(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directoryOverride = System.IO.Path.GetFullPath(directory);
        Directory.CreateDirectory(_directoryOverride);
    }

    internal static void ResetForTests() => _directoryOverride = null;

    /// <summary>無い／壊れているときは空の一覧。古い形式（Name/Path だけ）も読める。</summary>
    public static Vst3PluginListDocument Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return new Vst3PluginListDocument();
            }

            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize(json, Vst3PluginListJsonContext.Default.Vst3PluginListDocument);
            if (doc is null)
            {
                return new Vst3PluginListDocument();
            }

            doc.Plugins = doc.Plugins?
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Path))
                .Select(Normalize)
                .ToArray() ?? [];
            return doc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            return new Vst3PluginListDocument();
        }
    }

    /// <summary>保存できたら true。</summary>
    public static bool Save(Vst3PluginListDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
            var json = JsonSerializer.Serialize(document, Vst3PluginListJsonContext.Default.Vst3PluginListDocument);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>前回の振り分けを持たない、まっさらなスキャン結果。</summary>
    public static Vst3PluginListDocument FromScan(IEnumerable<Vst3ScannedPlugin> plugins, DateTimeOffset scannedAtUtc) =>
        MergeScan(null, plugins, scannedAtUtc);

    /// <summary>
    /// 今回のスキャン結果へ前回の振り分けを引き継ぐ（パスは大文字小文字を区別しない）。
    /// 手動で認識させないものは、まだあればそのまま。認識失敗は必ず認識させない側。
    /// 前回失敗でも今回読めれば認識する側へ戻す。見つからなくなったものは落とす。
    /// </summary>
    public static Vst3PluginListDocument MergeScan(
        Vst3PluginListDocument? previous,
        IEnumerable<Vst3ScannedPlugin> plugins,
        DateTimeOffset scannedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        var manual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in previous?.Plugins ?? [])
        {
            if (entry.Recognition == Vst3RecognitionKind.ManualExcluded)
            {
                manual.Add(entry.Path);
            }
        }

        var merged = new List<Vst3PluginEntry>();
        // WaveShell 展開後は同一 Path に複数 ClassId が付く
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            var meta = plugin.Metadata;
            var key = plugin.Info.Path + "\0" + (meta.ClassId ?? string.Empty);
            if (!seen.Add(key))
            {
                continue;
            }

            var recognition = manual.Contains(plugin.Info.Path)
                ? Vst3RecognitionKind.ManualExcluded
                : meta.IsFailed
                    ? Vst3RecognitionKind.Failed
                    : Vst3PluginMetadata.IsInstrumentCategory(meta.Category)
                        ? Vst3RecognitionKind.InstrumentExcluded
                        : Vst3PluginMetadata.IsOnlyAraCategory(meta.Category)
                            ? Vst3RecognitionKind.AraExcluded
                            : Vst3RecognitionKind.Recognized;
            merged.Add(new Vst3PluginEntry
            {
                Name = string.IsNullOrWhiteSpace(meta.Name) ? plugin.Info.Name : meta.Name,
                Path = plugin.Info.Path,
                LastWriteUtc = plugin.Info.LastWriteUtc,
                Vendor = meta.Vendor,
                Category = meta.Category,
                Version = meta.Version,
                ClassId = meta.ClassId,
                Recognition = recognition,
                FailureReason = meta.FailureReason,
            });
        }

        return new Vst3PluginListDocument
        {
            ScannedAtUtc = scannedAtUtc,
            Plugins = [.. merged],
        };
    }

    /// <summary>
    /// 設定画面の追加／削除。認識させると失敗理由も消す（ユーザーの上書き）。
    /// 認識させないへ移すと手動扱い。変わった件数を返す。
    /// </summary>
    public static int SetRecognized(Vst3PluginListDocument document, IEnumerable<string> paths, bool recognized)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paths);
        var targets = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var changed = 0;
        foreach (var entry in document.Plugins)
        {
            if (!targets.Contains(entry.Path) || entry.IsRecognized == recognized)
            {
                continue;
            }

            if (recognized)
            {
                entry.Recognition = Vst3RecognitionKind.Recognized;
                entry.FailureReason = null;
            }
            else
            {
                entry.Recognition = Vst3RecognitionKind.ManualExcluded;
            }

            changed++;
        }

        return changed;
    }

    /// <summary>キャッシュファイルを消す。消せたか、元から無ければ true。</summary>
    public static bool Clear()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Vst3PluginEntry Normalize(Vst3PluginEntry entry)
    {
        // 古いファイルや手書きの null を空文字へ。範囲外の値は認識する側へ戻す。
        entry.Name ??= string.Empty;
        entry.Vendor ??= string.Empty;
        entry.Category ??= string.Empty;
        entry.Version ??= string.Empty;
        if (!Enum.IsDefined(entry.Recognition))
        {
            entry.Recognition = Vst3RecognitionKind.Recognized;
        }

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            entry.Name = System.IO.Path.GetFileNameWithoutExtension(entry.Path);
        }

        return entry;
    }
}

[JsonSerializable(typeof(Vst3PluginListDocument))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class Vst3PluginListJsonContext : JsonSerializerContext;
