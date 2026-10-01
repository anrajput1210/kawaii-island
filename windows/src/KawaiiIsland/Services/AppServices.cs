using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace KawaiiIsland.Services;

/// <summary>
/// Microsoft.Extensions.Logging → one plain-text file per day in &lt;settings folder&gt;\logs, last 7 days kept.
/// Local only (the spec's %APPDATA% is replaced by the app's LOCALAPPDATA folder so nothing roams).
/// </summary>
public sealed class FileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _gate = new();

    public static ILogger Create(string directory, LogLevel minimum = LogLevel.Information)
    {
        Directory.CreateDirectory(directory);
        foreach (var old in new DirectoryInfo(directory).GetFiles("*.log").OrderByDescending(f => f.Name).Skip(7))
            try { old.Delete(); } catch (IOException) { }
        return LoggerFactory.Create(b => b.SetMinimumLevel(minimum).AddProvider(new FileLoggerProvider(directory))).CreateLogger("KawaiiIsland");
    }

    public ILogger CreateLogger(string category) => new FileLogger(this);

    public void Dispose() { }

    private void Write(string line)
    {
        lock (_gate)
            try { File.AppendAllText(Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine); }
            catch (IOException) { } // logging must never take the app down
            catch (UnauthorizedAccessException) { }
    }

    private sealed class FileLogger(FileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> format) =>
            owner.Write($"{DateTime.Now:HH:mm:ss.fff} {level.ToString().ToUpperInvariant()[..4]} {format(state, ex)}{(ex is null ? "" : Environment.NewLine + ex)}");
    }
}

/// <summary>Start with Windows (spec §6): HKCU\…\Run\KawaiiIsland = "&lt;exe&gt;".</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "KawaiiIsland";

    /// <summary>Development builds (bin\Debug, bin\Release) are never registered automatically, only on an explicit toggle.</summary>
    public static bool IsDevBuild(string exe) => exe.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) || exe.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);

    public static void Apply(bool on, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, $"\"{exe}\"");
        else if (key.GetValue(Name) is not null) key.DeleteValue(Name);
    }
}

/// <summary>"Ctrl+Alt+I"-style text ⇄ RegisterHotKey modifiers + virtual key. Unit-tested.</summary>
public static class HotkeyText
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    /// <returns>false for empty text, unknown keys, or no modifier (a bare letter would steal typing).</returns>
    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Ctrl; break;
                case "alt": modifiers |= Alt; break;
                case "shift": modifiers |= Shift; break;
                case "win" or "windows": modifiers |= Win; break;
                default:
                    if (virtualKey != 0) return false;
                    Key key;
                    try { key = (Key)new KeyConverter().ConvertFromInvariantString(part)!; }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or FormatException) { return false; }
                    virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return modifiers != 0 && virtualKey != 0;
    }
}
