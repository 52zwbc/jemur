using System.Windows;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

public sealed class LineLayoutTests
{
    private static readonly double[] W = { 50, 70 };

    [Fact]
    public void GroupSlots_ReturnsCardCentres() =>
        Assert.Equal(new[] { 125d, 195d }, LineLayout.GroupSlots(100, W, 10));

    [Theory]
    [InlineData(110, 0)]
    [InlineData(170, 1)]
    [InlineData(400, 2)]
    public void SlotIndexAt_FindsInsertIndex(double x, int expected) =>
        Assert.Equal(expected, LineLayout.SlotIndexAt(100, W, 10, x));

    [Fact]
    public void IsOutsideGroup_NeedsHalfACardBeyondAnEdge()
    {
        Assert.True(LineLayout.IsOutsideGroup(100, W, 10, 50, 50));
        Assert.False(LineLayout.IsOutsideGroup(100, W, 10, 150, 50));
        Assert.True(LineLayout.IsOutsideGroup(100, W, 10, 300, 50));
        Assert.False(LineLayout.IsOutsideGroup(100, W, 10, 250, 50));
    }

    private static readonly Rect[] Screens = { new(0, 0, 1920, 1040), new(1920, 0, 1280, 984) };

    [Fact]
    public void Clamp_MovesOffscreenPinIntoNearestWorkArea() =>
        Assert.Equal(new Point(3040, 100), LineLayout.ClampPin(new Rect(3500, 100, 160, 140), Screens));

    [Fact]
    public void Clamp_KeepsMostlyVisiblePin() =>
        Assert.Equal(new Point(1850, 100), LineLayout.ClampPin(new Rect(1850, 100, 160, 140), Screens));
}
