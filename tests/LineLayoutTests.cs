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

    [Fact]
    public void FirstPinSpot_Is200DipBelowTheLineInPixels() =>
        Assert.Equal(new Point(150, 405), LineLayout.FirstPinSpot(new Point(100, 70), 1.5));

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.75)]
    public void CardAndWindowOffsetsRoundTrip(double scale)
    {
        var card = new Point(2400, 300);
        var pad = new Vector(92, 28);
        var window = LineLayout.WindowForCard(card, pad, scale);
        Assert.Equal(new Point(2400 - 92 * scale, 300 - 28 * scale), window);
        Assert.Equal(card, LineLayout.CardInWindow(window, pad, scale));
    }

    [Fact]
    public void DraggedCard_StaysUnderTheCursorWhenTheScaleChanges()
    {
        var pad = new Vector(92, 28);
        var pressCard = new Point(1700, 300);
        var pressCursor = new Point(1780, 320); // grabbed 80 px right, 20 px down from the card's top-left
        var cursor = new Point(2100, 340);      // now on a 150% monitor
        var card = LineLayout.DraggedCard(pressCard, pressCursor, cursor);
        Assert.Equal(new Vector(80, 20), pressCursor - pressCard);
        Assert.Equal(new Vector(80, 20), cursor - card);
        foreach (var scale in new[] { 1.0, 1.5 })
            Assert.Equal(card, LineLayout.CardInWindow(LineLayout.WindowForCard(card, pad, scale), pad, scale));
    }
}
