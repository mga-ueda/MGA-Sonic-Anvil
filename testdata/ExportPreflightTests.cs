using MgaSonicAnvil.Wwise;
using System.IO;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ExportPreflightTests
{
    [Fact]
    public void IsUnderDirectory_AcceptsRootAndChild()
    {
        var root = Path.Combine(Path.GetTempPath(), "anvil-originals-root");
        var child = Path.Combine(root, "SFX");
        Directory.CreateDirectory(child);
        try
        {
            Assert.True(ExportPreflight.IsUnderDirectory(root, root));
            Assert.True(ExportPreflight.IsUnderDirectory(child, root));
            Assert.False(ExportPreflight.IsUnderDirectory(root + "2", root));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // 後始末失敗は無視。
            }
        }
    }

    [Fact]
    public void Evaluate_FailsWithoutDocument()
    {
        var result = ExportPreflight.Evaluate(@"C:\tmp", waapi: null, hasDocument: false);
        Assert.False(result.CanExport);
    }

    [Fact]
    public void Evaluate_FailsWhenDisconnected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "anvil-preflight-out");
        Directory.CreateDirectory(dir);
        try
        {
            var result = ExportPreflight.Evaluate(
                dir,
                new WaapiProbeResult { Ok = false, Message = "ng" },
                hasDocument: true);
            Assert.False(result.CanExport);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
