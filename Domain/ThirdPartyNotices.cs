using System.IO;
using System.Text;

namespace MgaSonicAnvil.Domain;

/// <summary>
/// THIRD_PARTY_NOTICES.md。単一 exe 配布では埋め込みリソースを読み、開発／テストでは隣のファイルも使える。
/// </summary>
internal static class ThirdPartyNotices
{
    public const string FileName = "THIRD_PARTY_NOTICES.md";

    /// <summary>アセンブリ埋め込みの論理名。</summary>
    public const string EmbeddedResourceName = "MgaSonicAnvil.THIRD_PARTY_NOTICES.md";

    public static string ResolvePath() =>
        Path.Combine(AppContext.BaseDirectory, FileName);

    public static bool TryRead(out string text)
    {
        try
        {
            var path = ResolvePath();
            if (File.Exists(path))
            {
                text = File.ReadAllText(path, Encoding.UTF8);
                return text.Length > 0;
            }

            return TryReadEmbedded(out text);
        }
        catch (IOException)
        {
            return TryReadEmbedded(out text);
        }
        catch (UnauthorizedAccessException)
        {
            return TryReadEmbedded(out text);
        }
    }

    private static bool TryReadEmbedded(out string text)
    {
        try
        {
            var assembly = typeof(ThirdPartyNotices).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null)
            {
                text = string.Empty;
                return false;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
            return text.Length > 0;
        }
        catch (IOException)
        {
            text = string.Empty;
            return false;
        }
    }
}
