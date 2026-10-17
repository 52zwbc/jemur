using System.Windows;
using System.Windows.Threading;
using Jemur.Core;

namespace Jemur.App;

public static class Program
{
    private const string MutexName = "Jemur.SingleInstance.v1";
    internal const string ShowPanelEvent = "Jemur.ShowPanel.v1";

    [STAThread]
    public static int Main(string[] args)
    {
        bool tool = args.Contains("--render-docs");
        using var mutex = new Mutex(true, tool ? MutexName + ".tool" : MutexName, out bool first);
        if (!first)
        {
            // Already running: bring up its control panel instead of starting a second copy.
            try { using var ev = EventWaitHandle.OpenExisting(ShowPanelEvent); ev.Set(); } catch { }
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += OnDispatcherException;
        int r = Array.IndexOf(args, "--render-docs");
        var jemur = new JemurApp(app, background: args.Contains("--background"), renderDocs: r >= 0 && r + 1 < args.Length ? args[r + 1] : null);
        app.Startup += (_, _) => jemur.Start();
        app.Exit += (_, _) => jemur.Stop();
        return app.Run();
    }

    private static int _crashes;

    /// <summary>Log and keep going: a tray utility disappearing silently is worse than one glitchy click.</summary>
    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("UI exception", e.Exception);
        e.Handled = ++_crashes < 20;
    }
}
