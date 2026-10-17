using System.IO;
using System.Text.Json;

namespace Jemur.Core;

/// <summary>
/// The model: what is on the line and what has been discarded. Views (the line, the control panel)
/// observe it; nothing here knows about windows. All members must be called on the UI thread.
/// </summary>
internal sealed class Library
{
    private sealed class FileShape
    {
        public int Version { get; set; } = 1;
        public List<HangItem> Items { get; set; } = new();
        public List<HangItem> Discarded { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly List<HangItem> _items = new();
    private readonly List<HangItem> _discarded = new();
    private readonly SynchronizationContext? _ui;
    private Timer? _saveTimer;

    public IReadOnlyList<HangItem> Items => _items;
    public IReadOnlyList<HangItem> Discarded => _discarded;

    public event Action<HangItem>? Added;
    /// <summary>Raised when an item leaves the line (discarded or deleted).</summary>
    public event Action<HangItem>? Removed;
    public event Action<HangItem>? Updated;
    /// <summary>Raised after any change, including to the discarded pile.</summary>
    public event Action? Changed;

    public Library() => _ui = SynchronizationContext.Current;

    // ───────────── persistence ─────────────

    public void Load(int keepDiscardedDays)
    {
        try
        {
            if (!File.Exists(AppPaths.ItemsFile)) return;
            var json = File.ReadAllText(AppPaths.ItemsFile);
            FileShape shape;
            if (json.TrimStart().StartsWith('['))
                shape = new FileShape { Items = JsonSerializer.Deserialize<List<HangItem>>(json) ?? new() }; // pre-0.1 format
            else
                shape = JsonSerializer.Deserialize<FileShape>(json) ?? new();

            foreach (var i in shape.Items.Concat(shape.Discarded)) i.File = AppPaths.Rebase(i.File);
            _items.AddRange(shape.Items.Where(IsUsable));
            _discarded.AddRange(shape.Discarded.Where(IsUsable));
            PurgeDiscarded(keepDiscardedDays);
        }
        catch (Exception ex)
        {
            Log.Error("Could not read items.json; starting with an empty line", ex);
            TryBackupCorrupt();
        }
    }

    private static bool IsUsable(HangItem i) => i.Kind == ItemKind.Note || (i.File != null && File.Exists(i.File));

    private static void TryBackupCorrupt()
    {
        try { File.Copy(AppPaths.ItemsFile, AppPaths.ItemsFile + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true); } catch { }
    }

    /// <summary>Coalesces bursts of edits (typing, dragging) into one write.</summary>
    public void ScheduleSave()
    {
        _saveTimer ??= new Timer(_ => Post(SaveNow));
        _saveTimer.Change(500, Timeout.Infinite);
    }

    public void SaveNow()
    {
        _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        try
        {
            Directory.CreateDirectory(AppPaths.Data);
            var shape = new FileShape { Items = _items.ToList(), Discarded = _discarded.ToList() };
            var tmp = AppPaths.ItemsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(shape, Json));
            File.Move(tmp, AppPaths.ItemsFile, true);
        }
        catch (Exception ex) { Log.Error("Saving items failed", ex); }
    }

    private void Post(Action a)
    {
        if (_ui != null) _ui.Post(_ => a(), null);
        else a();
    }

    // ───────────── mutations ─────────────

    public void Add(HangItem item)
    {
        item.DiscardedAt = null;
        _items.Add(item);
        Added?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    public void Touch(HangItem item)
    {
        Updated?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    public void Discard(HangItem item)
    {
        if (!_items.Remove(item)) return;
        item.DiscardedAt = DateTime.Now;
        _discarded.Insert(0, item);
        Removed?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    /// <summary>Removes an item without keeping it (e.g. an empty note that was never written).</summary>
    public void Drop(HangItem item)
    {
        if (!_items.Remove(item)) return;
        DeleteFile(item);
        Removed?.Invoke(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    public void DiscardAll()
    {
        foreach (var i in _items.ToList()) Discard(i);
    }

    public void Restore(HangItem item)
    {
        if (!_discarded.Remove(item)) return;
        if (item.Kind == ItemKind.Image && (item.File == null || !File.Exists(item.File))) { Changed?.Invoke(); ScheduleSave(); return; }
        Add(item);
    }

    public void DeleteForever(HangItem item)
    {
        if (!_discarded.Remove(item)) return;
        DeleteFile(item);
        Changed?.Invoke();
        ScheduleSave();
    }

    public void EmptyDiscarded()
    {
        foreach (var i in _discarded) DeleteFile(i);
        _discarded.Clear();
        Changed?.Invoke();
        ScheduleSave();
    }

    public void PurgeDiscarded(int days)
    {
        var cutoff = DateTime.Now.AddDays(-Math.Max(1, days));
        foreach (var i in _discarded.Where(i => i.DiscardedAt < cutoff).ToList())
        {
            _discarded.Remove(i);
            DeleteFile(i);
        }
    }

    private void DeleteFile(HangItem item)
    {
        // Only ever delete PNGs we created, and only if nothing else still points at them.
        if (item.Kind != ItemKind.Image || !AppPaths.IsOwnedShot(item.File)) return;
        if (_items.Concat(_discarded).Any(o => o != item && string.Equals(o.File, item.File, StringComparison.OrdinalIgnoreCase))) return;
        try { File.Delete(item.File!); } catch (Exception ex) { Log.Warn($"Could not delete {item.File}", ex); }
    }
}
