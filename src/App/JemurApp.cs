using System.IO;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Jemur.Core;
using Jemur.Interop;
using Jemur.UI;
using Jemur.Windows;
using Microsoft.Win32;

namespace Jemur.App;

/// <summary>Owns the long-lived pieces (library, line, tray, shortcuts) and every user-facing action.</summary>
internal sealed class JemurApp
{
    public const string RepoUrl = "https://github.com/revanapriyandi/jemur";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly double[] CardScales = { 0.85, 1.0, 1.22 };

    public static string Version { get; } =
        typeof(JemurApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private readonly Application _wpf;
    private readonly bool _background;
    private HotkeyManager _hotkeys = null!;
    private TrayIcon _tray = null!;
    private ControlPanel? _panel;
    private SpotlightWindow? _spotlight;
    private MarkupWindow? _markup;
    private bool _capturing;

    public Settings Settings { get; private set; } = new();
    public Library Library { get; } = new();
    public LineWindow Line { get; private set; } = null!;
    public bool HasOverlay => _spotlight != null || _markup != null || _capturing;

    public JemurApp(Application wpf, bool background, string? renderDocs = null)
    {
        _wpf = wpf;
        _background = background;
        _renderDocs = renderDocs;
    }

    private readonly string? _renderDocs;

    /// <summary>
    /// Developer tool: <c>Jemur.exe --render-docs &lt;folder&gt;</c> renders the control panel in dark and light
    /// theme straight to PNG, without touching the screen. Combine with JEMUR_DATA to use demo content.
    /// </summary>
    private void RenderDocs(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var mode in new[] { "dark", "light" })
        {
            Theme.SetMode(mode);
            var panel = new ControlPanel(this) { Left = -32000, Top = -32000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            panel.Show();
            panel.RenderPages(dir, mode);
            panel.Close();

            var shot = Library.Items.FirstOrDefault(i => i.Kind == ItemKind.Image)?.File;
            if (shot != null)
            {
                var markup = new MarkupWindow(shot) { Left = -32000, Top = -32000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Width = 1100, Height = 760 };
                markup.Show();
                var root = (System.Windows.Controls.Panel)markup.Content;
                root.Background = Theme.Window;
                root.UpdateLayout();
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(root.ActualWidth * 1.5), (int)(root.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                rtb.Render(root);
                Images.SavePng(rtb, Path.Combine(dir, $"markup-{mode}.png"));
                markup.Close();
            }
        }
        Log.Info($"Rendered docs to {dir}");
        _wpf.Shutdown();
    }

    // ───────────── lifecycle ─────────────

    public void Start()
    {
        AppPaths.Ensure();
        Log.Info($"Jemur {Version} starting on Windows {Environment.OSVersion.Version}");
        Settings = SettingsStore.Load();
        L.Init(Settings.Language);
        if (_renderDocs != null)
        {
            _wpf.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/UI/Controls.xaml") });
            Theme.Init(Settings.Theme);
            Library.Load(Settings.KeepDiscardedDays);
            Line = new LineWindow(this);
            _hotkeys = new HotkeyManager();
            _tray = new TrayIcon();
            RenderDocs(_renderDocs);
            return;
        }
        _wpf.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/UI/Controls.xaml") });
        Theme.Init(Settings.Theme);
        CardView.Scale = CardScales[Settings.CardSize];

        Library.Load(Settings.KeepDiscardedDays);
        Line = new LineWindow(this) { AutoHide = Settings.AutoHide };
        if (Library.Items.Count > 0) Line.Show();
        Library.Changed += () => _tray?.SetTip(TrayTip());

        _tray = new TrayIcon();
        _tray.Click += () => ShowPanel();
        _tray.RightClick += ShowTrayMenu;
        _tray.Show(Theme.IsTaskbarLight(), TrayTip());
        Theme.Changed += () => _tray.SetIcon(Theme.IsTaskbarLight());
        L.Changed += () => _tray.SetTip(TrayTip());

        _hotkeys = new HotkeyManager();
        var failed = BindHotkeys();
        if (failed.Count > 0) _tray.Notify("Jemur", L.F("tray.shortcutsTaken", string.Join(", ", failed)), warning: true);

        ListenForSecondInstance();

        if (!Settings.Welcomed)
        {
            Settings.Welcomed = true;
            SettingsStore.Save(Settings);
            ShowPanel();
        }
        else if (!_background && Library.Items.Count == 0) _tray.Notify("Jemur", L.F("tray.running", HotkeyText(Actions.Screenshot)));

        Memory.TrimSoon(4000);
        Memory.Report("started");
    }

    public void Stop()
    {
        if (_renderDocs == null) Library.SaveNow();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Log.Info("Jemur stopped");
    }

    public void Quit() => _wpf.Shutdown();

    private string TrayTip() => Library.Items.Count == 0 ? "Jemur" : $"Jemur · {L.N("tray.items", Library.Items.Count)}";

    private void ListenForSecondInstance()
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowPanelEvent);
        new Thread(() => { while (true) { ev.WaitOne(); _wpf.Dispatcher.BeginInvoke(() => ShowPanel()); } }) { IsBackground = true, Name = "Jemur.Activation" }.Start();
    }

    // ───────────── shortcuts ─────────────

    private Action HandlerFor(string action) => action switch
    {
        Actions.Screenshot => () => _ = TakeScreenshot(),
        Actions.Note => NewNote,
        Actions.Paste => HangClipboard,
        Actions.Toggle => () => Line.TogglePinned(),
        _ => () => ShowPanel(),
    };

    private List<string> BindHotkeys()
    {
        var failed = new List<string>();
        foreach (var a in Actions.All)
        {
            if (!Settings.Hotkeys.TryGetValue(a, out var s) || string.IsNullOrEmpty(s)) continue;
            if (!Hotkey.TryParse(s, out var hk) || !_hotkeys.Bind(a, hk, HandlerFor(a))) failed.Add(s);
        }
        return failed;
    }

    public string HotkeyText(string action) =>
        Settings.Hotkeys.TryGetValue(action, out var s) && Hotkey.TryParse(s, out var hk) ? hk.ToString() : "";

    /// <summary>Returns false when the combination is already taken by another app.</summary>
    public bool SetHotkey(string action, Hotkey? key)
    {
        if (key is not { } k)
        {
            _hotkeys.Unbind(action);
            Settings.Hotkeys[action] = "";
            SettingsStore.Save(Settings);
            return true;
        }
        // Clear the same combination from any other action first.
        foreach (var other in Actions.All.Where(o => o != action && Settings.Hotkeys.TryGetValue(o, out var s) && Hotkey.TryParse(s, out var h) && h == k).ToList())
        {
            _hotkeys.Unbind(other);
            Settings.Hotkeys[other] = "";
        }
        if (!_hotkeys.Bind(action, k, HandlerFor(action)))
        {
            if (Hotkey.TryParse(Settings.Hotkeys.GetValueOrDefault(action), out var old)) _hotkeys.Bind(action, old, HandlerFor(action));
            return false;
        }
        Settings.Hotkeys[action] = k.ToString();
        SettingsStore.Save(Settings);
        return true;
    }

    public void SuspendHotkeys(bool suspend) =>
        _hotkeys.Suspend(suspend, a => Settings.Hotkeys.TryGetValue(a, out var s) && Hotkey.TryParse(s, out var hk) ? (hk, HandlerFor(a)) : null);

    public void ResetHotkeys()
    {
        Settings.Hotkeys = Settings.DefaultHotkeys();
        SettingsStore.Save(Settings);
        foreach (var a in Actions.All) _hotkeys.Unbind(a);
        var failed = BindHotkeys();
        if (failed.Count > 0) _tray.Notify("Jemur", L.F("tray.shortcutsTaken", string.Join(", ", failed)), warning: true);
    }

    // ───────────── settings ─────────────

    public void SetTheme(string mode) { Settings.Theme = mode; SettingsStore.Save(Settings); Theme.SetMode(mode); }

    public void SetLanguage(string code) { Settings.Language = code; SettingsStore.Save(Settings); L.Use(code); Line.Rebuild(); }

    public void SetCardSize(int size)
    {
        Settings.CardSize = Math.Clamp(size, 0, 2);
        CardView.Scale = CardScales[Settings.CardSize];
        SettingsStore.Save(Settings);
        Line.Rebuild();
    }

    public void SetAutoHide(bool on) { Settings.AutoHide = on; Line.AutoHide = on; SettingsStore.Save(Settings); }

    public void SetKeepDays(int days) { Settings.KeepDiscardedDays = days; SettingsStore.Save(Settings); Library.PurgeDiscarded(days); Library.ScheduleSave(); }

    public bool IsStartupEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("Jemur") != null;
    }

    public void SetStartup(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue("Jemur", $"\"{Environment.ProcessPath}\" --background");
            else k.DeleteValue("Jemur", false);
        }
        catch (Exception ex) { Log.Error("Could not change startup setting", ex); }
    }

    // ───────────── capture & creation ─────────────

    public async Task TakeScreenshot()
    {
        if (_capturing) return;
        _capturing = true;
        bool panelVisible = _panel is { IsVisible: true, WindowState: not WindowState.Minimized };
        try
        {
            _spotlight?.Dismiss();
            if (panelVisible) _panel!.Hide();
            bool lineVisible = Line.IsVisible;
            Line.Hide();
            await Task.Delay(150); // let DWM repaint without our windows

            var (shot, bounds) = ScreenCapture.MonitorUnderCursor();
            var windows = ScreenCapture.VisibleWindows(IntPtr.Zero);
            if (lineVisible || Library.Items.Count > 0) Line.Show();

            var w = new CaptureWindow(shot, bounds, windows);
            w.ShowDialog();
            if (w.Result is { } result)
            {
                var path = Images.NewShotPath();
                Images.SavePng(result, path);
                Library.Add(new HangItem { Kind = ItemKind.Image, File = path });
            }
        }
        catch (Exception ex)
        {
            Log.Error("Screenshot failed", ex);
            _tray.Notify("Jemur", L.F("error.screenshot", ex.Message), warning: true);
        }
        finally
        {
            _capturing = false;
            if (panelVisible) _panel?.Show();
            Memory.TrimSoon(); // the full-screen bitmap is the biggest allocation Jemur ever makes
        }
    }

    public void NewNote()
    {
        var item = new HangItem { Kind = ItemKind.Note, Color = Random.Shared.Next(NoteColors.Count) };
        Library.Add(item);
        _wpf.Dispatcher.BeginInvoke(() => Spotlight(item), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void HangClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } img)
            {
                var path = Images.NewShotPath();
                Images.SavePng(img, path);
                Library.Add(new HangItem { Kind = ItemKind.Image, File = path });
                return;
            }
            if (Clipboard.ContainsFileDropList())
            {
                string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };
                bool any = false;
                foreach (var f in Clipboard.GetFileDropList().Cast<string?>())
                {
                    if (f == null || !exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    var path = Images.NewShotPath();
                    Images.SavePng(Images.Load(f), path);
                    Library.Add(new HangItem { Kind = ItemKind.Image, File = path });
                    any = true;
                }
                if (any) return;
            }
            if (Clipboard.ContainsText() && Clipboard.GetText().Trim() is { Length: > 0 } text)
            {
                Library.Add(new HangItem { Kind = ItemKind.Note, Text = text, Color = Random.Shared.Next(NoteColors.Count) });
                return;
            }
            _tray.Notify("Jemur", L.T("tray.clipboardEmpty"));
        }
        catch (Exception ex)
        {
            Log.Warn("Reading the clipboard failed", ex);
            _tray.Notify("Jemur", L.T("tray.clipboardFailed"), warning: true);
        }
        finally { Memory.TrimSoon(); }
    }

    // ───────────── item actions ─────────────

    public void Spotlight(HangItem item)
    {
        if (_spotlight != null) { _spotlight.Dismiss(() => Spotlight(item)); return; }
        if (!Library.Items.Contains(item)) return;
        Line.HoldOpen(800);
        Line.CardOf(item)?.SetAway(true);
        var w = new SpotlightWindow(item, this, Line.CardCenter(item));
        _spotlight = w;
        w.Finished += it =>
        {
            _spotlight = null;
            var c = Line.CardOf(it);
            c?.SetAway(false);
            if (c != null) Line.Kick(it, 70);
            if (it.Kind == ItemKind.Note && string.IsNullOrWhiteSpace(it.Text)) Library.Drop(it);
            Line.HoldOpen(1200);
            Memory.TrimSoon();
        };
        w.Show();
    }

    public void Markup(HangItem item)
    {
        if (item.File == null || !File.Exists(item.File)) return;
        _markup?.Close();
        var w = new MarkupWindow(item.File);
        _markup = w;
        w.Saved += () => { var c = Line.CardOf(item); if (c != null) { c.ReloadImage(); c.Toast(L.T("toast.saved")); } Library.Touch(item); };
        w.Closed += (_, _) => { _markup = null; Line.HoldOpen(1200); };
        w.Show();
        Native.ForceForeground(w);
    }

    public bool Copy(HangItem item)
    {
        try
        {
            var data = new DataObject();
            if (item.Kind == ItemKind.Image && item.File != null)
            {
                data.SetImage(Images.Load(item.File));
                data.SetFileDropList(new StringCollection { item.File });
            }
            else data.SetText(item.Text);
            Clipboard.SetDataObject(data, true); // copy=true: hand the data to Windows so we don't keep it alive
            Line.CardOf(item)?.Toast(L.T("toast.copied"));
            Memory.TrimSoon();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Copy failed", ex);
            Line.CardOf(item)?.Toast(L.T("toast.copyFailed"));
            return false;
        }
    }

    public void OpenFile(HangItem item)
    {
        if (item.File == null) return;
        try { Process.Start(new ProcessStartInfo(item.File) { UseShellExecute = true }); } catch (Exception ex) { Log.Warn("Open failed", ex); }
    }

    public void ShowInFolder(HangItem item)
    {
        if (item.File == null) return;
        try { Process.Start("explorer.exe", $"/select,\"{item.File}\""); } catch (Exception ex) { Log.Warn("Show in folder failed", ex); }
    }

    public void SaveAs(HangItem item, Window? owner = null)
    {
        if (item.File == null) return;
        var dlg = new SaveFileDialog { FileName = Path.GetFileName(item.File), Filter = "PNG|*.png", AddExtension = true };
        Line.MenuOpen = true;
        try { if (dlg.ShowDialog(owner) == true) File.Copy(item.File, dlg.FileName, true); }
        catch (Exception ex) { Log.Warn("Save as failed", ex); }
        finally { Line.MenuOpen = false; }
    }

    /// <summary>Lets the card drop off the line, then moves it to Discarded.</summary>
    public void Discard(HangItem item)
    {
        Line.LetGo(item);
        Library.Discard(item);
    }

    // ───────────── menus ─────────────

    public void ShowItemMenu(HangItem item, FrameworkElement target)
    {
        var m = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        bool img = item.Kind == ItemKind.Image;
        m.Items.Add(Ui.MenuItem(L.T(img ? "action.view" : "action.openNote"), () => Spotlight(item), img ? "" : ""));
        m.Items.Add(Ui.MenuItem(L.T("action.copy"), () => Copy(item), ""));
        if (img)
        {
            m.Items.Add(Ui.MenuItem(L.T("action.markup"), () => Markup(item), "", L.T("hint.hold")));
            m.Items.Add(Ui.MenuItem(L.T("action.open"), () => OpenFile(item), ""));
            m.Items.Add(Ui.MenuItem(L.T("action.saveAs"), () => SaveAs(item), ""));
            m.Items.Add(Ui.MenuItem(L.T("action.showInFolder"), () => ShowInFolder(item), ""));
        }
        else
        {
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            for (int i = 0; i < NoteColors.Count; i++)
            {
                int idx = i;
                var sw = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(item.Color == i ? 2 : 1),
                    Background = new SolidColorBrush(NoteColors.Paper(i)), ToolTip = L.T(NoteColors.Keys[i]),
                };
                sw.SetResourceReference(Border.BorderBrushProperty, item.Color == i ? "J.Text" : "J.ControlStrokeBottom");
                sw.PreviewMouseLeftButtonUp += (_, _) => { item.Color = idx; Library.Touch(item); };
                swatches.Children.Add(sw);
            }
            m.Items.Add(new MenuItem { Header = swatches, Icon = Ui.Glyph("", 16) });
        }
        m.Items.Add(new Separator());
        m.Items.Add(Ui.MenuItem(L.T("action.discard"), () => Discard(item), "", L.T("hint.dragOff"), critical: true));

        Line.MenuOpen = true;
        m.Closed += (_, _) => Line.MenuOpen = false;
        m.IsOpen = true;
    }

    private void ShowTrayMenu()
    {
        var m = new ContextMenu { Placement = PlacementMode.AbsolutePoint };
        Native.GetCursorPos(out var p);
        double scale = 96.0 / Native.GetDpiForSystem();
        m.HorizontalOffset = p.X * scale;
        m.VerticalOffset = p.Y * scale;

        m.Items.Add(Ui.MenuItem(L.T("action.screenshot"), () => _ = TakeScreenshot(), "", HotkeyText(Actions.Screenshot)));
        m.Items.Add(Ui.MenuItem(L.T("action.newNote"), NewNote, "", HotkeyText(Actions.Note)));
        m.Items.Add(Ui.MenuItem(L.T("action.paste"), HangClipboard, "", HotkeyText(Actions.Paste)));
        m.Items.Add(Ui.MenuItem(L.T("action.toggleLine"), () => Line.TogglePinned(), "", HotkeyText(Actions.Toggle)));
        m.Items.Add(new Separator());
        var restore = Ui.MenuItem(L.T("action.restoreLast"), () => { if (Library.Discarded.Count > 0) Library.Restore(Library.Discarded[0]); }, "");
        restore.IsEnabled = Library.Discarded.Count > 0;
        m.Items.Add(restore);
        m.Items.Add(Ui.MenuItem(L.T("action.controlPanel"), () => ShowPanel(), "", HotkeyText(Actions.Panel)));
        m.Items.Add(Ui.MenuItem(L.T("nav.settings"), () => ShowPanel(settings: true), ""));
        m.Items.Add(new Separator());
        m.Items.Add(Ui.MenuItem(L.T("action.quit"), Quit, ""));

        // A tray menu only dismisses on outside clicks if our process owns the foreground.
        Native.SetForegroundWindow(_tray.Hwnd);
        m.IsOpen = true;
    }

    public void ShowPanel(bool settings = false)
    {
        if (_panel == null)
        {
            _panel = new ControlPanel(this);
            _panel.Closed += (_, _) => _panel = null;
            _panel.Show();
        }
        if (_panel.WindowState == WindowState.Minimized) _panel.WindowState = WindowState.Normal;
        if (settings) _panel.ShowSettings();
        Native.ForceForeground(_panel);
    }
}
