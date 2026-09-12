using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// 表示名・GitHub・版の単一ソース。csproj の Version と揃える。
/// </summary>
internal static partial class AppVersion
{
    public const string ProductName = "MGA Sonic Anvil";
    public const string GitHubOwner = "mga-ueda";
    public const string GitHubRepo = "MGA-Sonic-Anvil";
    public const string RepositoryUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepo;
    public const string CompanyName = "MIYABI GAME AUDIO INC.";
    public const string CompanyUrl = "https://www.miyabi-ga.co.jp/";
    public const string CompanyFolderName = "MGA";
    public const string ReleasesApiUrl =
        "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases";

    /// <summary>GitHub Pages 上のユーザーマニュアル（docs/）。</summary>
    public const string ManualSiteUrl =
        "https://" + GitHubOwner + ".github.io/" + GitHubRepo + "/";
    public const string ManualJaUrl = ManualSiteUrl + "manual.ja.html";
    public const string ManualEnUrl = ManualSiteUrl + "manual.en.html";

    private static readonly Lazy<string> CurrentLazy = new(ReadCurrent);

    public static string Current => CurrentLazy.Value;

    public static string FormTitle => ProductName + " - Version " + Current;

    /// <summary>アクティブファイルがあるとき、タイトル末尾に <c>[ フルパス ]</c> を付ける。</summary>
    public static string FormTitleWithFile(string? sourcePath)
    {
        var path = (sourcePath ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            return FormTitle;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }

        return FormTitle + " [ " + path + " ]";
    }

    /// <summary>
    /// <paramref name="remoteSemVer"/> がローカルより新しいとき true。
    /// パース不能なときは比較せず false。
    /// </summary>
    public static bool IsRemoteNewer(string? remoteSemVer) =>
        CompareSemVer(remoteSemVer, Current) > 0;

    /// <summary>
    /// SemVer 風文字列を比較する。left &gt; right なら正、等しいなら 0、left &lt; right なら負。
    /// パース不能な側は「より古い」扱い（比較不能時は 0）。
    /// </summary>
    public static int CompareSemVer(string? left, string? right)
    {
        var leftOk = TryParse(left, out var leftParsed);
        var rightOk = TryParse(right, out var rightParsed);
        if (!leftOk && !rightOk)
        {
            return 0;
        }

        if (!leftOk)
        {
            return -1;
        }

        if (!rightOk)
        {
            return 1;
        }

        return Compare(leftParsed, rightParsed);
    }

    /// <summary>タグや版文字列から先頭の <c>v</c> を除いた SemVer 風文字列を返す。</summary>
    public static string NormalizeTag(string? tagOrVersion)
    {
        var text = (tagOrVersion ?? string.Empty).Trim();
        if (text.Length >= 2
            && (text[0] is 'v' or 'V')
            && char.IsDigit(text[1]))
        {
            return text[1..];
        }

        return text;
    }

    private static string ReadCurrent()
    {
        foreach (var meta in Assembly.GetExecutingAssembly()
                     .GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (meta.Key.Equals("AppVersion", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(meta.Value))
            {
                return NormalizeTag(meta.Value);
            }
        }

        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            var value = plus >= 0 ? informational[..plus] : informational;
            var normalized = NormalizeTag(value);
            if (TryParse(normalized, out _))
            {
                return normalized;
            }
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (assemblyVersion is not null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}");
        }

        return "0.0.1-beta";
    }

    private static bool TryParse(string? text, out ParsedVersion parsed)
    {
        parsed = default;
        var normalized = NormalizeTag(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        var match = SemVerRegex().Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        parsed = new ParsedVersion(
            int.Parse(match.Groups["maj"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["pat"].Value, CultureInfo.InvariantCulture),
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
        return true;
    }

    [GeneratedRegex(
        @"^(?<maj>\d+)\.(?<min>\d+)\.(?<pat>\d+)(?:-(?<pre>[0-9A-Za-z\.-]+))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemVerRegex();

    /// <summary>SemVer 風: 同じ数値なら、プレリリース無しが新しい。</summary>
    private static int Compare(ParsedVersion left, ParsedVersion right)
    {
        var core = left.Major.CompareTo(right.Major);
        if (core != 0)
        {
            return core;
        }

        core = left.Minor.CompareTo(right.Minor);
        if (core != 0)
        {
            return core;
        }

        core = left.Patch.CompareTo(right.Patch);
        if (core != 0)
        {
            return core;
        }

        var leftPre = left.Prerelease;
        var rightPre = right.Prerelease;
        if (leftPre is null && rightPre is null)
        {
            return 0;
        }

        if (leftPre is null)
        {
            return 1;
        }

        if (rightPre is null)
        {
            return -1;
        }

        return string.Compare(leftPre, rightPre, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ParsedVersion(
        int Major,
        int Minor,
        int Patch,
        string? Prerelease);
}
