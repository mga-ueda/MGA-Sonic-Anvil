using System.IO;

namespace MgaSonicAnvil.UI;

internal static class AppEmbeddedResources
{
    private const string LogoName = "MgaSonicAnvil.Branding.MiyabiGameAudio.png";

    public static Stream? OpenLogo()
    {
        var assembly = typeof(AppEmbeddedResources).Assembly;
        return assembly.GetManifestResourceStream(LogoName);
    }
}
