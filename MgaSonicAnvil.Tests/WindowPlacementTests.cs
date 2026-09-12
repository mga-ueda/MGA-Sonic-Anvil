using System.Windows;
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
}
