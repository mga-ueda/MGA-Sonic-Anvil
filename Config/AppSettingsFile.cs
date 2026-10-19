using System.IO;
using System.Text.Json;

namespace MgaSonicAnvil.Config;

internal enum SettingsFileReset
{
    None,
    Outdated,
    Invalid,
}

/// <summary>settings.json の読み書き。世代違いと壊れたファイルは破棄して作り直す。</summary>
internal static class AppSettingsFile
{
    public static AppSettings Load(
        string settingsPath,
        string? leftoverSessionDocumentPath,
        string? sessionDirectory,
        out SettingsFileReset reset)
    {
        reset = SettingsFileReset.None;
        if (!File.Exists(settingsPath))
        {
            return AppSettings.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            if (!TryReadGeneration(json, out var generation))
            {
                return Recreate(settingsPath, leftoverSessionDocumentPath, sessionDirectory, SettingsFileReset.Invalid, out reset);
            }

            if (generation != AppSettings.CurrentGeneration)
            {
                return Recreate(settingsPath, leftoverSessionDocumentPath, sessionDirectory, SettingsFileReset.Outdated, out reset);
            }

            var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
            if (settings is null || settings.SettingsGeneration != AppSettings.CurrentGeneration)
            {
                return Recreate(settingsPath, leftoverSessionDocumentPath, sessionDirectory, SettingsFileReset.Invalid, out reset);
            }

            settings.EnsureSpeakerPresets();
            return settings;
        }
        catch
        {
            return Recreate(settingsPath, leftoverSessionDocumentPath, sessionDirectory, SettingsFileReset.Invalid, out reset);
        }
    }

    public static void Write(string settingsPath, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath) ?? ".");
        settings.SettingsGeneration = AppSettings.CurrentGeneration;
        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        File.WriteAllText(settingsPath, json);
    }

    internal static bool TryReadGeneration(string json, out int generation)
    {
        generation = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty(nameof(AppSettings.SettingsGeneration), out var property))
            {
                return true;
            }

            return property.TryGetInt32(out generation);
        }
        catch
        {
            return false;
        }
    }

    private static AppSettings Recreate(
        string settingsPath,
        string? leftoverSessionDocumentPath,
        string? sessionDirectory,
        SettingsFileReset reason,
        out SettingsFileReset reset)
    {
        reset = reason;
        TryDelete(settingsPath);
        TryDelete(leftoverSessionDocumentPath);
        if (!string.IsNullOrWhiteSpace(sessionDirectory))
        {
            DocumentSessionStore.RemoveOrphanSessionFiles(sessionDirectory, []);
        }

        var settings = AppSettings.CreateDefault();
        try
        {
            Write(settingsPath, settings);
        }
        catch
        {
            // 作り直し後の保存失敗は次の Save に任せる。
        }

        return settings;
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 古いファイルの削除失敗は致命的ではない。
        }
    }
}
