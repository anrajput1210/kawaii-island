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
    /// <summary>"none" hides the mascot.</summary>
    public const string NoMascot = "none";

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
        Modules.Widgets ??= new();
        Modules.Widgets.City ??= "";
        Modules.Widgets.Tasks ??= [];
        Appearance.Shape = OneOf(Appearance.Shape, "pill", "pill", "notch");
        Modules.Code.Port = Math.Clamp(Modules.Code.Port, 1024, 65535);
        Modules.Notifications.Muted ??= [];
        Modules.Notifications.Source = OneOf(Modules.Notifications.Source, "windows", "windows", "mock");
        Modules.Shortcuts.Items ??= [];

        var w = Window;
        w.CollapsedWidth = Math.Clamp(w.CollapsedWidth, 120, 400);
        w.CollapsedHeight = Math.Clamp(w.CollapsedHeight, 24, 64);
        w.ExpandedWidth = Math.Clamp(w.ExpandedWidth, 300, 900);
        w.CornerRadius = Math.Clamp(w.CornerRadius, 0, 60);
        w.Opacity = Math.Clamp(w.Opacity, 0.2, 1.0);
        w.DockEdge = OneOf(w.DockEdge, "Top", "Top", "Bottom", "Left", "Right");
        w.Alignment = OneOf(w.Alignment, "Center", "Left", "Center", "Right", "Top", "Middle", "Bottom");

        Appearance.Theme = OneOf(Appearance.Theme, "dark", "dark", "light", "auto");
        Appearance.Mascot = OneOf(Appearance.Mascot, DefaultMascot, [.. Mascots, NoMascot]);
        // The open island always closes on its own unless music is playing (0 used to mean "never").
        Behavior.AutoCollapseSeconds = Behavior.AutoCollapseSeconds == 0 ? 4 : Math.Clamp(Behavior.AutoCollapseSeconds, 2, 600);
        Modules.Mail.PollSeconds = Math.Clamp(Modules.Mail.PollSeconds, 15, 3600);
        Modules.Mail.Provider = OneOf(Modules.Mail.Provider, "mock", "mock", "imap", "google", "microsoft");
        Modules.Mail.GoogleClientId ??= ""; Modules.Mail.GoogleClientSecret ??= ""; Modules.Mail.MicrosoftClientId ??= "";
        Modules.Mail.Port = Math.Clamp(Modules.Mail.Port, 1, 65535);
        Modules.Mail.Server ??= ""; Modules.Mail.Username ??= ""; Modules.Mail.OpenUrl ??= "";
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
    public string Accent { get; set; } = "#FF375F"; // Apple system pink
    public string Mascot { get; set; } = AppConfig.DefaultMascot;
    /// <summary>"pill" (floating, Apple) or "notch" (flush with the top edge, square top corners).</summary>
    public string Shape { get; set; } = "pill";
}

public sealed class TaskItem
{
    public string Name { get; set; } = "";
    public string Time { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class ModulesConfig
{
    public MailConfig Mail { get; set; } = new();
    public NotificationsConfig Notifications { get; set; } = new();
    public MusicConfig Music { get; set; } = new();
    public ShortcutsConfig Shortcuts { get; set; } = new();
    public CodeConfig Code { get; set; } = new();
    public WidgetsConfig Widgets { get; set; } = new();
}

/// <summary>What the island shows (Settings → Widgets). "Pill" = the small resting island, "Home" = expanded.</summary>
public sealed class WidgetsConfig
{
    public bool PillClock { get; set; } = true;
    public bool PillWeather { get; set; }
    public bool PillBattery { get; set; }
    public bool HomeClock { get; set; } = true;
    public bool HomeWeather { get; set; } = true;
    public bool HomeBattery { get; set; } = true;
    public bool HomeBluetooth { get; set; } = true;
    /// <summary>City for the weather; empty = Windows location (when allowed).</summary>
    public string City { get; set; } = "";
    public bool Fahrenheit { get; set; } = !System.Globalization.RegionInfo.CurrentRegion.IsMetric;
    // Live activities (brief alerts on the island)
    public bool LiveVolume { get; set; } = true;
    public bool LiveBattery { get; set; } = true;
    public bool LiveBluetooth { get; set; } = true;
    public bool LiveKeys { get; set; } = true;
    public bool LivePrivacy { get; set; } = true;
    /// <summary>Calendar tab: events from the signed-in Google/Outlook account + your own tasks.</summary>
    public bool Calendar { get; set; } = true;
    /// <summary>System tab: CPU, memory, disk, network, and Lock/Sleep/Restart/Shut down.</summary>
    public bool ShowSystem { get; set; } = true;
    public List<TaskItem> Tasks { get; set; } = [];
}

/// <summary>Code mode: Claude Code session companion. Listener is 127.0.0.1-only; data is kept in memory only.</summary>
public sealed class CodeConfig
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 47811;
    /// <summary>Our hooks are installed in ~/.claude/settings.json (Settings → AI agents → Claude Code).</summary>
    public bool ClaudeHooks { get; set; }
    /// <summary>The user has OK'd editing ~/.claude/settings.json once; later toggles skip the explanation.</summary>
    public bool Consented { get; set; }
    /// <summary>Answer Claude Code permission prompts (Allow / Deny) from the island.</summary>
    public bool Approvals { get; set; } = true;
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
    /// <summary>Developer overrides for the built-in OAuth clients (config.json only, no UI; see README → For maintainers).</summary>
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string MicrosoftClientId { get; set; } = "";
    /// <summary>Where clicking mail goes; empty = webmail guessed from the server, else the default mail app.</summary>
    public string OpenUrl { get; set; } = "";
}

public sealed class NotificationsConfig
{
    public bool Enabled { get; set; } = true;
    public List<string> Muted { get; set; } = [];
    public bool Dnd { get; set; }
    /// <summary>Show message text on the island. Off by default: screens are visible to bystanders (sender only).</summary>
    public bool ShowPreview { get; set; }
    /// <summary>"windows" = real toasts (UserNotificationListener); "mock" = fake ones for demos.</summary>
    public string Source { get; set; } = "windows";
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
    /// <summary>Global shortcut that opens/closes the island; "" = none.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+I";
}
