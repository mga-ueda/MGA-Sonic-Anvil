using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void VideoLaunch_RemembersPositionOnly()
    {
        var settings = new AppSettings();
        Assert.False(settings.VideoLaunchWindowHasPosition);
        Assert.False(settings.VideoLaunchHud);
        Assert.False(settings.VideoLaunchTimecode);
        settings.VideoLaunchWindowX = 120;
        settings.VideoLaunchWindowY = 80;
        settings.VideoLaunchWindowHasPosition = true;
        settings.VideoLaunchHud = false;
        settings.VideoLaunchTimecode = true;
        var json = System.Text.Json.JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.True(back!.VideoLaunchWindowHasPosition);
        Assert.Equal(120, back.VideoLaunchWindowX);
        Assert.Equal(80, back.VideoLaunchWindowY);
        Assert.False(back.VideoLaunchHud);
        Assert.True(back.VideoLaunchTimecode);
        LibraryPlayerMode.ResolveVideoLaunchChrome(
            back.VideoLaunchHud,
            back.VideoLaunchTimecode,
            out var hud,
            out var timecode);
        Assert.False(hud);
        Assert.True(timecode);
    }

    [Fact]
    public void FitVideoLaunchOuterSize_PreservesAspectAndAddsNonClient()
    {
        var outer = WindowPlacement.FitVideoLaunchOuterSize(
            clientWidth: 1920,
            clientHeight: 1080,
            nonClientWidth: 16,
            nonClientHeight: 39,
            minOuterWidth: 300,
            minOuterHeight: 200,
            maxOuterWidth: 4000,
            maxOuterHeight: 3000);
        Assert.Equal(1936, outer.Width, 3);
        Assert.Equal(1119, outer.Height, 3);
        var clientW = outer.Width - 16;
        var clientH = outer.Height - 39;
        Assert.Equal(1920 / 1080.0, clientW / clientH, 5);
    }

    [Fact]
    public void FitVideoLaunchOuterSize_MinOuterPreservesAspect()
    {
        var outer = WindowPlacement.FitVideoLaunchOuterSize(
            clientWidth: 100,
            clientHeight: 100,
            nonClientWidth: 0,
            nonClientHeight: 0,
            minOuterWidth: 400,
            minOuterHeight: 200,
            maxOuterWidth: 2000,
            maxOuterHeight: 2000);
        Assert.Equal(400, outer.Width, 3);
        Assert.Equal(400, outer.Height, 3);
    }

    [Fact]
    public void UsesVideoLaunchPlacementSlot_OnlyWhenVideoMiniFlag()
    {
        // プレイリスト（F10）再生中はフラグが立たない前提。立っているときだけ動画ミニ枠へ書く。
        Assert.False(WindowPlacement.UsesVideoLaunchPlacementSlot(videoLaunchPlacement: false));
        Assert.True(WindowPlacement.UsesVideoLaunchPlacementSlot(videoLaunchPlacement: true));
    }

    [Fact]
    public void VideoLaunchBoundsFromCenter_KeepsCenterWhenSizeChanges()
    {
        var small = WindowPlacement.VideoLaunchBoundsFromCenter(500, 400, width: 200, height: 100);
        Assert.Equal(400, small.X, 3);
        Assert.Equal(350, small.Y, 3);
        Assert.Equal(200, small.Width, 3);
        Assert.Equal(100, small.Height, 3);

        var large = WindowPlacement.VideoLaunchBoundsFromCenter(500, 400, width: 800, height: 450);
        Assert.Equal(100, large.X, 3);
        Assert.Equal(175, large.Y, 3);
        Assert.Equal(500, large.X + large.Width * 0.5, 3);
        Assert.Equal(400, large.Y + large.Height * 0.5, 3);
    }

    [Fact]
    public void CaptureVideoLaunchCenter_StoresCenterNotTopLeft()
    {
        var settings = new AppSettings();
        WindowPlacement.CaptureVideoLaunchCenter(960, 540, settings);
        Assert.True(settings.VideoLaunchWindowHasPosition);
        Assert.Equal(960, settings.VideoLaunchWindowX);
        Assert.Equal(540, settings.VideoLaunchWindowY);

        var placed = WindowPlacement.VideoLaunchBoundsFromCenter(
            settings.VideoLaunchWindowX,
            settings.VideoLaunchWindowY,
            width: 640,
            height: 360);
        Assert.Equal(640, placed.Width, 3);
        Assert.Equal(360, placed.Height, 3);
        Assert.Equal(960, placed.X + placed.Width * 0.5, 3);
        Assert.Equal(540, placed.Y + placed.Height * 0.5, 3);
    }

    [Fact]
    public void ClampRectToWorkArea_PushesOverflowBackInside()
    {
        var work = new Rect(0, 0, 1000, 800);
        var overflowRight = WindowPlacement.ClampRectToWorkArea(new Rect(900, 100, 200, 100), work);
        Assert.Equal(800, overflowRight.X, 3);
        Assert.Equal(100, overflowRight.Y, 3);
        Assert.Equal(200, overflowRight.Width, 3);

        var overflowBottomLeft = WindowPlacement.ClampRectToWorkArea(new Rect(-50, 750, 300, 200), work);
        Assert.Equal(0, overflowBottomLeft.X, 3);
        Assert.Equal(600, overflowBottomLeft.Y, 3);
        Assert.Equal(300, overflowBottomLeft.Width, 3);
        Assert.Equal(200, overflowBottomLeft.Height, 3);
    }

    [Fact]
    public void ResolveVideoLaunchBounds_KeepsCenterThenClampsToDisplay()
    {
        var work = new Rect(0, 0, 1920, 1080);
        var centered = WindowPlacement.ResolveVideoLaunchBounds(960, 540, 640, 360, work);
        Assert.Equal(640, centered.Width, 3);
        Assert.Equal(360, centered.Height, 3);
        Assert.Equal(960, centered.X + centered.Width * 0.5, 3);
        Assert.Equal(540, centered.Y + centered.Height * 0.5, 3);

        var nearEdge = WindowPlacement.ResolveVideoLaunchBounds(1900, 50, 640, 360, work);
        Assert.Equal(1920 - 640, nearEdge.X, 3);
        Assert.Equal(0, nearEdge.Y, 3);
        Assert.True(nearEdge.X >= work.X);
        Assert.True(nearEdge.Y >= work.Y);
        Assert.True(nearEdge.Right <= work.Right + 0.001);
        Assert.True(nearEdge.Bottom <= work.Bottom + 0.001);
    }

    [Fact]
    public void CaptureVideoLaunchPosition_DoesNotTouchEditorPlayerOrMinimalSlots()
    {
        var settings = new AppSettings
        {
            WindowX = 11,
            WindowY = 22,
            WindowWidth = 1920,
            WindowHeight = 1080,
            WindowState = nameof(WindowState.Normal),
            PlayerWindowX = 33,
            PlayerWindowY = 44,
            PlayerWindowWidth = 1600,
            PlayerWindowHeight = 900,
            PlayerWindowState = nameof(WindowState.Maximized),
            MinimalPlayerWindowX = 55,
            MinimalPlayerWindowY = 66,
            MinimalPlayerWindowWidth = 900,
            MinimalPlayerWindowHeight = 500,
            MinimalPlayerWindowState = nameof(WindowState.Normal),
        };

        WindowPlacement.CaptureVideoLaunchCenter(120, 80, settings);

        Assert.True(settings.VideoLaunchWindowHasPosition);
        Assert.Equal(120, settings.VideoLaunchWindowX);
        Assert.Equal(80, settings.VideoLaunchWindowY);
        Assert.Equal(11, settings.WindowX);
        Assert.Equal(22, settings.WindowY);
        Assert.Equal(1920, settings.WindowWidth);
        Assert.Equal(1080, settings.WindowHeight);
        Assert.Equal(nameof(WindowState.Normal), settings.WindowState);
        Assert.Equal(33, settings.PlayerWindowX);
        Assert.Equal(1600, settings.PlayerWindowWidth);
        Assert.Equal(nameof(WindowState.Maximized), settings.PlayerWindowState);
        Assert.Equal(55, settings.MinimalPlayerWindowX);
        Assert.Equal(900, settings.MinimalPlayerWindowWidth);
        Assert.Equal(nameof(WindowState.Normal), settings.MinimalPlayerWindowState);
        Assert.True(WindowPlacement.UsesVideoLaunchPlacementSlot(videoLaunchPlacement: true));
        Assert.False(WindowPlacement.UsesVideoLaunchPlacementSlot(videoLaunchPlacement: false));
    }

    [Fact]
    public void TryRead_RejectsUnsetSize()
    {
        var settings = new AppSettings();
        Assert.False(WindowPlacement.TryRead(settings, DesignMetrics.WindowMinWidth, DesignMetrics.WindowMinHeight, out _, out _));
    }

    [Fact]
    public void TryRead_RejectsBelowMinimum()
    {
        var settings = new AppSettings
        {
            WindowX = 10,
            WindowY = 20,
            WindowWidth = 400,
            WindowHeight = 300,
        };
        Assert.False(WindowPlacement.TryRead(settings, DesignMetrics.WindowMinWidth, DesignMetrics.WindowMinHeight, out _, out _));
    }

    [Fact]
    public void TryRead_AcceptsSavedNormalBounds()
    {
        var settings = new AppSettings
        {
            WindowX = 80,
            WindowY = 40,
            WindowWidth = 1920,
            WindowHeight = 640,
            WindowState = "Normal",
        };
        Assert.True(WindowPlacement.TryRead(settings, DesignMetrics.WindowMinWidth, DesignMetrics.WindowMinHeight, out var bounds, out var maximized));
        Assert.False(maximized);
        Assert.Equal(80, bounds.X);
        Assert.Equal(40, bounds.Y);
        Assert.Equal(1920, bounds.Width);
        Assert.Equal(640, bounds.Height);
    }

    [Fact]
    public void TryRead_RecognizesMaximized()
    {
        var settings = new AppSettings
        {
            WindowX = 0,
            WindowY = 0,
            WindowWidth = 1920,
            WindowHeight = 720,
            WindowState = "Maximized",
        };
        Assert.True(WindowPlacement.TryRead(settings, DesignMetrics.WindowMinWidth, DesignMetrics.WindowMinHeight, out _, out var maximized));
        Assert.True(maximized);
    }

    [Fact]
    public void TryReadSettings_RejectsUnset()
    {
        Assert.False(WindowPlacement.TryReadSettings(new AppSettings(), out _, out _));
    }

    [Fact]
    public void TryReadSettings_ReturnsSavedBounds()
    {
        var settings = new AppSettings
        {
            SettingsWindowHasPosition = true,
            SettingsWindowX = 40,
            SettingsWindowY = 80,
            SettingsWindowWidth = 900,
            SettingsWindowHeight = 640,
        };
        Assert.True(WindowPlacement.TryReadSettings(settings, out var bounds, out var hasSize));
        Assert.True(hasSize);
        Assert.Equal(40, bounds.X);
        Assert.Equal(80, bounds.Y);
        Assert.Equal(900, bounds.Width);
        Assert.Equal(640, bounds.Height);
    }

    [Fact]
    public void TryReadSettings_PositionOnlyHasNoSize()
    {
        var settings = new AppSettings
        {
            SettingsWindowHasPosition = true,
            SettingsWindowX = 40,
            SettingsWindowY = 80,
        };
        Assert.True(WindowPlacement.TryReadSettings(settings, out var bounds, out var hasSize));
        Assert.False(hasSize);
        Assert.Equal(40, bounds.X);
        Assert.Equal(80, bounds.Y);
    }

    [Fact]
    public void TryReadSettings_HeightAloneCountsAsSize()
    {
        var settings = new AppSettings
        {
            SettingsWindowHasPosition = true,
            SettingsWindowX = 40,
            SettingsWindowY = 80,
            SettingsWindowHeight = 640,
        };
        Assert.True(WindowPlacement.TryReadSettings(settings, out var bounds, out var hasSize));
        Assert.True(hasSize);
        Assert.Equal(640, bounds.Height);
    }

    [Fact]
    public void TryReadColorPanel_RejectsUnset()
    {
        Assert.False(WindowPlacement.TryReadColorPanel(new AppSettings(), out _, out _));
    }

    [Fact]
    public void TryReadColorPanel_ReturnsSavedBounds()
    {
        var settings = new AppSettings
        {
            ColorPanelHasPosition = true,
            ColorPanelX = 120,
            ColorPanelY = 80,
            ColorPanelWidth = 720,
            ColorPanelHeight = 640,
        };
        Assert.True(WindowPlacement.TryReadColorPanel(settings, out var bounds, out var hasSize));
        Assert.True(hasSize);
        Assert.Equal(120, bounds.X);
        Assert.Equal(80, bounds.Y);
        Assert.Equal(720, bounds.Width);
        Assert.Equal(640, bounds.Height);
    }

    [Fact]
    public void StoredExtent_IsUnchangedAtDefaultScale()
    {
        Assert.Equal(1000, WindowPlacement.FromStoredExtent(1000));
        Assert.Equal(1000, WindowPlacement.ToStoredExtent(1000));
    }

    [Fact]
    public void CenteredOn_PlacesDefaultSizeInWorkArea()
    {
        var work = new Rect(100, 50, 1920, 1080);
        var bounds = WindowPlacement.CenteredOn(
            work,
            DesignMetrics.WindowDefaultWidth,
            DesignMetrics.WindowDefaultHeight);

        Assert.Equal(DesignMetrics.WindowDefaultWidth, bounds.Width);
        Assert.Equal(DesignMetrics.WindowDefaultHeight, bounds.Height);
        Assert.Equal(100 + (1920 - DesignMetrics.WindowDefaultWidth) / 2, bounds.X);
        Assert.Equal(50 + (1080 - DesignMetrics.WindowDefaultHeight) / 2, bounds.Y);
    }

    [Fact]
    public void DeviceRectToDip_KeepsPixelsWhenIdentity()
    {
        var bounds = WindowPlacement.DeviceRectToDip(0, 0, 1920, 1080, Matrix.Identity);
        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(1920, bounds.Width);
        Assert.Equal(1080, bounds.Height);
    }

    [Fact]
    public void DeviceRectToDip_ScalesWithTransformFromDevice()
    {
        var fromDevice = new Matrix(0.5, 0, 0, 0.5, 0, 0);
        var bounds = WindowPlacement.DeviceRectToDip(100, 200, 2020, 1280, fromDevice);
        Assert.Equal(50, bounds.X);
        Assert.Equal(100, bounds.Y);
        Assert.Equal(960, bounds.Width);
        Assert.Equal(540, bounds.Height);
    }

    [Fact]
    public void Capture_WritesMaximizedFlagFromOverride()
    {
        var settings = new AppSettings();
        WindowPlacement.Capture(new Rect(12, 24, 1600, 900), maximized: true, settings);
        Assert.Equal(12, settings.WindowX);
        Assert.Equal(24, settings.WindowY);
        Assert.Equal(WindowPlacement.ToStoredExtent(1600), settings.WindowWidth);
        Assert.Equal(WindowPlacement.ToStoredExtent(900), settings.WindowHeight);
        Assert.Equal(nameof(WindowState.Maximized), settings.WindowState);
        Assert.Equal(0, settings.PlayerWindowWidth);
        Assert.Equal(string.Empty, settings.PlayerWindowState);
    }

    [Fact]
    public void CapturePlayer_WritesSeparateSlot()
    {
        var settings = new AppSettings();
        WindowPlacement.Capture(new Rect(10, 20, 1600, 900), maximized: false, settings);
        WindowPlacement.CapturePlayer(new Rect(80, 40, 1100, 700), maximized: true, settings);
        Assert.Equal(10, settings.WindowX);
        Assert.Equal(20, settings.WindowY);
        Assert.Equal(WindowPlacement.ToStoredExtent(1600), settings.WindowWidth);
        Assert.Equal(WindowPlacement.ToStoredExtent(900), settings.WindowHeight);
        Assert.Equal(nameof(WindowState.Normal), settings.WindowState);
        Assert.Equal(80, settings.PlayerWindowX);
        Assert.Equal(40, settings.PlayerWindowY);
        Assert.Equal(WindowPlacement.ToStoredExtent(1100), settings.PlayerWindowWidth);
        Assert.Equal(WindowPlacement.ToStoredExtent(700), settings.PlayerWindowHeight);
        Assert.Equal(nameof(WindowState.Maximized), settings.PlayerWindowState);
    }

    [Fact]
    public void TryReadPlayer_RejectsUnsetSize()
    {
        var settings = new AppSettings();
        Assert.False(WindowPlacement.TryReadPlayer(
            settings,
            DesignMetrics.WindowMinWidth,
            DesignMetrics.WindowMinHeight,
            out _,
            out _));
    }

    [Fact]
    public void CaptureMinimalPlayer_WritesSeparateSlot()
    {
        var settings = new AppSettings();
        WindowPlacement.Capture(new Rect(10, 20, 1600, 900), maximized: false, settings);
        WindowPlacement.CapturePlayer(new Rect(80, 40, 1100, 700), maximized: true, settings);
        WindowPlacement.CaptureMinimalPlayer(new Rect(200, 100, 900, 500), maximized: false, settings);
        Assert.Equal(WindowPlacement.ToStoredExtent(1600), settings.WindowWidth);
        Assert.Equal(WindowPlacement.ToStoredExtent(1100), settings.PlayerWindowWidth);
        Assert.Equal(200, settings.MinimalPlayerWindowX);
        Assert.Equal(100, settings.MinimalPlayerWindowY);
        Assert.Equal(WindowPlacement.ToStoredExtent(900), settings.MinimalPlayerWindowWidth);
        Assert.Equal(WindowPlacement.ToStoredExtent(500), settings.MinimalPlayerWindowHeight);
        Assert.Equal(nameof(WindowState.Normal), settings.MinimalPlayerWindowState);
    }

    [Fact]
    public void TryReadMinimalPlayer_AcceptsSavedBounds()
    {
        var width = DesignMetrics.MinimalPlayerWindowMinWidth + 40;
        var settings = new AppSettings
        {
            MinimalPlayerWindowX = 15,
            MinimalPlayerWindowY = 25,
            MinimalPlayerWindowWidth = (int)Math.Ceiling(width),
            MinimalPlayerWindowHeight = 720,
            MinimalPlayerWindowState = "Maximized",
        };
        Assert.True(WindowPlacement.TryReadMinimalPlayer(
            settings,
            DesignMetrics.MinimalPlayerWindowMinWidth,
            DesignMetrics.WindowMinHeight,
            out var bounds,
            out var maximized));
        Assert.True(maximized);
        Assert.Equal(15, bounds.X);
        Assert.Equal(25, bounds.Y);
        Assert.Equal((int)Math.Ceiling(width), bounds.Width);
        Assert.Equal(720, bounds.Height);
    }

    [Fact]
    public void TryReadMinimalPlayer_AcceptsWidthBelowEditorMinimum()
    {
        Assert.True(DesignMetrics.MinimalPlayerWindowMinWidth < DesignMetrics.WindowMinWidth);
        var width = (int)Math.Ceiling(DesignMetrics.MinimalPlayerWindowMinWidth + 20);
        Assert.True(width < DesignMetrics.WindowMinWidth);
        var settings = new AppSettings
        {
            MinimalPlayerWindowX = 10,
            MinimalPlayerWindowY = 20,
            MinimalPlayerWindowWidth = width,
            MinimalPlayerWindowHeight = (int)Math.Ceiling(DesignMetrics.WindowMinHeight + 40),
            MinimalPlayerWindowState = nameof(WindowState.Normal),
        };
        Assert.True(WindowPlacement.TryReadMinimalPlayer(
            settings,
            DesignMetrics.MinimalPlayerWindowMinWidth,
            DesignMetrics.WindowMinHeight,
            out var bounds,
            out var maximized));
        Assert.False(maximized);
        Assert.Equal(width, bounds.Width);
    }

    [Fact]
    public void TryReadMinimalPlayer_RejectsUnsetSize()
    {
        Assert.False(WindowPlacement.TryReadMinimalPlayer(
            new AppSettings(),
            DesignMetrics.MinimalPlayerWindowMinWidth,
            DesignMetrics.WindowMinHeight,
            out _,
            out _));
    }
}
