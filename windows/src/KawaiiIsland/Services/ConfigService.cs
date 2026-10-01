using System.IO;
using System.Text.Json;

namespace KawaiiIsland.Services;

/// <summary>
/// Loads/saves %LOCALAPPDATA%\KawaiiIsland\config.json. Local machine only (not roaming AppData, never cloud).
/// Saves are debounced (500 ms) and atomic (temp file + rename). A corrupt file is kept as config.bad.json
/// and the app falls back to defaults.
/// </summary>
public sealed class ConfigService : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KawaiiIsland");

    private readonly string _path;
    private readonly object _gate = new();
    private Timer? _debounce;

    public AppConfig Current { get; }
    public string Directory { get; }

    public ConfigService(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
        System.IO.Directory.CreateDirectory(Directory);
        _path = Path.Combine(Directory, "config.json");
        Current = Load(_path);
    }

    internal static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json)
                         ?? throw new JsonException("config.json is empty");
            config.Validate();
            return config;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            File.Copy(path, Path.Combine(Path.GetDirectoryName(path)!, "config.bad.json"), overwrite: true);
            return new AppConfig();
        }
    }

    /// <summary>Schedules a save 500 ms from now; repeated calls collapse into one write.</summary>
    public void SaveSoon()
    {
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => SaveNow(), null, 500, Timeout.Infinite);
        }
    }

    public void SaveNow()
    {
        lock (_gate)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_debounce is null) return;
            _debounce.Dispose();
            _debounce = null;
        }
        SaveNow(); // flush a pending debounced save
    }
}

// ---- Model (mirrors spec §6; property names serialize camelCase) ----

public sealed class AppConfig
{
    public const string DefaultMascot = "kiko";
    public static readonly string[] Mascots = ["kiko", "miso", "bun", "bolt", "ribbit"];

    public int Version { get; set; } = 1;
    public WindowConfig Window { get; set; } = new();
    public AppearanceConfig Appearance { get; set; } = new();
    public ModulesConfig Modules { get; set; } = new();
    public BehaviorConfig Behavior { get; set; } = new();

    /// <summary>Clamps out-of-range values and replaces unknown enum-like strings with defaults.</summary>
    public void Validate()
    {
        Window ??= new(); Appearance ??= new(); Modules ??= new(); Behavior ??= new();
        Modules.Mail ??= new(); Modules.Notifications ??= new(); Modules.Music ??= new(); Modules.Shortcuts ??= new();
        Modules.Code ??= new();
        Modules.Code.Port = Math.Clamp(Modules.Code.Port, 1024, 65535);
        Modules.Notifications.Muted ??= [];
        Modules.Shortcuts.Items ??= [];

        var w = Window;
        w.CollapsedWidth = Math.Clamp(w.CollapsedWidth, 120, 400);
        w.CollapsedHeight = Math.Clamp(w.CollapsedHeight, 24, 64);
        w.ExpandedWidth = Math.Clamp(w.ExpandedWidth, 300, 900);
        w.ExpandedHeight = Math.Clamp(w.ExpandedHeight, 120, 400);
        w.CornerRadius = Math.Clamp(w.CornerRadius, 0, 60);
        w.Opacity = Math.Clamp(w.Opacity, 0.2, 1.0);
        w.DockEdge = OneOf(w.DockEdge, "Top", "Top", "Bottom", "Left", "Right");
        w.Alignment = OneOf(w.Alignment, "Center", "Left", "Center", "Right", "Top", "Middle", "Bottom");

        Appearance.Theme = OneOf(Appearance.Theme, "dark", "dark", "light", "auto");
        Appearance.Mascot = OneOf(Appearance.Mascot, DefaultMascot, Mascots);
        Behavior.AutoCollapseSeconds = Math.Clamp(Behavior.AutoCollapseSeconds, 0, 600); // 0 = never
        Modules.Mail.PollSeconds = Math.Clamp(Modules.Mail.PollSeconds, 15, 3600);
        Modules.Shortcuts.Max = Math.Clamp(Modules.Shortcuts.Max, 1, 24);
    }

    private static string OneOf(string? value, string fallback, params string[] allowed) =>
        allowed.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
}

public sealed class WindowConfig
{
    public string MonitorId { get; set; } = "";
    public string DockEdge { get; set; } = "Top";
    public string Alignment { get; set; } = "Center";
    public double X { get; set; }
    public double Y { get; set; }
    public double CollapsedWidth { get; set; } = 180;
    public double CollapsedHeight { get; set; } = 36;
    public double ExpandedWidth { get; set; } = 520;
    public double ExpandedHeight { get; set; } = 180;
    public double CornerRadius { get; set; } = 28;
    public double Opacity { get; set; } = 1.0;
    public bool Locked { get; set; } = true;
    public bool AppBarEnabled { get; set; } = true;
    public bool AutoHideFullscreen { get; set; } = true;
    public bool AutoHideAlways { get; set; }
}

public sealed class AppearanceConfig
{
    public string Theme { get; set; } = "dark";
    public string Accent { get; set; } = "#FF8FB1";
    public string Mascot { get; set; } = AppConfig.DefaultMascot;
}

public sealed class ModulesConfig
{
    public MailConfig Mail { get; set; } = new();
    public NotificationsConfig Notifications { get; set; } = new();
    public MusicConfig Music { get; set; } = new();
    public ShortcutsConfig Shortcuts { get; set; } = new();
    public CodeConfig Code { get; set; } = new();
}

/// <summary>Code mode: Claude Code session companion. Listener is 127.0.0.1-only; data is kept in memory only.</summary>
public sealed class CodeConfig
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 47811;
}

/// <summary>No password here — credentials are DPAPI-encrypted in a separate local file (Phase 9).</summary>
public sealed class MailConfig
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "mock";
    public string Server { get; set; } = "";
    public int Port { get; set; } = 993;
    public bool Ssl { get; set; } = true;
    public string Username { get; set; } = "";
    public int PollSeconds { get; set; } = 60;
}

public sealed class NotificationsConfig
{
    public bool Enabled { get; set; } = true;
    public List<string> Muted { get; set; } = [];
    public bool Dnd { get; set; }
}

public sealed class MusicConfig
{
    public bool Enabled { get; set; } = true;
}

public sealed class ShortcutsConfig
{
    public bool Enabled { get; set; } = true;
    public int Max { get; set; } = 8;
    public List<ShortcutItem> Items { get; set; } = [new() { Label = "Notepad", Path = @"C:\Windows\notepad.exe" }];
}

public sealed class ShortcutItem
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
}

public sealed class BehaviorConfig
{
    public int AutoCollapseSeconds { get; set; } = 4;
    public bool StartWithWindows { get; set; } = true;
}
