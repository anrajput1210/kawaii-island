using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KawaiiIsland.Services.Native;

namespace KawaiiIsland.Services;

public enum PinResult { Added, AlreadyPinned, Full, Missing }

/// <summary>App shortcuts (spec §3.4): list edits (unit-tested), launching, and icons cached as PNG.</summary>
public static class ShortcutLauncher
{
    /// <summary>Pins a file/app/folder: label = file name without extension; no duplicates; respects <paramref name="max"/>.</summary>
    public static PinResult Pin(List<ShortcutItem> items, string path, int max)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return PinResult.Missing;
        if (items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return PinResult.AlreadyPinned;
        if (items.Count >= max) return PinResult.Full;
        string name = Path.GetFileNameWithoutExtension(path.TrimEnd('\\'));
        items.Add(new ShortcutItem { Label = name.Length > 0 ? name : path, Path = path });
        return PinResult.Added;
    }

    /// <summary>Drag-reorder: moves the item at <paramref name="from"/> into position <paramref name="to"/>.</summary>
    public static void Move(List<ShortcutItem> items, int from, int to)
    {
        if (from == to || from < 0 || from >= items.Count || to < 0 || to >= items.Count) return;
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
    }

    /// <returns>null on success, or why it didn't start (missing file, UAC cancelled…).</returns>
    public static string? Launch(ShortcutItem item, bool asAdmin = false)
    {
        try
        {
            var info = new ProcessStartInfo(item.Path) { UseShellExecute = true, Arguments = item.Args ?? "" };
            if (asAdmin) info.Verb = "runas";
            var dir = Path.GetDirectoryName(item.Path);
            if (File.Exists(item.Path) && dir is not null) info.WorkingDirectory = dir;
            Process.Start(info);
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; } // UAC prompt cancelled
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return $"Couldn't open {item.Label}: {ex.Message}";
        }
    }

    private static readonly Dictionary<string, ImageSource?> Memory = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon from the PNG cache (icons/&lt;hash&gt;.png in the settings folder), extracted on first use.</summary>
    public static ImageSource? Icon(string path, string directory)
    {
        if (Memory.TryGetValue(path, out var known)) return known;
        string file = Path.Combine(directory, "icons", Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..16] + ".png");
        ImageSource? icon = null;
        try
        {
            if (File.Exists(file))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(file);
                bmp.EndInit();
                bmp.Freeze();
                icon = bmp;
            }
            else if (Win32.FileIcon(path) is { } extracted)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(extracted));
                using (var stream = File.Create(file)) encoder.Save(stream);
                icon = extracted;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
        return Memory[path] = icon;
    }
}
