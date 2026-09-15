using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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
        Assert.False(FileAssociations.IsOurProgId("Applications\\MGA Sonic Anvil.exe"));
        Assert.False(FileAssociations.IsOurProgId(null));
    }

    [Fact]
    public void IsApplicationsProgId_MatchesThisExeName()
    {
        var exe = @"C:\Apps\MGA Sonic Anvil.exe";
        Assert.True(FileAssociations.IsApplicationsProgId(@"Applications\MGA Sonic Anvil.exe", exe));
        Assert.True(FileAssociations.IsApplicationsProgId(@"applications\mga sonic anvil.exe", exe));
        Assert.True(FileAssociations.IsApplicationsProgIdName(
            @"Applications\MGA Sonic Anvil.exe",
            "MGA Sonic Anvil.exe"));
        Assert.False(FileAssociations.IsApplicationsProgId(@"Applications\Other.exe", exe));
        Assert.False(FileAssociations.IsApplicationsProgId("MgaSonicAnvil.wav", exe));
        Assert.False(FileAssociations.IsApplicationsProgId(null, exe));
    }

    [Fact]
    public void TargetsThisApp_AcceptsSameFileName()
    {
        var running = @"C:\dev\bin\MGA Sonic Anvil.exe";
        Assert.True(FileAssociations.TargetsThisApp(
            "\"V:\\Program Files\\MGA Sonic Anvil\\MGA Sonic Anvil.exe\" \"%1\"",
            running));
        Assert.True(FileAssociations.TargetsThisApp(
            @"V:\Program Files\MGA Sonic Anvil\MGA Sonic Anvil.exe",
            running));
        Assert.False(FileAssociations.TargetsThisApp(
            "\"C:\\Program Files\\Windows Media Player\\wmplayer.exe\" \"%1\"",
            running));
    }

    [Fact]
    public void UserChoiceHash_MatchesPublishedVector()
    {
        const long fileTime = 0x01d4d98267246000;
        Assert.Equal(
            "PCCqEmkvW2Y=",
            UserChoiceHash.Compute(
                ".3g2",
                "S-1-5-21-819709642-920330688-1657285119-500",
                "WMP11.AssocFile.3G2",
                fileTime));
    }

    [Fact]
    public void IsRuntimeHost_DetectsDotnetAndTestHost()
    {
        Assert.True(FileAssociations.IsRuntimeHost(@"C:\Program Files\dotnet\dotnet.exe"));
        Assert.True(FileAssociations.IsRuntimeHost(@"C:\tmp\testhost.exe"));
        Assert.False(FileAssociations.IsRuntimeHost(@"C:\Apps\MGA Sonic Anvil.exe"));
    }

    [Fact]
    public void SetAssociated_WritesUserChoiceForCurrentExe()
    {
        try
        {
            FileAssociations.SetAssociated(TestExtension, true);
            using var choice = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{TestExtension}\UserChoice");
            Assert.NotNull(choice);
            Assert.Equal(
                FileAssociations.ProgIdFor(TestExtension),
                choice.GetValue("ProgId") as string);
            Assert.False(string.IsNullOrWhiteSpace(choice.GetValue("Hash") as string));
        }
        finally
        {
            FileAssociations.SetAssociated(TestExtension, false);
            CleanupTestKeys();
        }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetAssociated_ClearsLockedUserChoice(bool applicationsProgId)
    {
        var progId = applicationsProgId
            ? @"Applications\" + FileAssociations.CurrentExeFileName()
            : FileAssociations.ProgIdFor(TestExtension);
        try
        {
            FileAssociations.SetAssociated(TestExtension, false);
            WriteLockedUserChoice(TestExtension, progId);
            Assert.True(FileAssociations.IsAssociated(TestExtension));

            FileAssociations.SetAssociated(TestExtension, false);
            Assert.False(FileAssociations.IsAssociated(TestExtension));
            Assert.Null(
                Registry.CurrentUser.OpenSubKey(
                    $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{TestExtension}\UserChoice"));
        }
        finally
        {
            FileAssociations.SetAssociated(TestExtension, false);
            CleanupTestKeys();
        }
    }

    private static void WriteLockedUserChoice(string extension, string progId)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice");
        key.SetValue("ProgId", progId);
        key.SetValue("Hash", "test");
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID is missing.");
        var security = key.GetAccessControl();
        security.AddAccessRule(new RegistryAccessRule(
            user,
            RegistryRights.SetValue,
            AccessControlType.Deny));
        key.SetAccessControl(security);
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
