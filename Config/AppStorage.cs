using System.IO;
using System.Text.Json;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Config;

/// <summary>
/// ユーザー設定の配置場所（Local AppData）。exe 横には書かない。
/// </summary>
internal static class AppStorage
{
    public const string CompanyFolderName = AppVersion.CompanyFolderName;
    public const string AppFolderName = AppVersion.ProductName;
    public const string SettingsFileName = "settings.json";
    public const string SessionDocumentFileName = "last-document.wav";

    public static AppSettings Settings { get; private set; } = new();

    public static void Initialize()
    {
        Directory.CreateDirectory(RootDirectory);
        Load();
    }

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CompanyFolderName,
        AppFolderName);

    public static string SettingsPath => Path.Combine(RootDirectory, SettingsFileName);

    public static string SessionDocumentPath => Path.Combine(RootDirectory, SessionDocumentFileName);

    public static string SessionDirectory => Path.Combine(RootDirectory, DocumentSessionStore.SessionDirectoryName);

    public static string SessionFilePath(string fileName)
    {
        var name = DocumentSessionStore.SanitizeSessionFileName(fileName)
            ?? throw new ArgumentException("Invalid session file name.", nameof(fileName));
        return Path.Combine(SessionDirectory, name);
    }

    public static string SessionSidecarPath(string fileName)
    {
        var name = DocumentSessionStore.SanitizeSidecarName(fileName)
            ?? throw new ArgumentException("Invalid session sidecar name.", nameof(fileName));
        return Path.Combine(SessionDirectory, name);
    }

    public static void ClearSessionDocument()
    {
        try
        {
            if (File.Exists(SessionDocumentPath))
            {
                File.Delete(SessionDocumentPath);
            }
        }
        catch
        {
            // 作業コピーの削除失敗は致命的ではない。
        }
    }

    public static void ReplaceSessionFiles(IReadOnlyCollection<string> keepFileNames)
    {
        Directory.CreateDirectory(SessionDirectory);
        DocumentSessionStore.RemoveOrphanSessionFiles(SessionDirectory, keepFileNames);
        ClearSessionDocument();
    }

    public static void ClearAllSessionAudio()
    {
        ClearSessionDocument();
        DocumentSessionStore.RemoveOrphanSessionFiles(SessionDirectory, []);
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Settings = new AppSettings();
                return;
            }

            var json = File.ReadAllText(SettingsPath);
            Settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings)
                ?? new AppSettings();
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var json = JsonSerializer.Serialize(Settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 設定保存失敗は致命的ではない。
        }
    }
}
