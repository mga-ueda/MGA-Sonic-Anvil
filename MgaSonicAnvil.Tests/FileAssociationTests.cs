using System.IO;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class FileAssociationTests
{
    private const string TestExtension = ".mgaanvilx";

    [Theory]
    [InlineData(".wav", "MgaSonicAnvil.wav")]
    [InlineData("WAV", "MgaSonicAnvil.wav")]
    [InlineData(".wave", "MgaSonicAnvil.wave")]
    [InlineData(".aiff", "MgaSonicAnvil.aiff")]
    [InlineData("mp3", "MgaSonicAnvil.mp3")]
    public void ProgIdFor_NormalizesExtension(string ext, string expected) =>
        Assert.Equal(expected, FileAssociations.ProgIdFor(ext));

    [Fact]
    public void FormatLabel_UsesKindAndExtension()
    {
        Assert.Equal("Wave (.wav)", FileAssociations.FormatLabel(".wav"));
        Assert.Equal("Wave (.wave)", FileAssociations.FormatLabel(".wave"));
        Assert.Equal("AIFF (.aif)", FileAssociations.FormatLabel(".aif"));
        Assert.Equal("AIFF (.aiff)", FileAssociations.FormatLabel(".AIFF"));
        Assert.Equal("MP3 (.mp3)", FileAssociations.FormatLabel("mp3"));
    }

    [Fact]
    public void Extensions_MatchOpenableTypes() =>
        Assert.Equal(AudioCodec.OpenExtensions, FileAssociations.Extensions);

    [Fact]
    public void BuildOpenCommand_QuotesExeAndPlaceholder() =>
        Assert.Equal(
            "\"C:\\Apps\\MGA Sonic Anvil.exe\" \"%1\"",
            FileAssociations.BuildOpenCommand(@"C:\Apps\MGA Sonic Anvil.exe"));

    [Theory]
    [InlineData("\"C:\\Apps\\MGA Sonic Anvil.exe\" \"%1\"", @"C:\Apps\MGA Sonic Anvil.exe")]
    [InlineData(@"C:\Apps\anvil.exe %1", @"C:\Apps\anvil.exe")]
    public void TryGetCommandExePath_ReadsFirstToken(string command, string expected)
    {
        Assert.True(FileAssociations.TryGetCommandExePath(command, out var path));
        Assert.Equal(expected, path);
    }

    [Fact]
    public void CommandTargets_IgnoresCaseAndNormalizes()
    {
        var exe = Path.GetFullPath(@"C:\Apps\MGA Sonic Anvil.exe");
        Assert.True(FileAssociations.CommandTargets($"\"{exe.ToUpperInvariant()}\" \"%1\"", exe));
        Assert.False(FileAssociations.CommandTargets("\"C:\\Other\\app.exe\" \"%1\"", exe));
        Assert.False(FileAssociations.CommandTargets(null, exe));
    }

    [Fact]
    public void IsOurProgId_MatchesPrefixAndExactExtension()
    {
        Assert.True(FileAssociations.IsOurProgId("MgaSonicAnvil.wav"));
        Assert.True(FileAssociations.IsOurProgId("mgaSonicAnvil.WAV", ".wav"));
        Assert.False(FileAssociations.IsOurProgId("MgaSonicAnvil.wav", ".mp3"));
        Assert.False(FileAssociations.IsOurProgId("WMP11.AssocFile.WAV"));
        Assert.False(FileAssociations.IsOurProgId(null));
    }

    [Fact]
    public void SetAssociated_RoundTripsCurrentExe()
    {
        try
        {
            FileAssociations.SetAssociated(TestExtension, false);
            Assert.False(FileAssociations.IsAssociated(TestExtension));

            FileAssociations.SetAssociated(TestExtension, true);
            Assert.True(FileAssociations.IsAssociated(TestExtension));
            Assert.Equal(
                FileAssociations.ProgIdFor(TestExtension),
                Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{TestExtension}")?.GetValue(null) as string);

            FileAssociations.SetAssociated(TestExtension, false);
            Assert.False(FileAssociations.IsAssociated(TestExtension));
        }
        finally
        {
            FileAssociations.SetAssociated(TestExtension, false);
            CleanupTestKeys();
        }
    }

    private static void CleanupTestKeys()
    {
        var progId = FileAssociations.ProgIdFor(TestExtension);
        TryDelete(@"Software\Classes\" + progId);
        TryDelete(@"Software\Classes\" + TestExtension);
        TryDelete(@"Software\MGA\MGA Sonic Anvil\FileAssociations\" + TestExtension);
        TryDelete(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + TestExtension);
        var exeName = Path.GetFileName(FileAssociations.ResolveExePath() ?? "testhost.exe");
        using var types = Registry.CurrentUser.OpenSubKey(
            $@"Software\Classes\Applications\{exeName}\SupportedTypes",
            writable: true);
        types?.DeleteValue(TestExtension, throwOnMissingValue: false);
        using var caps = Registry.CurrentUser.OpenSubKey(
            @"Software\MGA\MGA Sonic Anvil\Capabilities\FileAssociations",
            writable: true);
        caps?.DeleteValue(TestExtension, throwOnMissingValue: false);
    }

    private static void TryDelete(string relativePath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
        }
        catch (IOException)
        {
        }
    }
}
