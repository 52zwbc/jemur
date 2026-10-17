using Jemur.Core;

namespace Jemur.App;

internal sealed class UpdateCoordinator : IDisposable
{
    private readonly IUpdateChecker _checker;
    private readonly Action<string, string, bool> _notify;
    private readonly Action _showPanel;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _notifiedVersions = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loop;
    private Task<UpdateCheckResult>? _inFlight;
    private bool _disposed;

    public UpdateCoordinator(
        IUpdateChecker checker,
        Action<string, string, bool> notify,
        Action showPanel,
        TimeProvider clock,
        TimeSpan initialDelay,
        TimeSpan interval)
        : this(checker, notify, showPanel, clock, initialDelay, interval, static (delay, token) => Task.Delay(delay, token)) { }

    internal UpdateCoordinator(
        IUpdateChecker checker,
        Action<string, string, bool> notify,
        Action showPanel,
        TimeProvider clock,
        TimeSpan initialDelay,
        TimeSpan interval,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _checker = checker;
        _notify = notify;
        _showPanel = showPanel;
        _clock = clock;
        _initialDelay = initialDelay;
        _interval = interval;
        _delay = delay;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _loop != null) return;
            _loop = RunLoopAsync();
        }
    }

    public Task<UpdateCheckResult> CheckNowAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed) return Task.FromResult(UpdateCheckResult.Failed("Update checks are stopped"));
            if (_inFlight is { IsCompleted: false }) return _inFlight;
            _inFlight = CheckAndNotifyAsync(cancellationToken);
            return _inFlight;
        }
    }

    private async Task RunLoopAsync()
    {
        try
        {
            await _delay(_initialDelay, _stop.Token);
            while (!_stop.IsCancellationRequested)
            {
                await CheckNowAsync(_stop.Token);
                await _delay(_interval, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { Log.Warn("Background update loop stopped", ex); }
    }

    private async Task<UpdateCheckResult> CheckAndNotifyAsync(CancellationToken cancellationToken)
    {
        var result = await _checker.CheckAsync(cancellationToken);
        if (result.Kind == UpdateCheckKind.UpdateAvailable && result.Manifest is { } manifest && _notifiedVersions.Add(manifest.Version))
        {
            _notify("Jemur", $"Jemur {manifest.Version} is available. Open Jemur to download it.", false);
        }
        return result;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
        }
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _stop.Dispose();
    }
}
