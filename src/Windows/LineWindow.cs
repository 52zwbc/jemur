using System.IO;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Jemur.App;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;
using Path = System.Windows.Shapes.Path;

namespace Jemur.Windows;

/// <summary>
/// The clothesline: a transparent, always-on-top strip across the top of the primary screen.
/// Fully transparent pixels let clicks through, so only the rope and the cards catch the mouse.
/// When idle the cards ride up until only their bottom edges peek out ("out of sight"), and the
/// window shrinks to a thin strip so its layered surface costs almost nothing.
/// </summary>
internal sealed class LineWindow : Window
{
    private const double BandHeight = 300, RopeY = 30, Sag = 9, Peek = 20, CollapsedHeight = 36;
    private const double DragThreshold = 5, TagX = 14, SwitchSlide = 220;
    private const int HoldMs = 500;

    private readonly JemurApp _app;
    private readonly Canvas _stage = new();
    private readonly Path _rope, _ropeShadow, _ropeTwist;
    private readonly List<CardView> _cards = new();
    private readonly RopeTag _tag = new();
    private readonly DispatcherTimer _hover, _hold, _shrink;

    private bool _expanded = true;
    private bool _lifted;
    private string _ropeId;
    private int _flipSeq;
    private DateTime _holdOpenUntil = DateTime.MinValue;
    private DateTime _outsideSince = DateTime.MinValue;
    private bool _animating;
    private TimeSpan _lastFrame;

    // press / drag
    private CardView? _pressed;
    private Point _pressPoint;
    private double _pressX, _lastDragX;
    private bool _dragging, _holdFired;

    public bool AutoHide { get; set; } = true;
    public bool MenuOpen { get; set; }

    public LineWindow(JemurApp app)
    {
        _app = app;
        Title = "Jemur";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = BandHeight;

        _ropeShadow = new Path { Stroke = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0))), StrokeThickness = 3.4, IsHitTestVisible = false, RenderTransform = new TranslateTransform(0, 1.6) };
        _rope = new Path { Stroke = Theme.Freeze(new SolidColorBrush(Color.FromRgb(0xE9, 0xDF, 0xCF))), StrokeThickness = 2.4, IsHitTestVisible = false };
        // Short darker dashes along the rope read as twisted fibre.
        _ropeTwist = new Path { Stroke = Theme.Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x8A, 0x74, 0x58))), StrokeThickness = 2.4, StrokeDashArray = new DoubleCollection { 0.6, 1.6 }, IsHitTestVisible = false };
        _stage.Children.Add(_ropeShadow);
        _stage.Children.Add(_rope);
        _stage.Children.Add(_ropeTwist);
        _stage.Children.Add(_tag);
        _tag.MouseLeftButtonUp += (_, e) => { _app.ShowRopeMenu(_tag); e.Handled = true; };
        Content = _stage;

        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(this, noActivate: true);
            HwndSource.FromHwnd(Native.Handle(this))?.AddHook(WndProc);
        };
        SizeChanged += (_, _) => { DrawRope(); LayoutCards(); };

        _hover = new DispatcherTimer(TimeSpan.FromMilliseconds(90), DispatcherPriority.Background, (_, _) => UpdateHover(), Dispatcher);
        _hold = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(HoldMs) };
        _hold.Tick += (_, _) => OnHold();
        _shrink = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(420) };
        _shrink.Tick += (_, _) =>
        {
            _shrink.Stop();
            if (_expanded) return;
            Height = CollapsedHeight;
            Memory.TrimSoon(2500); // tucked away again: give back what showing the cards paged in
        };

        _ropeId = app.Settings.ActiveRope;
        foreach (var item in app.Library.Items.Where(OnLine)) AddCard(item, animate: false);
        UpdateTag();
        app.Library.Added += item => { if (!OnLine(item)) return; AddCard(item, animate: true); HoldOpen(2400); };
        app.Library.Removed += item => { var c = CardOf(item); if (c != null) RemoveCard(c); };
        app.Library.Updated += item => CardOf(item)?.RefreshNote();
        app.Library.Relocated += OnRelocated;
        app.Library.Changed += () =>
        {
            UpdateTag();
            if (StaysOut && !IsVisible && !_lifted && !_app.HasOverlay) HoldOpen(1600); // e.g. a second rope now exists: its tag must be reachable
        };

        DrawRope();
        Loaded += (_, _) => { LayoutCards(); HoldOpen(1600); };
    }

    private IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_DISPLAYCHANGE || msg == Native.WM_DPICHANGED)
            Dispatcher.BeginInvoke(() => { Width = SystemParameters.PrimaryScreenWidth; LayoutCards(); });
        return IntPtr.Zero;
    }

    // ───────────── geometry ─────────────

    private double LineWidth => ActualWidth > 0 ? ActualWidth : Width;

    private double RopeAt(double x)
    {
        double t = Math.Clamp((x + 10) / (LineWidth + 20), 0, 1);
        return RopeY + 4 * Sag * t * (1 - t);
    }

    private void DrawRope()
    {
        double w = LineWidth;
        var g = new PathGeometry();
        var f = new PathFigure { StartPoint = new Point(-10, RopeY) };
        f.Segments.Add(new QuadraticBezierSegment(new Point(w / 2, RopeY + 2 * Sag), new Point(w + 10, RopeY), true));
        g.Figures.Add(f);
        g.Freeze();
        _rope.Data = _ropeShadow.Data = _ropeTwist.Data = g;
    }

    private double XOf(CardView c) => Math.Clamp(c.Item.Position, 0, 1) * LineWidth;

    private void LayoutCard(CardView c)
    {
        double x = XOf(c);
        Canvas.SetLeft(c, Math.Round(x - c.Width / 2));
        Canvas.SetTop(c, Math.Round(RopeAt(x) - 9));
    }

    private void LayoutCards()
    {
        foreach (var c in _cards) LayoutCard(c);
        Canvas.SetLeft(_tag, TagX);
        Canvas.SetTop(_tag, Math.Round(RopeAt(TagX + 20) - 1));
    }

    private double CollapsedOffset(CardView c) => -(RopeAt(XOf(c)) - 9 + c.Height - Peek);

    private double TagCollapsedOffset => -(Canvas.GetTop(_tag) + (_tag.ActualHeight > 0 ? _tag.ActualHeight : 30) - Peek);

    private double FreePosition()
    {
        double w = LineWidth, gap = 176 * CardView.Scale;
        for (double x = 140; x < w - 100; x += 24)
            if (_cards.All(c => Math.Abs(XOf(c) - x) >= gap)) return x / w;
        return Math.Clamp((140 + _cards.Count * 41 % Math.Max(100, w - 280)) / w, 0.05, 0.95);
    }

    public CardView? CardOf(HangItem item) => _cards.FirstOrDefault(c => c.Item == item);

    /// <summary>Centre of an item's card in screen DIPs (the line sits at the screen origin).</summary>
    public Point CardCenter(HangItem item)
    {
        var c = CardOf(item);
        if (c == null) return new Point(LineWidth / 2, RopeY + 60);
        return new Point(Canvas.GetLeft(c) + c.Width / 2, Canvas.GetTop(c) + c.Slide.Y + c.Height / 2);
    }

    // ───────────── cards ─────────────

    private bool OnLine(HangItem item) => item.RopeId == _ropeId && !item.Pinned;

    private CardView AddCard(HangItem item, bool animate)
    {
        if (item.Position < 0 || item.Position > 1) item.Position = FreePosition();
        var c = new CardView(item);
        c.PreviewMouseLeftButtonDown += Card_Down;
        c.PreviewMouseMove += Card_Move;
        c.PreviewMouseLeftButtonUp += Card_Up;
        c.MouseEnter += (_, _) => { if (_pressed == null) Kick(c, (Random.Shared.NextDouble() < .5 ? -1 : 1) * 26); };
        c.MouseRightButtonUp += (_, e) => { _app.ShowItemMenu(item, c); e.Handled = true; };
        _cards.Add(c);
        _stage.Children.Add(c);
        LayoutCard(c);
        if (!IsVisible && IsLoaded && !_lifted) Show();
        if (_lifted) c.Slide.Y = LiftedY;
        else if (!_expanded) c.Slide.Y = CollapsedOffset(c);
        if (animate && !_lifted)
        {
            c.Angle = 22;
            c.Slide.Y = -c.Height - 40;
            c.Slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
            StartPhysics();
        }
        return c;
    }

    private void RemoveCard(CardView c)
    {
        c.Slide.BeginAnimation(TranslateTransform.YProperty, null);
        _cards.Remove(c);
        _stage.Children.Remove(c);
    }

    /// <summary>Takes a card off the line at once, but lets it animate away (slide by <paramref name="dx"/> and fade) before it leaves the stage.</summary>
    private void Leave(CardView c, int delayMs, double dx)
    {
        _cards.Remove(c);
        c.IsHitTestVisible = false;
        var dur = TimeSpan.FromMilliseconds(Motion.Enabled ? 260 : 0);
        var begin = TimeSpan.FromMilliseconds(delayMs);
        double left = Canvas.GetLeft(c), o = c.Opacity;
        Canvas.SetLeft(c, left); c.Opacity = o; // base values hold the card where it is until its turn
        var fade = new DoubleAnimation(o, 0, dur) { BeginTime = begin };
        fade.Completed += (_, _) =>
        {
            c.BeginAnimation(Canvas.LeftProperty, null);
            c.BeginAnimation(OpacityProperty, null);
            _stage.Children.Remove(c);
        };
        c.BeginAnimation(Canvas.LeftProperty, dx == 0 ? null : new DoubleAnimation(left, left + dx, dur) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        c.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Removes cards that are still animating away, for when the line has to settle at once.</summary>
    private void DropLeavers()
    {
        foreach (var c in _stage.Children.OfType<CardView>().Where(c => !_cards.Contains(c)).ToList()) _stage.Children.Remove(c);
    }

    private void OnRelocated(HangItem item)
    {
        var c = CardOf(item);
        bool on = OnLine(item) && _app.Library.Items.Contains(item);
        if (c != null && !on) RemoveCard(c);
        else if (c == null && on) { AddCard(item, animate: true); HoldOpen(1600); }
    }

    /// <summary>The card shows a toast, then fades off the line (it is going to a rope that isn't showing).</summary>
    public void SeeOff(HangItem item, string toast)
    {
        var c = CardOf(item);
        if (c == null) return;
        c.Toast(toast);
        HoldOpen(1800);
        Leave(c, 1100, 0);
    }

    public void Rebuild()
    {
        DropLeavers();
        foreach (var c in _cards.ToList()) RemoveCard(c);
        foreach (var i in _app.Library.Items.Where(OnLine)) AddCard(i, animate: false);
        UpdateTag();
        HoldOpen(1800);
    }

    // ───────────── ropes ─────────────

    public string RopeId => _ropeId;

    /// <summary>The line stays on screen while the rope has cards, or while there are other ropes to switch to (its tag peeks).</summary>
    public bool StaysOut => _cards.Count > 0 || _app.Library.Ropes.Count > 1;

    private void UpdateTag(bool name = true)
    {
        if (name) _tag.Text = _app.Library.RopeById(_ropeId)?.Name ?? "";
        _tag.Visibility = StaysOut ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Swaps the line to another rope: old cards slide off to the left, the tag turns over, the new cards come in from the right.</summary>
    public void ShowRope(string ropeId, bool animate)
    {
        if (ropeId == _ropeId) return;
        bool play = animate && Motion.Enabled && IsVisible && !_lifted;
        _ropeId = ropeId;
        var old = _cards.OrderBy(XOf).ToList();
        for (int i = 0; i < old.Count; i++)
        {
            if (play) Leave(old[i], 25 * i, -SwitchSlide);
            else RemoveCard(old[i]);
        }
        if (!play) DropLeavers();
        foreach (var item in _app.Library.Items.Where(OnLine).ToList())
        {
            var c = AddCard(item, animate: false);
            if (play) SlideIn(c);
        }
        UpdateTag(name: !play);
        if (play) FlipTag();
        else { _flipSeq++; _tag.Flip.BeginAnimation(ScaleTransform.ScaleYProperty, null); _tag.Flip.ScaleY = 1; }
        HoldOpen(1800);
    }

    private void SlideIn(CardView c)
    {
        double left = Canvas.GetLeft(c);
        var dur = TimeSpan.FromMilliseconds(260);
        var slide = new DoubleAnimation(left + SwitchSlide, left, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        slide.Completed += (_, _) =>
        {
            if (!_cards.Contains(c)) return; // already leaving again: its newer animations own the values
            c.BeginAnimation(Canvas.LeftProperty, null);
            c.BeginAnimation(OpacityProperty, null);
        };
        c.BeginAnimation(Canvas.LeftProperty, slide);
        c.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
        Kick(c, (Random.Shared.NextDouble() < .5 ? -1 : 1) * 40);
    }

    /// <summary>ScaleY 1 → 0 → 1 over 200 ms; the name changes while the tag is edge-on.</summary>
    private void FlipTag()
    {
        int seq = ++_flipSeq;
        var half = TimeSpan.FromMilliseconds(100);
        var down = new DoubleAnimation(0, half) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        down.Completed += (_, _) =>
        {
            if (seq != _flipSeq) return; // a newer switch took over
            UpdateTag();
            var up = new DoubleAnimation(0, 1, half) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            up.Completed += (_, _) =>
            {
                if (seq != _flipSeq) return;
                _tag.Flip.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                _tag.Flip.ScaleY = 1;
            };
            _tag.Flip.BeginAnimation(ScaleTransform.ScaleYProperty, up);
        };
        _tag.Flip.BeginAnimation(ScaleTransform.ScaleYProperty, down);
    }

    public void Kick(HangItem item, double v) { var c = CardOf(item); if (c != null) Kick(c, v); }

    /// <summary>The card drops off the line (used when discarding from a menu or the panel).</summary>
    public void LetGo(HangItem item)
    {
        var c = CardOf(item);
        if (c == null) return;
        var ghost = new GhostWindow(c);
        ghost.ShowAt(Canvas.GetLeft(c), Canvas.GetTop(c) + c.Slide.Y, c.Angle);
        c.Visibility = Visibility.Hidden;
        ghost.Fall();
    }

    // ───────────── out of sight ─────────────

    public void HoldOpen(int ms)
    {
        if (_lifted) return;
        if (!IsVisible && StaysOut) Show();
        _holdOpenUntil = DateTime.Now.AddMilliseconds(ms);
        SetExpanded(true);
    }

    public bool IsLifted => _lifted;

    private static double LiftedY => -(BandHeight + 40);

    /// <summary>Takes everything in (cards rise out of sight, window hides) or hangs it back out.</summary>
    public void SetLifted(bool lifted, bool animate)
    {
        if (lifted == _lifted) return;
        _lifted = lifted;
        _shrink.Stop();
        bool play = animate && Motion.Enabled && (IsVisible || !lifted); // hanging out starts from a hidden window
        DropLeavers();
        var order = _cards.OrderBy(XOf).ToList();
        var ropes = new UIElement[] { _rope, _ropeShadow, _ropeTwist, _tag };

        if (lifted)
        {
            if (!play || order.Count == 0)
            {
                foreach (var c in order) { c.Slide.BeginAnimation(TranslateTransform.YProperty, null); c.Slide.Y = LiftedY; }
                foreach (var r in ropes) { r.BeginAnimation(OpacityProperty, null); r.Opacity = 0; }
                Hide();
                return;
            }
            var dur = TimeSpan.FromMilliseconds(280);
            for (int i = 0; i < order.Count; i++)
            {
                var c = order[i];
                double from = c.Slide.Y;
                c.Slide.Y = from; // base value holds the card in place until its staggered turn
                var a = new DoubleAnimation(from, LiftedY, dur) { BeginTime = TimeSpan.FromMilliseconds(30 * i), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
                if (i == order.Count - 1)
                    a.Completed += (_, _) =>
                    {
                        if (!_lifted) return;
                        foreach (var o in order) { o.Slide.BeginAnimation(TranslateTransform.YProperty, null); o.Slide.Y = LiftedY; }
                        foreach (var r in ropes) { r.BeginAnimation(OpacityProperty, null); r.Opacity = 0; }
                        Hide(); // nothing left running: no clocks, no timers
                    };
                c.Slide.BeginAnimation(TranslateTransform.YProperty, a);
            }
            foreach (var r in ropes) { double o = r.Opacity; r.Opacity = o; r.BeginAnimation(OpacityProperty, new DoubleAnimation(o, 0, dur)); }
            return;
        }

        _expanded = true;
        Height = BandHeight;
        if (StaysOut && !IsVisible) Show();
        _tag.Slide.BeginAnimation(TranslateTransform.YProperty, null);
        _tag.Slide.Y = 0;
        foreach (var r in ropes)
        {
            r.Opacity = 1;
            r.BeginAnimation(OpacityProperty, play ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) : null);
        }
        for (int i = 0; i < order.Count; i++)
        {
            var c = order[i];
            if (!play) { c.Slide.BeginAnimation(TranslateTransform.YProperty, null); c.Slide.Y = 0; continue; }
            c.Angle = 22;
            c.Slide.Y = LiftedY;
            c.Slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(LiftedY, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(30 * i), EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
        }
        if (play) StartPhysics();
        HoldOpen(2400);
    }

    private bool Busy => _pressed != null || MenuOpen || _app.HasOverlay;

    private void UpdateHover()
    {
        if (!IsVisible) return;
        if (_lifted) return;
        if (!AutoHide || Busy || DateTime.Now < _holdOpenUntil) { SetExpanded(true); _outsideSince = DateTime.MinValue; return; }
        if (_cards.Count == 0) { SetExpanded(false); return; }

        var p = Native.CursorDip(this);
        bool inside = p.X >= 0 && p.X <= LineWidth;
        if (!_expanded)
        {
            bool overCard = _cards.Any(c => Math.Abs(p.X - XOf(c)) <= c.Width / 2 + 8) || (_tag.IsVisible && p.X <= TagX + _tag.ActualWidth + 8);
            if (inside && (p.Y <= 1.5 || (p.Y <= Peek + 2 && overCard))) SetExpanded(true);
            return;
        }
        double bottom = _cards.Max(c => RopeAt(XOf(c)) - 9 + c.Height) + 24;
        if (inside && p.Y <= bottom) { _outsideSince = DateTime.MinValue; return; }
        if (_outsideSince == DateTime.MinValue) _outsideSince = DateTime.Now;
        else if ((DateTime.Now - _outsideSince).TotalMilliseconds > 600) SetExpanded(false);
    }

    private void SetExpanded(bool on)
    {
        if (_lifted || on == _expanded) return;
        _expanded = on;
        _shrink.Stop();
        if (on) Height = BandHeight;
        var dur = TimeSpan.FromMilliseconds(on ? 260 : 320);
        IEasingFunction ease = on ? new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseInOut };
        foreach (var c in _cards)
        {
            c.Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? 0 : CollapsedOffset(c), dur) { EasingFunction = ease });
            if (on) Kick(c, (Random.Shared.NextDouble() - .5) * 46);
        }
        _tag.Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? 0 : TagCollapsedOffset, dur) { EasingFunction = ease });
        var fade = new DoubleAnimation(on ? 1 : 0, dur);
        _rope.BeginAnimation(OpacityProperty, fade);
        _ropeShadow.BeginAnimation(OpacityProperty, fade);
        _ropeTwist.BeginAnimation(OpacityProperty, fade);
        if (!on) _shrink.Start();
    }

    // ───────────── physics ─────────────

    private void Kick(CardView c, double v) { c.Impulse(v); StartPhysics(); }

    private void StartPhysics()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (t - _lastFrame).TotalSeconds);
        _lastFrame = t;
        if (dt <= 0) return;
        bool any = false;
        foreach (var c in _cards) any |= c.Step(dt);
        if (any) return;
        CompositionTarget.Rendering -= OnFrame; // stop rendering entirely when nothing moves
        _animating = false;
    }

    // ───────────── click / hold / drag ─────────────

    private void Card_Down(object sender, MouseButtonEventArgs e)
    {
        var c = (CardView)sender;
        _pressed = c;
        _pressPoint = e.GetPosition(this);
        _pressX = XOf(c);
        _lastDragX = _pressPoint.X;
        _dragging = _holdFired = false;
        foreach (var o in _cards) Panel.SetZIndex(o, o == c ? 10 : 0);
        c.CaptureMouse();
        _hold.Stop();
        _hold.Start();
        e.Handled = true;
    }

    private void Card_Move(object sender, MouseEventArgs e)
    {
        var c = (CardView)sender;
        if (_pressed != c || _holdFired || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        var d = p - _pressPoint;
        if (!_dragging && (Math.Abs(d.X) > DragThreshold || Math.Abs(d.Y) > DragThreshold)) { _dragging = true; _hold.Stop(); }
        if (!_dragging) return;

        if (p.Y > RopeAt(XOf(c)) + c.Height + 18 || p.Y < -4)
        {
            ShareDrag(c);
            return;
        }
        double x = Math.Clamp(_pressX + d.X, c.Width / 2 + 6, LineWidth - c.Width / 2 - 6);
        Kick(c, -(p.X - _lastDragX) * 7);
        _lastDragX = p.X;
        c.Item.Position = x / LineWidth;
        LayoutCard(c);
        e.Handled = true;
    }

    private void Card_Up(object sender, MouseButtonEventArgs e)
    {
        var c = (CardView)sender;
        if (_pressed != c) return;
        _hold.Stop();
        c.ReleaseMouseCapture();
        _pressed = null;
        e.Handled = true;
        if (_holdFired) return;
        if (_dragging) { _app.Library.Touch(c.Item); return; }
        _app.Spotlight(c.Item);
    }

    private void OnHold()
    {
        _hold.Stop();
        var c = _pressed;
        if (c == null || _dragging) return;
        _holdFired = true;
        c.ReleaseMouseCapture();
        _pressed = null;
        c.Pulse();
        if (c.Item.Kind == ItemKind.Image) _app.Markup(c.Item);
        else _app.Spotlight(c.Item);
    }

    /// <summary>Pulled off the line: drag the file into another app, or drop it on nothing to let it go.</summary>
    private void ShareDrag(CardView c)
    {
        _hold.Stop();
        c.ReleaseMouseCapture();
        _pressed = null;
        _dragging = false;
        c.Item.Position = _pressX / LineWidth;
        LayoutCard(c);

        string? path = c.Item.Kind == ItemKind.Image ? c.Item.File : Images.NoteToTempFile(c.Item);
        if (path == null || !File.Exists(path)) return;
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { path });
        if (c.Item.Kind == ItemKind.Note) data.SetText(c.Item.Text);

        var ghost = new GhostWindow(c);
        ghost.Show();
        ghost.FollowCursor();
        c.Opacity = 0.2;

        bool cancelled = false;
        GiveFeedbackEventHandler fb = (_, _) => ghost.FollowCursor();
        QueryContinueDragEventHandler qc = (_, ev) => { if (ev.EscapePressed) { cancelled = true; ev.Action = DragAction.Cancel; } };
        c.GiveFeedback += fb;
        c.QueryContinueDrag += qc;
        DragDropEffects result;
        try { result = DragDrop.DoDragDrop(c, data, DragDropEffects.Copy); }
        catch (Exception ex) { Log.Warn("Drag and drop failed", ex); result = DragDropEffects.None; cancelled = true; }
        finally { c.GiveFeedback -= fb; c.QueryContinueDrag -= qc; }

        if (result == DragDropEffects.None && !cancelled)
        {
            ghost.Fall();
            _app.Library.Discard(c.Item);
        }
        else
        {
            ghost.Close();
            c.Opacity = 1;
            Kick(c, 60);
            if (!cancelled) c.Toast(L.T("toast.sent"));
        }
    }
}
