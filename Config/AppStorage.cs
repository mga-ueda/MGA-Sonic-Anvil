using System.IO;
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

    public static AppSettings Settings { get; private set; } = AppSettings.CreateDefault();

    /// <summary>起動時に設定を作り直した理由。通知後は <see cref="AcknowledgeSettingsReset"/> で消す。</summary>
    public static SettingsFileReset SettingsReset { get; private set; }

    public static void Initialize()
    {
        Directory.CreateDirectory(RootDirectory);
        Load();
    }

    public static void AcknowledgeSettingsReset() => SettingsReset = SettingsFileReset.None;

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
        Settings = AppSettingsFile.Load(
            SettingsPath,
            SessionDocumentPath,
            SessionDirectory,
            out var reset);
        SettingsReset = reset;
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            AppSettingsFile.Write(SettingsPath, Settings);
        }
        catch
        {
            // 設定保存失敗は致命的ではない。
        }
    }
}
