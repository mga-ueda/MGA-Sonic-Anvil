using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand? ConvertSampleRate(
        AudioDocument document,
        int destRate,
        IProgress<double>? progress = null)
    {
        if (!FormatConvert.IsValidSampleRate(destRate) || destRate == document.SampleRate)
        {
            return null;
        }

        var before = FormatSnapshot.Capture(document);
        var samples = document.MaterializeFormat(destRate, document.BitsPerSample, document.Channels, progress);
        var destFrames = samples.Length / document.Channels;
        var after = new FormatSnapshot(
            samples,
            destRate,
            document.Channels,
            document.BitsPerSample,
            before.OriginSamples,
            before.OriginSampleRate,
            before.OriginChannels,
            before.OriginBitsPerSample,
            WaveSelection.Empty,
            FormatConvert.ScaleSelection(document.SampleLoop, document.SampleRate, destRate, destFrames),
            ScaleRegions(document, destRate, destFrames),
            FormatConvert.ScaleFrame(document.CursorFrame, document.SampleRate, destRate, destFrames),
            FormatConvert.ScaleMarkers(document.SnapshotMarkers(), document.SampleRate, destRate, destFrames));
        var command = new ConvertFormatCommand(
            "Convert Sample Rate",
            $"{UiStrings.EditHistoryName("Convert Sample Rate")}  {document.SampleRate}→{destRate} Hz",
            before,
            after);
        command.Replay = target => ConvertSampleRate(target, destRate);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.ConvertRate,
            SourceRate = document.SampleRate,
            Value = destRate,
        };
        return command;
    }

    private static WaveRegion[] ScaleRegions(AudioDocument document, int destRate, long destFrames)
    {
        var source = document.SnapshotRegions();
        if (source.Length == 0)
        {
            return source;
        }

        var scaled = new WaveRegion[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            scaled[i] = new WaveRegion(
                FormatConvert.ScaleSelection(source[i].Range, document.SampleRate, destRate, destFrames),
                source[i].Name);
        }

        return scaled;
    }

    public static IEditCommand? ConvertBitDepth(AudioDocument document, int bits)
    {
        if (!FormatConvert.IsValidBitDepth(bits) || bits == document.BitsPerSample)
        {
            return null;
        }

        var before = FormatSnapshot.Capture(document);
        var samples = document.MaterializeFormat(document.SampleRate, bits, document.Channels);
        var after = before with { Samples = samples, BitsPerSample = bits, Selection = WaveSelection.Empty };
        var command = new ConvertFormatCommand(
            "Convert Bit Depth",
            $"{UiStrings.EditHistoryName("Convert Bit Depth")}  {document.BitsPerSample}→{bits} bit",
            before,
            after);
        command.Replay = target => ConvertBitDepth(target, bits);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.ConvertBits,
            SourceRate = document.SampleRate,
            Value = bits,
        };
        return command;
    }

    public static IEditCommand? ConvertChannels(AudioDocument document, int destChannels)
    {
        if (destChannels < 1 || destChannels == document.Channels)
        {
            return null;
        }

        var before = FormatSnapshot.Capture(document);
        var samples = document.MaterializeFormat(document.SampleRate, document.BitsPerSample, destChannels);
        var after = before with { Samples = samples, Channels = destChannels, Selection = WaveSelection.Empty };
        var command = new ConvertFormatCommand(
            "Convert Channels",
            $"{UiStrings.EditHistoryName("Convert Channels")}  {document.Channels}→{destChannels} ch",
            before,
            after);
        command.Replay = target => ConvertChannels(target, destChannels);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.ConvertChannels,
            SourceRate = document.SampleRate,
            Value = destChannels,
        };
        return command;
    }

}
