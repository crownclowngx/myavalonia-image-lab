using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.PoissonBlending;

namespace ImageLabPlugin.Tests;

internal static class PoissonTestFactory
{
    public static PixelImage Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var bytes = new byte[checked(width * height * 4)];
        for (var i = 0; i < width * height; i++) { bytes[i * 4] = r; bytes[(i * 4) + 1] = g; bytes[(i * 4) + 2] = b; bytes[(i * 4) + 3] = a; }
        return new PixelImage(new ImageSize(width, height), bytes);
    }

    public static PixelImage Gradient(int width, int height)
    {
        var image = Solid(width, height, 0, 0, 0);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            image.SetRgb(x, y, (byte)(x * 255 / Math.Max(width - 1, 1)), (byte)(y * 255 / Math.Max(height - 1, 1)), (byte)((x + y) * 255 / Math.Max(width + height - 2, 1)));
        return image;
    }

    public static PoissonBinaryMask RectangleMask(int width, int height, PoissonRectangle rectangle) =>
        new PoissonMaskRasterizer().Rasterize(new ImageSize(width, height), new PoissonMaskDefinition(rectangle, []));

    public static PoissonGuidanceCatalog Catalog() => new(new IPoissonGuidanceStrategy[]
    { new NormalCloneGuidanceStrategy(), new MixedGradientGuidanceStrategy(), new MonochromeGuidanceStrategy() });

    public static PoissonProblemBuilder Builder(SrgbColorSpace? color = null)
    {
        color ??= new SrgbColorSpace();
        return new(color, Catalog(), new PoissonPlacementValidator(), new PoissonMaskTopologyAnalyzer(), new PoissonResourceEstimator());
    }
}
