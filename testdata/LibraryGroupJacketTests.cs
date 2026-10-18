using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryGroupJacketTests
{
    [Fact]
    public void ArtworkForGroup_UsesActiveRowInSameGroup_KeepsFirstForOthers()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var red = SolidPng(255, 32, 32);
            var blue = SolidPng(32, 32, 255);
            var green = SolidPng(32, 255, 32);
            var first = Session(@"jacket-pick-a\01.mp3", red);
            var second = Session(@"jacket-pick-a\02.mp3", blue);
            var other = Session(@"jacket-pick-b\01.mp3", green);
            var view = new LibraryBrowserView();
            view.SetGroup(LibraryFileGroup.Folder);
            view.SetSessions([first, second, other], first, [first]);
            Flush();

            var groupA = view.PlaylistGroupNamed("jacket-pick-a");
            var groupB = view.PlaylistGroupNamed("jacket-pick-b");
            Assert.NotNull(groupA);
            Assert.NotNull(groupB);

            AssertRgb(view.ArtworkForGroup(groupA), 255, 32, 32);
            AssertRgb(view.ArtworkForGroup(groupB), 32, 255, 32);

            var secondRow = RowFor(groupA!, second);
            view.SelectOnlyPlaylistRow(secondRow);
            Flush();
            AssertRgb(view.ArtworkForGroup(groupA), 32, 32, 255);
            AssertRgb(view.ArtworkForGroup(groupB), 32, 255, 32);

            var otherRow = RowFor(groupB!, other);
            view.SelectOnlyPlaylistRow(otherRow);
            Flush();
            AssertRgb(view.ArtworkForGroup(groupA), 255, 32, 32);
            AssertRgb(view.ArtworkForGroup(groupB), 32, 255, 32);
        });
    }

    private static LibraryFileRow RowFor(System.Windows.Data.CollectionViewGroup group, DocumentSession session)
    {
        foreach (var item in group.Items)
        {
            if (item is LibraryFileRow { Tag: DocumentSession tagged } row
                && ReferenceEquals(tagged, session))
            {
                return row;
            }
        }

        throw new InvalidOperationException("Row was not in the group.");
    }

    private static DocumentSession Session(string relative, byte[] art)
    {
        var session = new DocumentSession(
            AudioDocument.CreateDeferred(Path.Combine(Path.GetTempPath(), relative)));
        session.Document.SetArtwork(art);
        return session;
    }

    private static byte[] SolidPng(byte r, byte g, byte b)
    {
        var bmp = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { b, g, r, 255 },
            4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void AssertRgb(BitmapSource? source, byte r, byte g, byte b)
    {
        Assert.NotNull(source);
        var pixels = new byte[4];
        source!.CopyPixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
        Assert.InRange(pixels[2], r - 8, r + 8);
        Assert.InRange(pixels[1], g - 8, g + 8);
        Assert.InRange(pixels[0], b - 8, b + 8);
    }

    private static void EnsureTheme()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        Application.Current!.Resources["AccentCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x6C, 0xB6, 0xFF));
        Application.Current.Resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA));
        Application.Current.Resources["MenuHighlightBackBrush"] = new SolidColorBrush(Color.FromRgb(0x37, 0x37, 0x3A));
        Application.Current.Resources["SurfaceBackBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    }

    private static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
