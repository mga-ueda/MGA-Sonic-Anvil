using System.Windows.Controls;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
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

internal sealed record AudioLayoutItem(ChannelLayout Layout, string Label)
{
    public AudioLayoutItem(ChannelLayout layout)
        : this(layout, layout.MenuLabel)
    {
    }

    public override string ToString() => Label;
}

/// <summary>設定と新規ダイアログで同じフォーマット一覧を出す。</summary>
internal static class AudioFormatComboFill
{
    public static void Fill(
        ComboBox rateCombo,
        ComboBox bitsCombo,
        ComboBox layoutCombo,
        DefaultAudioFormat.Spec current,
        IEnumerable<string>? visibleIds,
        IEnumerable<SpeakerPreset>? presets = null)
    {
        FillRates(rateCombo, current.SampleRate);
        FillBits(bitsCombo, current.BitsPerSample);
        FillLayouts(layoutCombo, current.Layout.Id, visibleIds, presets);
    }

    public static DefaultAudioFormat.Spec Read(
        ComboBox rateCombo,
        ComboBox bitsCombo,
        ComboBox layoutCombo,
        IEnumerable<string>? visibleIds,
        IEnumerable<SpeakerPreset>? presets = null)
    {
        var rate = rateCombo.SelectedItem is AudioRateItem rateItem
            ? rateItem.Hertz
            : DefaultAudioFormat.SampleRate;
        var bits = bitsCombo.SelectedItem is AudioBitDepthItem bitsItem
            ? bitsItem.Bits
            : DefaultAudioFormat.BitsPerSample;
        if (layoutCombo.SelectedItem is AudioLayoutItem layoutItem)
        {
            return new(
                DefaultAudioFormat.ClampSampleRate(rate),
                DefaultAudioFormat.ClampBitDepth(bits),
                layoutItem.Layout);
        }

        return DefaultAudioFormat.Resolve(rate, bits, DefaultAudioFormat.ChannelLayoutId, visibleIds);
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

    private static void FillLayouts(
        ComboBox combo,
        string currentId,
        IEnumerable<string>? visibleIds,
        IEnumerable<SpeakerPreset>? presets)
    {
        combo.Items.Clear();
        AudioLayoutItem? selected = null;
        AudioLayoutItem? stereo = null;
        var visible = new HashSet<string>(
            SpeakerPreset.NormalizeVisibleIds(visibleIds, presets),
            StringComparer.OrdinalIgnoreCase);

        foreach (var layout in DefaultAudioFormat.PickerLayouts(visible))
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

        if (presets is not null)
        {
            foreach (var preset in presets)
            {
                if (preset is null || !preset.IsCustom || !visible.Contains(preset.Id))
                {
                    continue;
                }

                var item = new AudioLayoutItem(preset.ToLayout(), preset.DisplayName());
                combo.Items.Add(item);
                if (preset.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = item;
                }
            }
        }

        combo.SelectedItem = selected ?? stereo ?? (combo.Items.Count > 0 ? combo.Items[0] : null);
    }
}
