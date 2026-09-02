using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.Shared.Perturbations;

internal sealed class ScaleOperator : SynchronousPerturbationOperator<ScaleParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Scale;
    protected override PixelImage Apply(PixelImage source, ScaleParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var width = Math.Max(1, checked((int)Math.Round(source.Size.Width * (double)p.ScaleX, MidpointRounding.AwayFromZero)));
        var height = Math.Max(1, checked((int)Math.Round(source.Size.Height * (double)p.ScaleY, MidpointRounding.AwayFromZero)));
        var size = new ImageSize(width, height); if (size == source.Size) return source.Clone();
        var output = new byte[checked(width * height * 4)]; var input = source.Rgba.Span;
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var sy = ((y + 0.5d) * source.Size.Height / height) - 0.5d;
            for (var x = 0; x < width; x++)
            {
                var sx = ((x + 0.5d) * source.Size.Width / width) - 0.5d;
                PerturbationPixels.Bilinear(input, source.Size, sx, sy, output, PerturbationPixels.Offset(size, x, y));
            }
        }
        return new PixelImage(size, output);
    }
}

internal sealed class CropOperator : SynchronousPerturbationOperator<CropParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Crop;
    protected override PixelImage Apply(PixelImage source, CropParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var width = source.Size.Width - p.Left - p.Right; var height = source.Size.Height - p.Top - p.Bottom;
        if (width <= 0 || height <= 0) throw new ArgumentException("裁剪后尺寸必须至少为 1×1。");
        if (width == source.Size.Width && height == source.Size.Height) return source.Clone();
        var size = new ImageSize(width, height); var output = new byte[checked(width * height * 4)]; var input = source.Rgba.Span;
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            input.Slice(PerturbationPixels.Offset(source.Size, p.Left, y + p.Top), width * 4).CopyTo(output.AsSpan(y * width * 4));
        }
        return new PixelImage(size, output);
    }
}

internal sealed class PadOperator : SynchronousPerturbationOperator<PadParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Pad;
    protected override PixelImage Apply(PixelImage source, PadParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        var size = new ImageSize(checked(source.Size.Width + p.Left + p.Right), checked(source.Size.Height + p.Top + p.Bottom));
        if (size == source.Size) return source.Clone();
        var output = new byte[checked((int)size.PixelCount * 4)];
        for (var y = 0; y < size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < size.Width; x++) PerturbationPixels.Write(output, PerturbationPixels.Offset(size, x, y), p.Fill);
        }
        var input = source.Rgba.Span;
        for (var y = 0; y < source.Size.Height; y++) input.Slice(y * source.Size.Width * 4, source.Size.Width * 4).CopyTo(output.AsSpan(PerturbationPixels.Offset(size, p.Left, y + p.Top)));
        return new PixelImage(size, output);
    }
}

internal sealed class TranslateOperator : SynchronousPerturbationOperator<TranslateParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Translate;
    protected override PixelImage Apply(PixelImage source, TranslateParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        if (p.Dx == 0 && p.Dy == 0) return source.Clone();
        var output = new byte[checked((int)source.Size.PixelCount * 4)]; var input = source.Rgba.Span;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var o = PerturbationPixels.Offset(source.Size, x, y); var sx = x - p.Dx; var sy = y - p.Dy;
                if ((uint)sx < (uint)source.Size.Width && (uint)sy < (uint)source.Size.Height) input.Slice(PerturbationPixels.Offset(source.Size, sx, sy), 4).CopyTo(output.AsSpan(o));
                else PerturbationPixels.Write(output, o, p.Fill);
            }
        }
        return new PixelImage(source.Size, output);
    }
}

internal sealed class RotateOperator : SynchronousPerturbationOperator<RotateParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Rotate;
    protected override PixelImage Apply(PixelImage source, RotateParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        if (p.Degrees == 0m) return source.Clone();
        var radians = -(double)p.Degrees * Math.PI / 180d; var cosine = Math.Cos(radians); var sine = Math.Sin(radians);
        var cx = (source.Size.Width - 1d) / 2d; var cy = (source.Size.Height - 1d) / 2d;
        return GeometrySampler.Map(source, (x, y) =>
        {
            var dx = x - cx; var dy = y - cy;
            return ((cosine * dx) - (sine * dy) + cx, (sine * dx) + (cosine * dy) + cy);
        }, p.Fill, token);
    }
}

internal sealed class PerspectiveOperator : SynchronousPerturbationOperator<PerspectiveParameters>
{
    public override PerturbationKind Kind => PerturbationKind.Perspective;
    protected override PixelImage Apply(PixelImage source, PerspectiveParameters p, PerturbationExecutionContext trial, CancellationToken token)
    {
        if (p is { TopLeftX: 0, TopLeftY: 0, TopRightX: 0, TopRightY: 0, BottomRightX: 0, BottomRightY: 0, BottomLeftX: 0, BottomLeftY: 0 }) return source.Clone();
        var w = source.Size.Width - 1d; var h = source.Size.Height - 1d;
        var sourceCorners = new[] { (0d, 0d), (w, 0d), (w, h), (0d, h) };
        var destinationCorners = new[]
        {
            ((double)p.TopLeftX * w, (double)p.TopLeftY * h),
            (w + (double)p.TopRightX * w, (double)p.TopRightY * h),
            (w + (double)p.BottomRightX * w, h + (double)p.BottomRightY * h),
            ((double)p.BottomLeftX * w, h + (double)p.BottomLeftY * h)
        };
        // 求“目标坐标→源坐标”的单应矩阵，随后逆向采样；不做自动配准或裁边。
        var matrix = Homography.Solve(destinationCorners, sourceCorners);
        return GeometrySampler.Map(source, (x, y) =>
        {
            var denominator = (matrix[6] * x) + (matrix[7] * y) + 1d;
            if (Math.Abs(denominator) < 1e-12) return (double.NaN, double.NaN);
            return (((matrix[0] * x) + (matrix[1] * y) + matrix[2]) / denominator, ((matrix[3] * x) + (matrix[4] * y) + matrix[5]) / denominator);
        }, p.Fill, token);
    }
}

internal static class GeometrySampler
{
    public static PixelImage Map(PixelImage source, Func<double, double, (double X, double Y)> inverse, RgbaColor fill, CancellationToken token)
    {
        var output = new byte[checked((int)source.Size.PixelCount * 4)]; var input = source.Rgba.Span;
        for (var y = 0; y < source.Size.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var o = PerturbationPixels.Offset(source.Size, x, y); var mapped = inverse(x, y);
                if (!double.IsFinite(mapped.X) || !double.IsFinite(mapped.Y) || mapped.X < 0d || mapped.Y < 0d || mapped.X > source.Size.Width - 1d || mapped.Y > source.Size.Height - 1d) PerturbationPixels.Write(output, o, fill);
                else PerturbationPixels.Bilinear(input, source.Size, mapped.X, mapped.Y, output, o);
            }
        }
        return new PixelImage(source.Size, output);
    }
}

internal static class Homography
{
    public static double[] Solve((double X, double Y)[] source, (double X, double Y)[] destination)
    {
        var equations = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = source[i]; var (u, v) = destination[i]; var row = i * 2;
            equations[row, 0] = x; equations[row, 1] = y; equations[row, 2] = 1; equations[row, 6] = -u * x; equations[row, 7] = -u * y; equations[row, 8] = u;
            equations[row + 1, 3] = x; equations[row + 1, 4] = y; equations[row + 1, 5] = 1; equations[row + 1, 6] = -v * x; equations[row + 1, 7] = -v * y; equations[row + 1, 8] = v;
        }
        for (var column = 0; column < 8; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < 8; row++) if (Math.Abs(equations[row, column]) > Math.Abs(equations[pivot, column])) pivot = row;
            if (Math.Abs(equations[pivot, column]) < 1e-10) throw new ArgumentException("透视四边形不可逆或数值不稳定。");
            if (pivot != column) for (var j = column; j < 9; j++) (equations[column, j], equations[pivot, j]) = (equations[pivot, j], equations[column, j]);
            var divisor = equations[column, column]; for (var j = column; j < 9; j++) equations[column, j] /= divisor;
            for (var row = 0; row < 8; row++) if (row != column)
            {
                var factor = equations[row, column]; for (var j = column; j < 9; j++) equations[row, j] -= factor * equations[column, j];
            }
        }
        return Enumerable.Range(0, 8).Select(i => equations[i, 8]).ToArray();
    }
}
