using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KawaiiIsland.Services;

/// <summary>
/// Turns any picture into a custom mascot: downscale to ≤256 px, remove the background, trim to the subject and pad
/// to a transparent square PNG.
/// ponytail: background removal is an edge flood fill on the border colour, which is right for stickers, logos and
/// characters on plain/near-plain backgrounds; busy photo backgrounds need a segmentation model (not bundled).
/// </summary>
public static class MascotImage
{
    private const int MaxSide = 256;

    public static void Import(string source, string destination)
    {
        var frame = BitmapFrame.Create(new Uri(source), BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
        double scale = Math.Min(1, (double)MaxSide / Math.Max(frame.PixelWidth, frame.PixelHeight));
        BitmapSource bmp = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
        bmp = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);

        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        RemoveBackground(px, w, h);
        var (cropped, side) = TrimToSquare(px, w, h);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, cropped, side * 4)));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var file = File.Create(destination);
        encoder.Save(file);
    }

    /// <summary>
    /// Clears every pixel connected to the image border that is close to the dominant border colour, and feathers
    /// the 1 px rim. Pictures that already have a transparent border are left alone. BGRA, in place.
    /// </summary>
    public static void RemoveBackground(byte[] px, int w, int h, int tolerance = 48)
    {
        var border = BorderPixels(w, h).ToList();
        if (border.Count(i => px[i * 4 + 3] < 32) > border.Count / 4) return; // already cut out

        // Dominant border colour (quantised to 16 levels per channel so JPEG noise still agrees).
        int bg = border.GroupBy(i => Quant(px, i)).OrderByDescending(g => g.Count()).First().First();
        byte b = px[bg * 4], g = px[bg * 4 + 1], r = px[bg * 4 + 2];
        int Dist(int i) => Math.Max(Math.Abs(px[i * 4] - b), Math.Max(Math.Abs(px[i * 4 + 1] - g), Math.Abs(px[i * 4 + 2] - r)));

        var removed = new bool[w * h];
        var queue = new Queue<int>();
        foreach (int i in border)
            if (!removed[i] && Dist(i) <= tolerance) { removed[i] = true; queue.Enqueue(i); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            foreach (int n in (int[])[x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1])
                if (n >= 0 && !removed[n] && Dist(n) <= tolerance) { removed[n] = true; queue.Enqueue(n); }
        }

        for (int i = 0; i < removed.Length; i++)
        {
            if (removed[i]) { px[i * 4 + 3] = 0; continue; }
            int x = i % w, y = i / w; // rim pixel next to the removed area: fade by how background-like it is
            bool rim = (x > 0 && removed[i - 1]) || (x < w - 1 && removed[i + 1]) || (y > 0 && removed[i - w]) || (y < h - 1 && removed[i + w]);
            if (rim) px[i * 4 + 3] = (byte)(px[i * 4 + 3] * Math.Clamp((Dist(i) - tolerance) / (double)tolerance, 0.35, 1));
        }
    }

    /// <summary>Crops to the visible pixels (alpha ≥ 16) and centres them in a transparent square.</summary>
    public static (byte[] Pixels, int Side) TrimToSquare(byte[] px, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] >= 16) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        if (maxX < 0) { minX = minY = 0; maxX = w - 1; maxY = h - 1; } // nothing left: keep the whole picture

        int cw = maxX - minX + 1, ch = maxY - minY + 1, side = Math.Max(cw, ch);
        int ox = (side - cw) / 2, oy = (side - ch) / 2;
        var output = new byte[side * side * 4];
        for (int y = 0; y < ch; y++)
            Array.Copy(px, ((minY + y) * w + minX) * 4, output, ((oy + y) * side + ox) * 4, cw * 4);
        return (output, side);
    }

    private static int Quant(byte[] px, int i) => (px[i * 4] >> 4) | (px[i * 4 + 1] >> 4 << 4) | (px[i * 4 + 2] >> 4 << 8);

    private static IEnumerable<int> BorderPixels(int w, int h)
    {
        for (int x = 0; x < w; x++) { yield return x; if (h > 1) yield return (h - 1) * w + x; }
        for (int y = 1; y < h - 1; y++) { yield return y * w; if (w > 1) yield return y * w + w - 1; }
    }
}
