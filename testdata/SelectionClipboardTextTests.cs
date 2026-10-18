using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SelectionClipboardTextTests
{
    [Fact]
    public void Format_AlwaysPrefixesFileName()
    {
        var previous = UiStrings.Language;
        try
        {
            (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[] items =
            [
                (@"D:\sounds\kick.wav", "kick.wav", new WaveSelection(48000, 96000), 48000),
            ];

            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal(
                "kick.wav\t00:01.000 ～ 00:02.000",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FileName, asSamples: false));

            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal(
                "kick.wav\t00:01.000 - 00:02.000",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FileName, asSamples: false));
            Assert.Equal(
                "kick.wav\t48000 - 96000",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FileName, asSamples: true));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Format_FullPath_UsesAbsolutePath()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var source = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sel-copy-test.wav"));
            (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[] items =
            [
                (source, "sel-copy-test.wav", new WaveSelection(0, 48000), 48000),
            ];

            Assert.Equal(
                source + "\t00:00.000 ～ 00:01.000",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FullPath, asSamples: false));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Format_MultipleSelections_OneLineEach()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[] items =
            [
                (@"C:\a.wav", "a.wav", new WaveSelection(0, 48000), 48000),
                (@"C:\b.wav", "b.wav", new WaveSelection(24000, 72000), 48000),
            ];

            Assert.Equal(
                "a.wav\t00:00.000 ～ 00:01.000\r\nb.wav\t00:00.500 ～ 00:01.500",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FileName, asSamples: false));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Format_SkipsEmptySelections()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.English);
            (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[] items =
            [
                ("empty.wav", "empty.wav", WaveSelection.Empty, 48000),
                ("hit.wav", "hit.wav", new WaveSelection(48000, 96000), 48000),
            ];

            Assert.Equal(
                "hit.wav\t00:01.000 - 00:02.000",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FileName, asSamples: false));
            Assert.True(SelectionClipboardText.CanCopy(items));
            Assert.False(SelectionClipboardText.CanCopy(
            [
                (null, "x.wav", WaveSelection.Empty, 48000),
            ]));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Format_UntitledFallsBackToDisplayName()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[] items =
            [
                (null, "無題", new WaveSelection(0, 48000), 48000),
            ];

            Assert.Equal(
                "無題\t00:00.000 ～ 00:01.000",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FileName, asSamples: false));
            Assert.Equal(
                "無題\t00:00.000 ～ 00:01.000",
                SelectionClipboardText.Format(items, SelectionClipboardPathKind.FullPath, asSamples: false));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Format_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(
            string.Empty,
            SelectionClipboardText.Format([], SelectionClipboardPathKind.FileName, asSamples: false));
        Assert.False(SelectionClipboardText.CanCopy([]));
    }

    [Fact]
    public void RangeSeparator_DependsOnLanguage()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal(" ～ ", SelectionClipboardText.RangeSeparator);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal(" - ", SelectionClipboardText.RangeSeparator);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }
}
