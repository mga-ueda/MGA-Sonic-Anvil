using System.IO;
using System.Text;

namespace MgaSonicAnvil.Domain;

/// <summary>同梱の THIRD_PARTY_NOTICES.md。</summary>
internal static class ThirdPartyNotices
{
    public const string FileName = "THIRD_PARTY_NOTICES.md";

    public static string ResolvePath() =>
        Path.Combine(AppContext.BaseDirectory, FileName);

    public static bool TryRead(out string text)
    {
        try
        {
            var path = ResolvePath();
            if (!File.Exists(path))
            {
                text = string.Empty;
                return false;
            }

            text = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }
        catch (IOException)
        {
            text = string.Empty;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            text = string.Empty;
            return false;
        }
    }
}
