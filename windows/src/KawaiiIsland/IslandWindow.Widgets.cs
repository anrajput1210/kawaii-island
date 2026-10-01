using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Native;
using Microsoft.Extensions.Logging;

namespace KawaiiIsland;

/// <summary>
/// Widgets the user picks in Settings → Widgets: weather, laptop battery and Bluetooth batteries, in the resting
/// pill and/or the expanded Home view. Battery/Bluetooth refresh every minute, weather every 20 minutes.
/// </summary>
public partial class IslandWindow
{
    private static readonly FontFamily Glyphs = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly DispatcherTimer _deviceTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(20) };
    private WeatherNow? _weather;
    private (int Percent, bool Charging)? _battery;
    private List<BluetoothBattery> _bluetooth = [];

    private WidgetsConfig Widgets => _config.Current.Modules.Widgets;

    private void InitWidgets()
    {
        _deviceTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        _deviceTimer.Start();
        _weatherTimer.Start();
        _ = RefreshDevicesAsync();
        _ = RefreshWeatherAsync();
    }

    /// <summary>Settings changed (city, units, which widgets): refetch and redraw.</summary>
    public async Task RefreshWidgetsAsync()
    {
        await RefreshWeatherAsync();
        await RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        _battery = Win32.Battery();
        if (Widgets.HomeBluetooth) _bluetooth = await BluetoothBatteries.ReadAsync();
        RenderWidgets();
    }

    private async Task RefreshWeatherAsync()
    {
        if (!Widgets.PillWeather && !Widgets.HomeWeather) { _weather = null; RenderWidgets(); return; }
        try { _weather = await WeatherService.FetchAsync(Widgets.City, Widgets.Fahrenheit) ?? _weather; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            App.Log.LogInformation("Weather unavailable: {Message}", ex.Message); // offline: keep the last reading
        }
        RenderWidgets();
    }

    /// <summary>Extra width the resting pill needs for its chosen widgets.</summary>
    private double PillWidgetWidth => (Widgets.PillWeather ? 56 : 0) + (Widgets.PillBattery ? 60 : 0) - (Widgets.PillClock ? 0 : 60);

    private void RenderWidgets()
    {
        // resting pill chips
        PillWeather.Text = _weather is { } w ? $"{w.Symbol} {w.Text}" : "";
        if (_battery is { } b)
        {
            PillBatteryGlyph.Text = BatteryGlyph(b.Percent, b.Charging);
            PillBatteryText.Text = $"{b.Percent}%";
            PillBatteryGlyph.Foreground = b.Percent <= 20 && !b.Charging ? Brushes.OrangeRed : (Brush)FindResource("IslandText");
        }

        // expanded Home tiles
        HomeWidgets.Children.Clear();
        if (Widgets.HomeWeather && _weather is { } weather)
            AddTile(weather.Symbol + " " + weather.Text, weather.Condition + (weather.Place == "Here" ? "" : " · " + weather.Place), null);
        if (Widgets.HomeBattery && _battery is { } battery)
            AddTile($"{battery.Percent}%", battery.Charging ? "Charging" : "Battery", BatteryGlyph(battery.Percent, battery.Charging));
        if (Widgets.HomeBluetooth)
            foreach (var device in _bluetooth.Take(2))
                AddTile($"{device.Percent}%", device.Name, "");

        RenderCompact();
        RenderExpanded();
    }

    /// <summary>Big value + small caption, separated from the previous tile by a thin divider.</summary>
    private void AddTile(string value, string caption, string? glyph)
    {
        if (HomeWidgets.Children.Count > 0 || Widgets.HomeClock)
        {
            var line = new Rectangle { Width = 1, Height = 34, Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            line.SetResourceReference(Shape.FillProperty, "IslandTrack");
            HomeWidgets.Children.Add(line);
        }
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
        {
            var g = Text(glyph, 15, "IslandText");
            g.FontFamily = Glyphs;
            g.Margin = new Thickness(0, 0, 6, 0);
            g.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(g);
        }
        var big = Text(value, 19, "IslandText");
        big.FontWeight = FontWeights.SemiBold;
        head.Children.Add(big);
        var small = Text(caption, 11, "IslandMuted");
        small.MaxWidth = 110;
        small.TextTrimming = TextTrimming.CharacterEllipsis;
        HomeWidgets.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { head, small } });
    }

    /// <summary>Segoe battery glyphs: Battery0–9 (E850–E859), Battery10 (E83F); charging shows the plug variant.</summary>
    internal static string BatteryGlyph(int percent, bool charging)
    {
        if (charging) return "";
        int level = Math.Clamp(percent / 10, 0, 10);
        return level == 10 ? "" : ((char)(0xE850 + level)).ToString();
    }
}
