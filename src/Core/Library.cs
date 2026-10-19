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
        public int Version { get; set; } = 2;
        public List<Rope> Ropes { get; set; } = new();
        public List<CardGroup> Groups { get; set; } = new();
        public List<HangItem> Items { get; set; } = new();
        public List<HangItem> Discarded { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly List<HangItem> _items = new();
    private readonly List<HangItem> _discarded = new();
    private readonly SynchronizationContext? _ui;
    private readonly List<Rope> _ropes = new();
    private readonly List<CardGroup> _groups = new();
    private Timer? _saveTimer;
    private bool _backupV1;

    public const int MaxPinsPerRope = 3;

    public IReadOnlyList<Rope> Ropes => _ropes;
    public IReadOnlyList<CardGroup> Groups => _groups;
    public IEnumerable<HangItem> ItemsOn(string ropeId) => _items.Where(i => i.RopeId == ropeId);
    public Rope? RopeById(string id) => _ropes.FirstOrDefault(r => r.Id == id);
    public IReadOnlyList<HangItem> Items => _items;
    public IReadOnlyList<HangItem> Discarded => _discarded;

    public event Action<HangItem>? Added;
    /// <summary>Raised when an item leaves the line (discarded or deleted).</summary>
    public event Action<HangItem>? Removed;
    public event Action<HangItem>? Updated;
    /// <summary>Raised after any change, including to the discarded pile.</summary>
    public event Action? Changed;
    /// <summary>Raised when an item's rope, pin state or group changes, so views reconcile.</summary>
    public event Action<HangItem>? Relocated;

    public Library() => _ui = SynchronizationContext.Current;

    // ───────────── persistence ─────────────

    public void Load(int keepDiscardedDays, string defaultRopeName = "Jemuran")
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
            _ropes.AddRange(shape.Ropes);
            _groups.AddRange(shape.Groups);
            _backupV1 = shape.Version < 2;
            PurgeDiscarded(keepDiscardedDays);
        }
        catch (Exception ex)
        {
            Log.Error("Could not read items.json; starting with an empty line", ex);
            TryBackupCorrupt();
        }
        finally { Repair(defaultRopeName); }
    }

    /// <summary>Guarantees the default rope exists and every item points at a real rope.</summary>
    private void Repair(string defaultRopeName)
    {
        if (RopeById(Rope.DefaultId) == null) _ropes.Insert(0, new Rope { Id = Rope.DefaultId, Name = defaultRopeName });
        foreach (var i in _items.Concat(_discarded)) EnsureRope(i);
    }

    private void EnsureRope(HangItem i)
    {
        if (i.RopeId == null || RopeById(i.RopeId) == null) i.RopeId = Rope.DefaultId;
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
            if (_backupV1)
            {
                // Keep the pre-ropes file once, so a downgrade or a bug never costs anyone their line.
                var bak = Path.Combine(Path.GetDirectoryName(AppPaths.ItemsFile)!, "items.v1-backup.json");
                if (File.Exists(AppPaths.ItemsFile) && !File.Exists(bak)) File.Copy(AppPaths.ItemsFile, bak);
                _backupV1 = false;
            }
            var shape = new FileShape { Ropes = _ropes.ToList(), Groups = _groups.ToList(), Items = _items.ToList(), Discarded = _discarded.ToList() };
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
        EnsureRope(item);
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

    /// <summary>Discards the unpinned items of one rope.</summary>
    public void DiscardAll(string ropeId)
    {
        foreach (var i in _items.Where(i => i.RopeId == ropeId && !i.Pinned).ToList()) Discard(i);
    }

    // ───────────── ropes ─────────────

    public Rope AddRope(string name)
    {
        var r = new Rope { Name = name };
        _ropes.Add(r);
        Changed?.Invoke();
        ScheduleSave();
        return r;
    }

    public void RenameRope(string id, string name)
    {
        var r = RopeById(id);
        if (r == null) return;
        r.Name = name;
        Changed?.Invoke();
        ScheduleSave();
    }

    /// <summary>Removes a rope; its items (live and discarded) go to the default rope.</summary>
    public bool DeleteRope(string id)
    {
        var r = RopeById(id);
        if (r == null || id == Rope.DefaultId) return false;
        _ropes.Remove(r);
        _groups.RemoveAll(g => g.RopeId == id);
        foreach (var i in _discarded.Where(i => i.RopeId == id)) i.RopeId = Rope.DefaultId;
        foreach (var i in _items.Where(i => i.RopeId == id).ToList()) Relocate(i, Rope.DefaultId);
        Changed?.Invoke();
        ScheduleSave();
        return true;
    }

    public void MoveToRope(HangItem item, string ropeId)
    {
        if (RopeById(ropeId) == null) return;
        Relocate(item, ropeId);
        Changed?.Invoke();
        ScheduleSave();
    }

    private void Relocate(HangItem item, string ropeId)
    {
        item.RopeId = ropeId;
        item.Position = -1;
        Relocated?.Invoke(item);
    }

    public void Restore(HangItem item)
    {
        if (!_discarded.Remove(item)) return;
        EnsureRope(item);
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
