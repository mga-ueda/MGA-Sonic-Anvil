using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class HistorySessionTests
{
    [Fact]
    public void TryExportImport_RestoresUndoStackAndAudio()
    {
        var document = MakeConstant(32, 1f);
        var original = document.Interleaved[0];
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(0, 32), FadeShape.Linear));
        history.Do(document, ProcessEdits.AddMarker(document, 8));
        history.MarkClean();
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 32)));
        var current = document.Interleaved[16];
        Assert.True(history.CanUndo);
        Assert.Equal(3, history.CurrentIndex);

        var exported = history.TryExport();
        Assert.NotNull(exported);
        Assert.Equal(3, exported!.Recipes.Length);
        Assert.Equal(3, exported.CurrentIndex);
        Assert.Equal(2, exported.CleanIndex);

        var restored = MakeConstant(32, 1f);
        Assert.True(EditHistory.TryImport(restored, exported, out var imported));
        Assert.Equal(3, imported.CurrentIndex);
        Assert.Equal(3, imported.TotalCount);
        Assert.False(imported.IsClean);
        Assert.True(restored.IsDirty);
        Assert.True(restored.HasMarkerAt(8));
        Assert.Equal(current, restored.Interleaved[16], 4);

        Assert.True(imported.JumpTo(restored, 0));
        Assert.Equal(original, restored.Interleaved[0], 5);
        Assert.False(restored.HasMarkerAt(8));
    }

    [Fact]
    public void TryExportImport_KeepsRedoWhenCurrentIsNotLatest()
    {
        var document = MakeConstant(16, 0.5f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeOut(document, new WaveSelection(0, 16)));
        history.Do(document, ProcessEdits.AddMarker(document, 4));
        history.Undo(document);
        Assert.Equal(1, history.CurrentIndex);
        Assert.True(history.CanRedo);

        var exported = history.TryExport();
        Assert.NotNull(exported);

        var restored = MakeConstant(16, 0.5f);
        Assert.True(EditHistory.TryImport(restored, exported!, out var imported));
        Assert.Equal(1, imported.CurrentIndex);
        Assert.True(imported.CanRedo);
        Assert.False(restored.HasMarkerAt(4));

        Assert.True(imported.Redo(restored));
        Assert.True(restored.HasMarkerAt(4));
    }

    [Fact]
    public void HistoryJson_RoundTripsRecipes()
    {
        var document = MakeConstant(8, 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Delete(document, new WaveSelection(4, 8)));
        var exported = history.TryExport();
        Assert.NotNull(exported);

        var path = Path.Combine(Path.GetTempPath(), $"mga-history-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(DocumentSessionStore.TryWriteHistory(path, exported!));
            Assert.True(DocumentSessionStore.TryReadHistory(path, out var loaded));
            Assert.Equal(exported!.CurrentIndex, loaded.CurrentIndex);
            Assert.Equal(HistoryRecipes.Delete, loaded.Recipes[0].Kind);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void SanitizeSidecarName_AllowsOriginAndHistory()
    {
        Assert.Equal("doc-2-origin.wav", DocumentSessionStore.SanitizeSidecarName(@"..\session\doc-2-origin.wav"));
        Assert.Equal("doc-2-history.json", DocumentSessionStore.SanitizeSidecarName("doc-2-history.json"));
        Assert.Null(DocumentSessionStore.SanitizeSidecarName("doc-2.wav"));
        Assert.Null(DocumentSessionStore.SanitizeSidecarName("notes.json"));
    }

    private static AudioDocument MakeConstant(int frames, float value)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
    }
}
