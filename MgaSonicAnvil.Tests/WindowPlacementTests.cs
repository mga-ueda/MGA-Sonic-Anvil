using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WindowPlacementTests
{
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
    }
}
