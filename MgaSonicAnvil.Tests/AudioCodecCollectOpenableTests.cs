using System.IO;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioCodecCollectOpenableTests
{
    [Fact]
    public void CollectOpenable_RecursesFoldersAndSkipsUnsupported()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-anvil-drop-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        var empty = Path.Combine(nested, "empty");
        Directory.CreateDirectory(empty);
        var wav = Path.Combine(root, "a.wav");
        var mp3 = Path.Combine(nested, "b.MP3");
        var aiff = Path.Combine(nested, "c.aiff");
        var skipped = Path.Combine(nested, "notes.txt");
        var image = Path.Combine(root, "cover.png");
        File.WriteAllText(wav, "wav");
        File.WriteAllText(mp3, "mp3");
        File.WriteAllText(aiff, "aiff");
        File.WriteAllText(skipped, "txt");
        File.WriteAllText(image, "png");
        try
        {
            Assert.True(AudioCodec.CanAcceptDrop([root]));
            Assert.False(AudioCodec.CanAcceptDrop([skipped, image]));

            var collected = AudioCodec.CollectOpenable([root]);
            Assert.Equal(3, collected.Length);
            Assert.Contains(wav, collected, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(mp3, collected, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(aiff, collected, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(skipped, collected, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(image, collected, StringComparer.OrdinalIgnoreCase);

            var mixed = AudioCodec.CollectOpenable([wav, root]);
            Assert.Equal(3, mixed.Length);
            Assert.Equal(wav, mixed[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CollectOpenableFromDirectory_NonRecursive_SkipsNested()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-anvil-dir-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        var top = Path.Combine(root, "top.wav");
        var child = Path.Combine(nested, "child.mp3");
        File.WriteAllText(top, "wav");
        File.WriteAllText(child, "mp3");
        try
        {
            var shallow = AudioCodec.CollectOpenableFromDirectory(root, recursive: false);
            Assert.Equal([top], shallow);

            var deep = AudioCodec.CollectOpenableFromDirectory(root, recursive: true);
            Assert.Equal(2, deep.Length);
            Assert.Contains(top, deep, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(child, deep, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CollectPlayerOpenableDirectoryLayer_RootBeforeChildren()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-anvil-layer-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        var deep = Path.Combine(nested, "deep");
        Directory.CreateDirectory(deep);
        var top = Path.Combine(root, "top.wav");
        var nestedFile = Path.Combine(nested, "child.mp3");
        var deepFile = Path.Combine(deep, "leaf.m4a");
        var skipped = Path.Combine(root, "notes.txt");
        File.WriteAllText(top, "wav");
        File.WriteAllText(nestedFile, "mp3");
        File.WriteAllText(deepFile, "m4a");
        File.WriteAllText(skipped, "txt");
        try
        {
            AudioCodec.CollectPlayerOpenableDirectoryLayer(root, out var files, out var children);
            Assert.Equal([top], files);
            Assert.Equal([nested], children);
            Assert.DoesNotContain(skipped, files, StringComparer.OrdinalIgnoreCase);

            AudioCodec.CollectPlayerOpenableDirectoryLayer(nested, out files, out children);
            Assert.Equal([nestedFile], files);
            Assert.Equal([deep], children);

            AudioCodec.CollectPlayerOpenableDirectoryLayer(deep, out files, out children);
            Assert.Equal([deepFile], files);
            Assert.Empty(children);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CollectOpenable_EmptyFolder_ReturnsNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-anvil-drop-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.True(AudioCodec.CanAcceptDrop([root]));
            Assert.Empty(AudioCodec.CollectOpenable([root]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CollectPlayerOpenable_IncludesM4a_EditorCollectSkipsIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-anvil-m4a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var m4a = Path.Combine(root, "song.m4a");
        var mp3 = Path.Combine(root, "song.mp3");
        File.WriteAllText(m4a, "m4a");
        File.WriteAllText(mp3, "mp3");
        try
        {
            Assert.False(AudioCodec.IsOpenable(m4a));
            Assert.True(AudioCodec.IsPlayerOpenable(m4a));
            Assert.Equal(AudioFileKind.M4a, AudioCodec.DetectKind(m4a));

            var editor = AudioCodec.CollectOpenable([root]);
            Assert.DoesNotContain(m4a, editor, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(mp3, editor, StringComparer.OrdinalIgnoreCase);

            var player = AudioCodec.CollectPlayerOpenable([root]);
            Assert.Contains(m4a, player, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(mp3, player, StringComparer.OrdinalIgnoreCase);
            Assert.True(AudioCodec.CanAcceptPlayerDrop([m4a]));
            Assert.False(AudioCodec.CanAcceptDrop([m4a]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
