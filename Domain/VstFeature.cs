namespace MgaSonicAnvil.Domain;

/// <summary>VST3 対応の表示可否。Release では隠し、他修正の出荷を止めない。</summary>
internal static class VstFeature
{
#if DEBUG
    public const bool IsEnabled = true;
#else
    public const bool IsEnabled = false;
#endif
}
