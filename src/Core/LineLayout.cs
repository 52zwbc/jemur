using System.Windows;

namespace Jemur.Core;

/// <summary>Pure geometry for card groups and desktop pins, kept free of windows so it can be tested.</summary>
internal static class LineLayout
{
    public const double GroupGap = 10;

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
}
