using System.IO;
using System.Linq;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// 実 VST3 の in-process ロードはプラグインによってプロセスを落とす。
/// 既定のテスト実行では走らせず、手動確認用に残す。
/// </summary>
public sealed class Vst3ProcessorSmokeTests
{
    [Fact(Skip = "実 VST3 の in-process ロードは testhost を落とすことがある。手動で確認する。")]
    public void Manual_TryCreateAndProcess_FirstAvailablePlugin()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "VST3");
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var path in Directory.GetFileSystemEntries(root, "*.vst3")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(3))
        {
            if (Vst3FactoryProbe.IsWaveShellPath(path))
            {
                continue;
            }

            var slot = new VstInsertSlot
            {
                PluginPath = path,
                DisplayName = Path.GetFileNameWithoutExtension(path),
            };
            using var instance = Vst3ProcessorInstance.TryCreate(slot, 48000, 256, 2);
            if (instance is null || instance.Faulted)
            {
                continue;
            }

            var buffer = new float[512];
            Array.Fill(buffer, 0.05f);
            Assert.True(instance.Process(buffer, 0, buffer.Length, 2));
            return;
        }
    }
}
