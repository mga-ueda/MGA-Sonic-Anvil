using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Config;

/// <summary>
/// Ctrl+F5 用。Explorer ルート配下を、プレイリストに一度載せたのと同じ内容でキャッシュする。
/// タグ・波形棒・ジャケット。プレイリスト UI には載せない。重いので UI はすりガラスで止める。
/// </summary>
internal static class LibraryCatalogWarmup
{
    public static void Run(
        IReadOnlyList<string> roots,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        LibraryPlaylistDocuments.ApplyFromSettings(AppStorage.Settings);
        progress?.Report(0);
        LibraryCatalog.ReconcileRoots(roots);

        var files = CollectFiles(roots, cancellationToken);
        if (files.Count == 0)
        {
            progress?.Report(1);
            return;
        }

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WarmupFile(files[i], cancellationToken);
            progress?.Report((i + 1) / (double)files.Count);
        }
    }

    private static List<string> CollectFiles(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(root.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!Directory.Exists(full))
            {
                continue;
            }

            string[] found;
            try
            {
                found = AudioCodec.CollectPlayerOpenableFromDirectory(full, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in found)
            {
                if (seen.Add(file))
                {
                    files.Add(file);
                }
            }
        }

        return files;
    }

    private static void WarmupFile(string path, CancellationToken cancellationToken)
    {
        AudioFileTags tags;
        if (LibraryCatalog.TryGetFreshTags(path, out var cached))
        {
            tags = cached;
        }
        else if (AudioTagProbe.TryRead(path, out tags))
        {
            LibraryCatalog.UpsertTags(path, tags);
        }
        else
        {
            tags = AudioFileTags.Empty;
        }

        if (LibraryPlaylistCache.RequiresJacketForPath(path))
        {
            WarmupArtwork(path, tags);
        }

        if (LibraryPlaylistCache.RequiresWaveformForPath(path))
        {
            WarmupWaveforms(path, cancellationToken);
        }
    }

    private static void WarmupArtwork(string path, AudioFileTags tags)
    {
        if (LibraryMediaCache.TryGetArtworkProbe(path, out _))
        {
            return;
        }

        var kind = AudioCodec.DetectKind(path);
        if (!LibraryArtworkScan.CanRead(kind))
        {
            LibraryMediaCache.PutNoArtwork(path);
            return;
        }

        // 埋め込み音声はタグで無しと分かれば読まない。
        if (tags.Probed
            && !tags.HasArtwork
            && LibraryArtworkScan.IsEmbeddedKind(kind))
        {
            LibraryMediaCache.PutNoArtwork(path);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = ReadArtwork(kind, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LibraryMediaCache.PutNoArtwork(path);
            return;
        }

        if (bytes.Length > 0)
        {
            LibraryMediaCache.PutArtwork(path, bytes);
        }
        else
        {
            LibraryMediaCache.PutNoArtwork(path);
        }
    }

    private static void WarmupWaveforms(string path, CancellationToken cancellationToken)
    {
        foreach (var barCount in ResolveBarCountsForPath(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LibraryMediaCache.TryGetWaveform(path, barCount, out _))
            {
                continue;
            }

            float[] bars;
            try
            {
                bars = PeakPyramid.BuildPlaylistBarsFromPath(path, barCount, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or NotSupportedException)
            {
                bars = new float[barCount];
            }

            LibraryMediaCache.PutWaveform(path, barCount, bars);
        }
    }

    private static byte[] ReadArtwork(AudioFileKind kind, string path)
    {
        if (LibraryArtworkScan.IsPdfKind(kind))
        {
            return LibraryPdfPages.RenderPngAsync(path, 0, VideoArtwork.Edge)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult() ?? [];
        }

        if (kind == AudioFileKind.M4a)
        {
            return M4aArtwork.TryRead(path, out var art) ? art : [];
        }

        if (LibraryArtworkScan.IsVideoKind(kind))
        {
            return VideoArtwork.TryRead(path, out var frame) ? frame : [];
        }

        return Id3Artwork.TryRead(path, out var mp3) ? mp3 : [];
    }

    private static int[] ResolveBarCountsForPath(string path)
    {
        var settings = AppStorage.Settings;
        var key = LibraryColumnFilter.KindFromFileName(Path.GetFileName(path) ?? path);
        if (key is not null && LibraryColumnFilter.IsTagFamilyKind(key))
        {
            return [LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeMp3())];
        }

        if (key is not null && LibraryColumnFilter.IsPcmFamilyKind(key))
        {
            return [LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeWave())];
        }

        var wave = LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeWave());
        var mp3 = LibraryPlaylistWaveformSizes.BarCount(settings.ResolvedLibraryPlaylistWaveformSizeMp3());
        return wave == mp3 ? [wave] : [wave, mp3];
    }
}
