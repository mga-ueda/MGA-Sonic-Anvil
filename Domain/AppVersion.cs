using System.Globalization;
using System.Reflection;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// 表示名・GitHub・版の単一ソース。csproj の Version と揃える。
/// </summary>
internal static class AppVersion
{
    public const string ProductName = "MGA Sonic Anvil";
    public const string GitHubOwner = "mga-ueda";
    public const string GitHubRepo = "MGA-Sonic-Anvil";
    public const string RepositoryUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepo;
    public const string CompanyName = "MIYABI GAME AUDIO INC.";
    public const string CompanyUrl = "https://www.miyabi-ga.co.jp/";
    public const string CompanyFolderName = "MGA";

    private static readonly Lazy<string> CurrentLazy = new(ReadCurrent);

    public static string Current => CurrentLazy.Value;

    public static string FormTitle => ProductName + " - Version " + Current;

    private static string ReadCurrent()
    {
        foreach (var meta in Assembly.GetExecutingAssembly()
                     .GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (meta.Key.Equals("AppVersion", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(meta.Value))
            {
                return meta.Value.Trim();
            }
        }

        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (assemblyVersion is not null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}");
        }

        return "0.1.0";
    }
}
