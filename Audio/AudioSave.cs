namespace MgaSonicAnvil.Audio;

/// <summary>上書きできる拡張子と、複数の名前を付けて保存の Wave パス。</summary>
internal static class AudioSave
{
    public static bool CanOverwrite(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (AudioCodec.DetectKind(path) is AudioFileKind.Aiff or AudioFileKind.M4a)
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return AudioCodec.SaveExtensions.Any(item =>
            item.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public static string[] PlanFolderWavePaths(
        IReadOnlyList<(string? SourcePath, string DisplayName)> files,
        string folder,
        ISet<string>? reserved = null)
    {
        reserved ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new string[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            paths[i] = AudioExport.UniqueInDirectory(
                folder,
                AudioExport.SuggestBaseName(files[i].SourcePath, files[i].DisplayName),
                ".wav",
                reserved);
        }

        return paths;
    }
}
