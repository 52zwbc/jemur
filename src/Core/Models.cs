using System.Text.Json.Serialization;

namespace Jemur.Core;

public enum ItemKind { Image, Note }

/// <summary>Something hanging on the line (or lying in the discarded pile).</summary>
public sealed class HangItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ItemKind Kind { get; set; }
    /// <summary>PNG on disk for screenshots.</summary>
    public string? File { get; set; }
    public string Text { get; set; } = "";
    /// <summary>Horizontal position along the line, 0..1, so the layout survives resolution changes.</summary>
    public double Position { get; set; } = -1;
    public int Color { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime? DiscardedAt { get; set; }
    /// <summary>The rope this item hangs on. Null only in files written before ropes existed; Library repairs it on load.</summary>
    public string? RopeId { get; set; }
    /// <summary>Pinned items sit on the desktop at PinLeft/PinTop instead of on the rope.</summary>
    public bool Pinned { get; set; }
    public double PinLeft { get; set; }
    public double PinTop { get; set; }
}

/// <summary>A named line that items hang on.</summary>
public sealed class Rope
{
    public const string DefaultId = "default";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>A set of cards that move together. ItemIds is the left-to-right order.</summary>
public sealed class CardGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RopeId { get; set; } = Rope.DefaultId;
    public string Name { get; set; } = "";
    public double Position { get; set; } = -1;
    public bool Folded { get; set; }
    public List<string> ItemIds { get; set; } = new();
}

/// <summary>Named actions that can be bound to a global shortcut.</summary>
public static class Actions
{
    public const string Screenshot = "screenshot";
    public const string Note = "note";
    public const string Paste = "paste";
    public const string Toggle = "toggle";
    public const string Panel = "panel";
    public const string NextRope = "rope";
    public static readonly string[] All = { Screenshot, Note, Paste, Toggle, Panel, NextRope };
}

public sealed class Settings
{
    public int Version { get; set; } = 1;
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    /// <summary>"auto" or a locale code such as "en" or "id".</summary>
    public string Language { get; set; } = "auto";
    public bool AutoHide { get; set; } = true;
    /// <summary>0 small, 1 medium, 2 large.</summary>
    public int CardSize { get; set; } = 1;
    /// <summary>How long discarded items are kept before they are deleted for good.</summary>
    public int KeepDiscardedDays { get; set; } = 7;
    public Dictionary<string, string> Hotkeys { get; set; } = DefaultHotkeys();
    public bool Welcomed { get; set; }
    public string ActiveRope { get; set; } = Rope.DefaultId;
    public bool Lifted { get; set; }

    public static Dictionary<string, string> DefaultHotkeys() => new()
    {
        [Actions.Screenshot] = "Alt+Shift+S",
        [Actions.Note] = "Alt+Shift+N",
        [Actions.Paste] = "Alt+Shift+V",
        [Actions.Toggle] = "Alt+Shift+J",
        [Actions.Panel] = "Alt+Shift+P",
        [Actions.NextRope] = "Alt+Shift+G",
    };

    /// <summary>Fills in anything missing after loading a file written by an older version.</summary>
    public void Normalize()
    {
        Hotkeys ??= new();
        foreach (var (k, v) in DefaultHotkeys())
            if (!Hotkeys.ContainsKey(k)) Hotkeys[k] = v;
        CardSize = Math.Clamp(CardSize, 0, 2);
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (KeepDiscardedDays is not (1 or 7 or 30)) KeepDiscardedDays = 7;
        if (string.IsNullOrWhiteSpace(Language)) Language = "auto";
        if (string.IsNullOrWhiteSpace(ActiveRope)) ActiveRope = Rope.DefaultId;
    }
}
