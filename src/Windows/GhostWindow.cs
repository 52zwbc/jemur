using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Jemur.Interop;

namespace Jemur.Windows;

/// <summary>A click-through copy of a card that follows the cursor while dragging, and falls when let go.</summary>
internal sealed class GhostWindow : Window
{
    private const double Pad = 20;
    private readonly RotateTransform _tilt = new(-5);

    public GhostWindow(CardView source)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;

        var view = new CardView(source.Item) { IsHitTestVisible = false, Opacity = 0.94 };
        var host = new Grid { Margin = new Thickness(Pad), RenderTransformOrigin = new Point(0.5, 0), RenderTransform = _tilt };
        host.Children.Add(view);
        Content = host;
        Width = source.Width + Pad * 2;
        Height = source.Height + Pad * 2;
        Left = -10000; Top = -10000;
        SourceInitialized += (_, _) => Native.MakeToolWindow(this, clickThrough: true);
    }

    public void FollowCursor()
    {
        var p = Native.CursorDip(this);
        Left = p.X - Width / 2;
        Top = p.Y - Pad - 4;
    }

    public void ShowAt(double left, double top, double angle)
    {
        Left = left - Pad;
        Top = top - Pad;
        _tilt.Angle = angle;
        Show();
    }

    public void Fall()
    {
        var dur = TimeSpan.FromMilliseconds(560);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var drop = new DoubleAnimation(Top, Top + 480, dur) { EasingFunction = ease };
        drop.Completed += (_, _) => Close();
        BeginAnimation(TopProperty, drop);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, dur) { EasingFunction = ease });
        _tilt.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(_tilt.Angle, _tilt.Angle + (Random.Shared.NextDouble() < .5 ? -1 : 1) * 60, dur) { EasingFunction = ease });
    }
}
