using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace KawaiiIsland.Services;

// Live activities (ideas from the MIT-licensed Windhawk mod "Dynamic Island for Windows", re-implemented here):
// system volume, mic/camera in use, Bluetooth connect/disconnect, a countdown timer and battery alerts.

/// <summary>Master volume of the default playback device (Core Audio, no extra package).</summary>
public sealed class SystemVolume
{
    private IAudioEndpointVolume? _endpoint;

    /// <returns>(0–100, muted) or null when there is no audio device.</returns>
    public (int Percent, bool Muted)? Read()
    {
        try
        {
            _endpoint ??= Open();
            if (_endpoint is null) return null;
            if (_endpoint.GetMasterVolumeLevelScalar(out float level) != 0 || _endpoint.GetMute(out bool muted) != 0)
            {
                _endpoint = null; // device changed: reopen next time
                return null;
            }
            return ((int)Math.Round(level * 100), muted);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _endpoint = null;
            return null;
        }
    }

    private static IAudioEndpointVolume? Open()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0) return null;
        var iid = typeof(IAudioEndpointVolume).GUID;
        return device.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out object endpoint) == 0 ? (IAudioEndpointVolume)endpoint : null;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}

/// <summary>
/// Microphone / camera in use, read from the same registry store Windows' own privacy indicator uses
/// (CapabilityAccessManager\ConsentStore). An entry is "in use" when it has a start time and no stop time yet.
/// </summary>
public static class PrivacyMonitor
{
    private const string Store = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

    public static (bool Mic, bool Camera) Read() => (InUse("microphone"), InUse("webcam"));

    private static bool InUse(string capability)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var root = hive.OpenSubKey(Store + capability);
            if (root is not null && AnyInUse(root)) return true;
        }
        return false;
    }

    private static bool AnyInUse(RegistryKey root)
    {
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key is null) continue;
            if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase) ? AnyInUse(key) : EntryInUse(key)) return true;
        }
        return false;
    }

    /// <summary>Unit-tested: started and not stopped. Never-used entries (both zero) don't count.</summary>
    public static bool EntryInUse(object? start, object? stop) => start is long s && s != 0 && stop is long t && t == 0;

    private static bool EntryInUse(RegistryKey key) => EntryInUse(key.GetValue("LastUsedTimeStart"), key.GetValue("LastUsedTimeStop"));
}

/// <summary>Bluetooth devices connecting/disconnecting (classic + LE). Events are raised on a background thread.</summary>
public sealed class BluetoothWatcher : IDisposable
{
    private readonly List<DeviceWatcher> _watchers = [];
    private readonly Dictionary<string, string> _names = [];

    public event Action<string, bool>? Changed; // (device name, connected)

    public void Start()
    {
        foreach (var selector in new[]
                 {
                     BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                     BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                 })
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(selector);
                bool primed = false; // devices already connected at startup are not news
                watcher.EnumerationCompleted += (_, _) => primed = true;
                watcher.Added += (_, info) =>
                {
                    if (info.Name.Length == 0) return;
                    lock (_names) _names[info.Id] = info.Name;
                    if (primed) Changed?.Invoke(info.Name, true);
                };
                watcher.Removed += (_, update) =>
                {
                    string? name;
                    lock (_names) _names.Remove(update.Id, out name);
                    if (name is not null) Changed?.Invoke(name, false);
                };
                watcher.Updated += (_, _) => { };
                watcher.Start();
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    /// <summary>Segoe glyph for a device, guessed from its name.</summary>
    public static string Glyph(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("bud") || n.Contains("pods") || n.Contains("head") || n.Contains("ear") ? ""
             : n.Contains("speaker") || n.Contains("sound") ? ""
             : n.Contains("mouse") ? ""
             : n.Contains("keyboard") ? ""
             : n.Contains("phone") || n.Contains("galaxy") || n.Contains("pixel") ? ""
             : n.Contains("controller") || n.Contains("xbox") || n.Contains("dualsense") ? ""
             : "";
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
            try { w.Stop(); } catch (InvalidOperationException) { }
        _watchers.Clear();
    }
}

/// <summary>Countdown timer (Apple's Timer live activity). Pure; unit-tested.</summary>
public sealed class CountdownTimer
{
    private DateTimeOffset _endsAt;
    private TimeSpan _remainingAtPause;

    public TimeSpan Total { get; private set; }
    public bool Active { get; private set; }
    public bool Running { get; private set; }

    public void Start(TimeSpan length, DateTimeOffset now)
    {
        Total = length;
        _endsAt = now + length;
        Active = Running = true;
    }

    public void TogglePause(DateTimeOffset now)
    {
        if (!Active) return;
        if (Running) { _remainingAtPause = Remaining(now); Running = false; }
        else { _endsAt = now + _remainingAtPause; Running = true; }
    }

    public void Cancel() => Active = Running = false;

    public TimeSpan Remaining(DateTimeOffset now) =>
        !Active ? TimeSpan.Zero : Running ? (_endsAt - now < TimeSpan.Zero ? TimeSpan.Zero : _endsAt - now) : _remainingAtPause;

    /// <returns>true exactly once, when a running timer reaches zero (it then ends).</returns>
    public bool CheckFinished(DateTimeOffset now)
    {
        if (!Active || !Running || now < _endsAt) return false;
        Cancel();
        return true;
    }

    /// <summary>"4:59", "59:00", "1:02:03".</summary>
    public static string Format(TimeSpan t)
    {
        int s = (int)Math.Ceiling(t.TotalSeconds);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}

/// <summary>When to show a battery alert. Unit-tested.</summary>
public static class BatteryAlerts
{
    public enum Kind { None, Charging, Unplugged, Low }

    public static Kind Check((int Percent, bool Charging)? before, (int Percent, bool Charging)? now)
    {
        if (before is not { } b || now is not { } n) return Kind.None;
        if (!b.Charging && n.Charging) return Kind.Charging;
        if (b.Charging && !n.Charging) return Kind.Unplugged;
        if (!n.Charging && n.Percent < b.Percent && (n.Percent <= 20 && b.Percent > 20 || n.Percent <= 10 && b.Percent > 10)) return Kind.Low;
        return Kind.None;
    }
}
