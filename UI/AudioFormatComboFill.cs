using System.Windows.Controls;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed record AudioRateItem(int Hertz)
{
    public override string ToString() => UiStrings.FormatSampleRate(Hertz);
}

internal sealed record AudioBitDepthItem(int Bits)
{
    public override string ToString() => UiStrings.FormatBitDepth(Bits);
}

internal sealed record AudioLayoutItem(ChannelLayout Layout)
{
    public override string ToString() => Layout.MenuLabel;
}

/// <summary>設定と新規ダイアログで同じフォーマット一覧を出す。</summary>
internal static class AudioFormatComboFill
{
    public static void Fill(
        ComboBox rateCombo,
        ComboBox bitsCombo,
        ComboBox layoutCombo,
        DefaultAudioFormat.Spec current,
        IEnumerable<string>? visibleIds)
    {
        FillRates(rateCombo, current.SampleRate);
        FillBits(bitsCombo, current.BitsPerSample);
        FillLayouts(layoutCombo, current.Layout.Id, visibleIds);
    }

    public static DefaultAudioFormat.Spec Read(
        ComboBox rateCombo,
        ComboBox bitsCombo,
        ComboBox layoutCombo,
        IEnumerable<string>? visibleIds)
    {
        var rate = rateCombo.SelectedItem is AudioRateItem rateItem
            ? rateItem.Hertz
            : DefaultAudioFormat.SampleRate;
        var bits = bitsCombo.SelectedItem is AudioBitDepthItem bitsItem
            ? bitsItem.Bits
            : DefaultAudioFormat.BitsPerSample;
        var layoutId = layoutCombo.SelectedItem is AudioLayoutItem layoutItem
            ? layoutItem.Layout.Id
            : DefaultAudioFormat.ChannelLayoutId;
        return DefaultAudioFormat.Resolve(rate, bits, layoutId, visibleIds);
    }

    private static void FillRates(ComboBox combo, int current)
    {
        combo.Items.Clear();
        AudioRateItem? selected = null;
        var seen = new HashSet<int>(FormatConvert.SampleRates);
        foreach (var rate in FormatConvert.SampleRates)
        {
            var item = new AudioRateItem(rate);
            combo.Items.Add(item);
            if (rate == current)
            {
                selected = item;
            }
        }

        if (!seen.Contains(current))
        {
            selected = new AudioRateItem(current);
            combo.Items.Add(selected);
        }

        combo.SelectedItem = selected ?? combo.Items[0];
    }

    private static void FillBits(ComboBox combo, int current)
    {
        combo.Items.Clear();
        AudioBitDepthItem? selected = null;
        AudioBitDepthItem? fallback = null;
        foreach (var bits in DefaultAudioFormat.BitDepths)
        {
            var item = new AudioBitDepthItem(bits);
            combo.Items.Add(item);
            if (bits == current)
            {
                selected = item;
            }

            if (bits == DefaultAudioFormat.BitsPerSample)
            {
                fallback = item;
            }
        }

        combo.SelectedItem = selected ?? fallback ?? combo.Items[^1];
    }

    private static void FillLayouts(ComboBox combo, string currentId, IEnumerable<string>? visibleIds)
    {
        combo.Items.Clear();
        AudioLayoutItem? selected = null;
        AudioLayoutItem? stereo = null;
        foreach (var layout in DefaultAudioFormat.PickerLayouts(visibleIds))
        {
            var item = new AudioLayoutItem(layout);
            combo.Items.Add(item);
            if (layout.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase))
            {
                selected = item;
            }

            if (layout.Id.Equals(DefaultAudioFormat.ChannelLayoutId, StringComparison.OrdinalIgnoreCase))
            {
                stereo = item;
            }
        }

        combo.SelectedItem = selected ?? stereo ?? combo.Items[0];
    }
}
