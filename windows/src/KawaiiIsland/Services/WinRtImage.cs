using System.IO;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;

namespace KawaiiIsland.Services;

/// <summary>WinRT image streams (album art, app logos) → frozen WPF bitmaps.</summary>
internal static class WinRtImage
{
    public static async Task<MemoryStream> ReadAsync(IRandomAccessStreamReference reference)
    {
        using var winrt = await reference.OpenReadAsync();
        using var source = winrt.AsStreamForRead();
        var bytes = new MemoryStream();
        await source.CopyToAsync(bytes);
        return bytes;
    }

    public static BitmapImage Decode(MemoryStream bytes, int width)
    {
        bytes.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.DecodePixelWidth = width;
        img.StreamSource = bytes;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
