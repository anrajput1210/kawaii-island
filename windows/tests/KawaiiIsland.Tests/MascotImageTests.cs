using KawaiiIsland.Services;

namespace KawaiiIsland.Tests;

public sealed class MascotImageTests
{
    /// <summary>10x10 white picture with a red 4x4 square whose centre 2x2 is white (enclosed, not background).</summary>
    private static byte[] Picture()
    {
        var px = new byte[10 * 10 * 4];
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
            {
                bool red = x is >= 3 and <= 6 && y is >= 3 and <= 6 && !(x is 4 or 5 && y is 4 or 5);
                int i = (y * 10 + x) * 4;
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = red ? ((byte)30, (byte)30, (byte)230, (byte)255) : ((byte)250, (byte)250, (byte)250, (byte)255);
            }
        return px;
    }

    private static byte Alpha(byte[] px, int w, int x, int y) => px[(y * w + x) * 4 + 3];

    [Fact]
    public void Removes_border_connected_background_but_keeps_enclosed_areas()
    {
        var px = Picture();
        MascotImage.RemoveBackground(px, 10, 10);
        Assert.Equal(0, Alpha(px, 10, 0, 0));      // corner: background
        Assert.Equal(0, Alpha(px, 10, 2, 5));      // next to the subject: background
        Assert.Equal(255, Alpha(px, 10, 4, 4));    // white but enclosed by the subject: kept
        Assert.True(Alpha(px, 10, 3, 5) > 0);      // subject rim kept (maybe feathered)
    }

    [Fact]
    public void Trims_to_subject_and_pads_to_square()
    {
        var px = Picture();
        MascotImage.RemoveBackground(px, 10, 10);
        var (square, side) = MascotImage.TrimToSquare(px, 10, 10);
        Assert.Equal(4, side);
        Assert.Equal(16 * 4, square.Length);
    }

    [Fact]
    public void Pictures_that_are_already_cut_out_are_left_alone()
    {
        var px = new byte[4 * 4 * 4]; // fully transparent
        px[(1 * 4 + 1) * 4 + 3] = 255;
        var before = (byte[])px.Clone();
        MascotImage.RemoveBackground(px, 4, 4);
        Assert.Equal(before, px);
    }
}
