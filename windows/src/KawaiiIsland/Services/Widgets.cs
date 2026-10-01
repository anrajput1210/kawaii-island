using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Windows.Devices.Enumeration;
using Windows.Devices.Geolocation;

namespace KawaiiIsland.Services;

/// <summary>An app from the Start menu (desktop or Store). Launch path: shell:AppsFolder\&lt;AppId&gt;.</summary>
public sealed record InstalledApp(string Name, string AppId)
{
    public string Path => @"shell:AppsFolder\" + AppId;
}

/// <summary>Installed apps exactly as the Start menu lists them (the shell's AppsFolder), minus uninstallers and docs.</summary>
public static class AppCatalog
{
    private static readonly string[] NotApps = [".url", ".txt", ".chm", ".pdf", ".htm", ".html", ".rtf", ".md", ".ini"];

    /// <summary>Run off the UI thread: ~200 items, a fraction of a second.</summary>
    public static List<InstalledApp> All()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return [];
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}"); // AppsFolder
        var apps = new List<InstalledApp>();
        foreach (dynamic item in folder.Items())
        {
            string name = item.Name, id = item.Path;
            if (IsApp(name, id)) apps.Add(new InstalledApp(name, id));
        }
        return [.. apps.DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Filters uninstallers, help files and web links out of the Start menu list. Unit-tested.</summary>
    public static bool IsApp(string name, string id) =>
        name.Length > 0 && id.Length > 0
        && !name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
        && !name.EndsWith("Uninstall", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("Uninstaller", StringComparison.OrdinalIgnoreCase)
        && !NotApps.Any(ext => id.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Current conditions for the weather widget.</summary>
public sealed record WeatherNow(double Temperature, int Code, string Place, bool Fahrenheit)
{
    public string Text => $"{Math.Round(Temperature):0}°";
    public string Symbol => WeatherCodes.Symbol(Code);
    public string Condition => WeatherCodes.Describe(Code);
}

/// <summary>WMO weather codes (as used by Open-Meteo) → a monochrome symbol and a short description. Unit-tested.</summary>
public static class WeatherCodes
{
    public static string Symbol(int code) => code switch
    {
        0 or 1 => "☀",
        2 => "⛅",
        3 => "☁",
        45 or 48 => "≋",
        >= 51 and <= 67 or >= 80 and <= 82 => "☂",
        >= 71 and <= 77 or 85 or 86 => "❄",
        >= 95 => "⚡",
        _ => "☁",
    };

    public static string Describe(int code) => code switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Cloudy",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 or >= 80 and <= 82 => "Rain",
        >= 71 and <= 77 or 85 or 86 => "Snow",
        >= 95 => "Thunderstorm",
        _ => "—",
    };
}

/// <summary>
/// Weather from Open-Meteo (free, no account, no API key). Location: the city typed in Settings, else Windows
/// location (if the user allows desktop apps to use it). Coordinates are rounded to ~10 km before leaving the PC.
/// </summary>
public static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<WeatherNow?> FetchAsync(string city, bool fahrenheit)
    {
        var place = city.Trim().Length > 0 ? await GeocodeAsync(city.Trim()) : await WindowsLocationAsync();
        if (place is not { } p) return null;
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={p.Lat:0.0}&longitude={p.Lon:0.0}&current=temperature_2m,weather_code&temperature_unit={(fahrenheit ? "fahrenheit" : "celsius")}");
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        return ParseCurrent(doc.RootElement, p.Name, fahrenheit);
    }

    /// <summary>Unit-tested against Open-Meteo's documented response shape.</summary>
    public static WeatherNow? ParseCurrent(JsonElement root, string place, bool fahrenheit) =>
        root.TryGetProperty("current", out var c) && c.TryGetProperty("temperature_2m", out var t) && c.TryGetProperty("weather_code", out var w)
            ? new WeatherNow(t.GetDouble(), w.GetInt32(), place, fahrenheit)
            : null;

    private static async Task<(double Lat, double Lon, string Name)?> GeocodeAsync(string city)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(
            $"https://geocoding-api.open-meteo.com/v1/search?count=1&name={Uri.EscapeDataString(city)}"));
        if (!doc.RootElement.TryGetProperty("results", out var r) || r.GetArrayLength() == 0) return null;
        var first = r[0];
        return (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble(), first.GetProperty("name").GetString() ?? city);
    }

    private static async Task<(double Lat, double Lon, string Name)?> WindowsLocationAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
            var pos = await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10));
            return (pos.Coordinate.Point.Position.Latitude, pos.Coordinate.Point.Position.Longitude, "Here");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or TaskCanceledException) { return null; }
    }
}

public sealed record BluetoothBattery(string Name, int Percent);

/// <summary>
/// Battery levels Windows knows for paired Bluetooth devices (headphones, mice, controllers that report it —
/// the same numbers as Settings → Bluetooth &amp; devices). Devices that don't report a level are skipped.
/// </summary>
public static class BluetoothBatteries
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2"; // DEVPKEY_Bluetooth_Battery
    private const string BluetoothClass = "System.Devices.ClassGuid:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";

    public static async Task<List<BluetoothBattery>> ReadAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(BluetoothClass, [BatteryKey], DeviceInformationKind.Device);
            return [.. devices
                .Where(d => d.Properties.TryGetValue(BatteryKey, out var v) && v is byte)
                .Select(d => new BluetoothBattery(d.Name, (byte)d.Properties[BatteryKey]))
                .DistinctBy(b => b.Name)
                .OrderBy(b => b.Percent)];
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
