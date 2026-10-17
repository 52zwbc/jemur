using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Jemur.Core;
using Jemur.UI;
using Path = System.Windows.Shapes.Path;

namespace Jemur.Windows;

/// <summary>
/// One thing on the line: a polaroid screenshot or a paper note, held by a clothespin.
/// Shadows are layered translucent borders instead of DropShadowEffect: effects force an extra
/// off-screen surface per card, which on a layered (transparent) window costs memory on every frame.
/// </summary>
internal sealed class CardView : Grid
{
    /// <summary>Global size multiplier, from Settings → Card size.</summary>
    public static double Scale = 1.0;

    public const double PinHeight = 30, PinOverlap = 15;

    public HangItem Item { get; }
    public readonly RotateTransform Rot = new();
    public readonly TranslateTransform Slide = new();
    public double Angle, Velocity;

    private readonly Border _paper;
    private readonly Grid _paperHost;
    private readonly Image? _image;
    private readonly TextBlock? _noteText;
    private readonly Border _toast;
    private readonly TextBlock _toastText;

    public CardView(HangItem item, bool thumbnail = true)
    {
        Item = item;
        RenderTransformOrigin = new Point(0.5, 0);
        var tg = new TransformGroup();
        tg.Children.Add(Rot);
        tg.Children.Add(Slide);
        RenderTransform = tg;
        Cursor = Cursors.Hand;
        UseLayoutRounding = true;

        _paper = new Border { CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Top };
        _paperHost = new Grid { Margin = new Thickness(0, PinHeight - PinOverlap, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        AddShadow(_paperHost);
        _paperHost.Children.Add(_paper);

        if (item.Kind == ItemKind.Image)
        {
            _paper.Background = Brushes.White;
            _paper.Padding = new Thickness(6, 6, 6, 15);
            Width = Math.Round(156 * Scale) + 12;      // sensible size even if the file can't be read
            _paper.Height = Math.Round(100 * Scale) + 21;
            _image = new Image { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _paper.Child = new Border { Child = _image, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)) };
            if (thumbnail) ReloadImage();
        }
        else
        {
            _paper.Padding = new Thickness(11, 12, 11, 10);
            Width = Math.Round(138 * Scale);
            _paper.Height = Math.Round(120 * Scale);
            _noteText = new TextBlock
            {
                FontFamily = Theme.Hand, FontSize = Math.Round(12.5 * Math.Sqrt(Scale) * 2) / 2, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Theme.Ink, LineHeight = Math.Round(18 * Math.Sqrt(Scale)),
            };
            var g = new Grid();
            g.Children.Add(_noteText);
            g.Children.Add(new Path
            {
                Data = Geometry.Parse("M 12,0 L 12,12 L 0,12 Z"), Fill = new SolidColorBrush(Color.FromArgb(0x1E, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -11, -10),
            });
            _paper.Child = g;
            RefreshNote();
        }

        Children.Add(_paperHost);
        Children.Add(Clothespin());

        _toastText = new TextBlock { Foreground = Brushes.White, FontSize = 11.5, FontFamily = Theme.Font };
        _toast = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 4), Child = _toastText, Opacity = 0, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 20),
        };
        Children.Add(_toast);
        Height = TotalHeight;
    }

    public double PaperHeight => _paper.Height;
    public double TotalHeight => PinHeight - PinOverlap + PaperHeight;

    private static readonly Brush[] ShadowBrushes =
    {
        Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x0A, 0, 0, 0))),
        Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0, 0, 0))),
        Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x16, 0, 0, 0))),
    };

    /// <summary>Three stacked translucent rounded borders read as a soft shadow at a fraction of the cost of a blur.</summary>
    private static void AddShadow(Grid host)
    {
        host.Children.Add(new Border { Background = ShadowBrushes[0], CornerRadius = new CornerRadius(7), Margin = new Thickness(-4, 1, -4, -7) });
        host.Children.Add(new Border { Background = ShadowBrushes[1], CornerRadius = new CornerRadius(5), Margin = new Thickness(-2, 1, -2, -4) });
        host.Children.Add(new Border { Background = ShadowBrushes[2], CornerRadius = new CornerRadius(3), Margin = new Thickness(-1, 0, -1, -1.5) });
    }

    private static readonly Brush Wood = Theme.Freeze(new LinearGradientBrush(Color.FromRgb(0xE4, 0xB4, 0x80), Color.FromRgb(0xB5, 0x7A, 0x42), 0));
    private static readonly Brush WoodEdge = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x4A, 0x2C, 0x10)));
    private static readonly Brush Spring = Theme.Freeze(new LinearGradientBrush(Color.FromRgb(0xD8, 0xDC, 0xE1), Color.FromRgb(0x84, 0x8B, 0x94), 90));

    private static UIElement Clothespin()
    {
        var pin = new Grid { Width = 12, Height = PinHeight, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        pin.Children.Add(new Border { Background = ShadowBrushes[1], CornerRadius = new CornerRadius(3), Margin = new Thickness(-1, 1, -1, -1.5) });
        pin.Children.Add(new Border { Background = Wood, CornerRadius = new CornerRadius(3, 3, 2, 2), BorderBrush = WoodEdge, BorderThickness = new Thickness(0.75) });
        pin.Children.Add(new Border { Width = 1, Background = WoodEdge, Margin = new Thickness(0, 3, 0, 3) });
        pin.Children.Add(new Border { Height = 3, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-1.5, 11, -1.5, 0), CornerRadius = new CornerRadius(1.5), Background = Spring });
        return pin;
    }

    /// <summary>Decodes only as many pixels as the card shows (×2 for high-DPI), never the full screenshot.</summary>
    public void ReloadImage()
    {
        if (_image == null || Item.File == null) return;
        try
        {
            double maxW = 156 * Scale, maxH = 112 * Scale;
            var (pw, ph) = Images.Size(Item.File);
            double ar = pw > 0 && ph > 0 ? pw / (double)ph : 16 / 9.0;
            double w = maxW, h = w / ar;
            if (h > maxH) { h = maxH; w = h * ar; }
            w = Math.Round(Math.Max(w, 64)); h = Math.Round(Math.Max(h, 48));
            _image.Source = Images.Thumbnail(Item.File, (int)(w * 2));
            _image.Width = w; _image.Height = h;
            Width = w + 12;
            _paper.Height = h + 21;
            Height = TotalHeight;
        }
        catch (Exception ex) { Log.Warn($"Thumbnail failed for {Item.File}", ex); }
    }

    public void RefreshNote()
    {
        if (_noteText == null) return;
        _paper.Background = new SolidColorBrush(NoteColors.Paper(Item.Color));
        bool empty = string.IsNullOrWhiteSpace(Item.Text);
        _noteText.Text = empty ? L.T("note.empty") : Item.Text;
        _noteText.Opacity = empty ? 0.45 : 1;
    }

    public void Toast(string text)
    {
        _toastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1350))));
        _toast.BeginAnimation(OpacityProperty, a);
    }

    public void Pulse()
    {
        var st = new ScaleTransform(1, 1, Width / 2, 0);
        _paperHost.RenderTransform = st;
        var a = new DoubleAnimation(1, 1.05, TimeSpan.FromMilliseconds(100)) { AutoReverse = true };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Fade the card while it is out in the spotlight.</summary>
    public void SetAway(bool away) => BeginAnimation(OpacityProperty, new DoubleAnimation(away ? 0.2 : 1, TimeSpan.FromMilliseconds(160)));

    public void Impulse(double degPerSec) => Velocity += degPerSec;

    /// <summary>Damped pendulum. Returns true while still moving.</summary>
    public bool Step(double dt)
    {
        const double k = 55, c = 3.2;
        Velocity += (-k * Angle - c * Velocity) * dt;
        Angle = Math.Clamp(Angle + Velocity * dt, -38, 38);
        bool moving = Math.Abs(Angle) > 0.05 || Math.Abs(Velocity) > 0.3;
        if (!moving) { Angle = 0; Velocity = 0; }
        Rot.Angle = Angle;
        return moving;
    }
}

/// <summary>Paper colours for notes, chosen to read well with dark ink and to stay calm on any wallpaper.</summary>
internal static class NoteColors
{
    private static readonly Color[] Papers =
    {
        Color.FromRgb(0xFB, 0xEB, 0x9A), // butter
        Color.FromRgb(0xFA, 0xD4, 0xD0), // blush
        Color.FromRgb(0xCF, 0xE5, 0xF5), // sky
        Color.FromRgb(0xD6, 0xEC, 0xCC), // sage
        Color.FromRgb(0xF3, 0xEE, 0xE4), // paper
    };
    public static readonly string[] Keys = { "color.yellow", "color.pink", "color.blue", "color.green", "color.paper" };
    public static int Count => Papers.Length;
    public static Color Paper(int i) => Papers[Math.Clamp(i, 0, Papers.Length - 1)];
}
