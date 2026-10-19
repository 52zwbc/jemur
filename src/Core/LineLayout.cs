using System.Windows;

namespace Jemur.Core;

/// <summary>Pure geometry for card groups and desktop pins, kept free of windows so it can be tested.</summary>
internal static class LineLayout
{
    public const double GroupGap = 10, FirstPinDrop = 200;

    /// <summary>Centre x of each card when laid out left to right from <paramref name="startX"/>.</summary>
    public static double[] GroupSlots(double startX, IReadOnlyList<double> widths, double gap)
    {
        var centres = new double[widths.Count];
        var x = startX;
        for (var i = 0; i < widths.Count; i++)
        {
            centres[i] = x + widths[i] / 2;
            x += widths[i] + gap;
        }
        return centres;
    }

    /// <summary>The index a card dragged to centre <paramref name="x"/> would be inserted at.</summary>
    public static int SlotIndexAt(double startX, IReadOnlyList<double> widths, double gap, double x) =>
        GroupSlots(startX, widths, gap).Count(c => c < x);

    /// <summary>True when the dragged card centre is more than half a card beyond either end of the group.</summary>
    public static bool IsOutsideGroup(double startX, IReadOnlyList<double> widths, double gap, double x, double cardWidth)
    {
        var end = startX + widths.Sum() + gap * Math.Max(0, widths.Count - 1);
        return x < startX - cardWidth / 2 || x > end + cardWidth / 2;
    }

    /// <summary>Keeps a pin reachable after a monitor change: unchanged if at least half is on one work area.</summary>
    public static Point ClampPin(Rect pin, IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0) return pin.TopLeft;
        var area = pin.Width * pin.Height;
        if (workAreas.Any(w => { var o = Rect.Intersect(w, pin); return !o.IsEmpty && o.Width * o.Height >= area / 2; })) return pin.TopLeft;

        var cx = pin.X + pin.Width / 2;
        var cy = pin.Y + pin.Height / 2;
        var near = workAreas.OrderBy(w => Math.Pow(w.X + w.Width / 2 - cx, 2) + Math.Pow(w.Y + w.Height / 2 - cy, 2)).First();
        return new Point(Math.Max(near.Left, Math.Min(pin.X, near.Right - pin.Width)),
                         Math.Max(near.Top, Math.Min(pin.Y, near.Bottom - pin.Height)));
    }

    /// <summary>
    /// Where a card first lands when pinned: <see cref="FirstPinDrop"/> DIP below its spot on the line, in physical pixels.
    /// The line sits at the origin of the primary screen, so its DIPs scale straight to pixels.
    /// </summary>
    public static Point FirstPinSpot(Point cardOnLineDip, double lineScale) =>
        new(cardOnLineDip.X * lineScale, (cardOnLineDip.Y + FirstPinDrop) * lineScale);

    /// <summary>A window's top-left in physical pixels, for a card held <paramref name="padDip"/> inside it.</summary>
    public static Point WindowForCard(Point cardPx, Vector padDip, double scale) =>
        new(cardPx.X - padDip.X * scale, cardPx.Y - padDip.Y * scale);

    /// <summary>The inverse of <see cref="WindowForCard"/>.</summary>
    public static Point CardInWindow(Point windowPx, Vector padDip, double scale) =>
        new(windowPx.X + padDip.X * scale, windowPx.Y + padDip.Y * scale);
}
