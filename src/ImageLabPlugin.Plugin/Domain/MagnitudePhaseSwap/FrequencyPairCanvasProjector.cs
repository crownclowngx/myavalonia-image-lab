using System.Buffers.Binary;
using System.Security.Cryptography;
using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.MagnitudePhaseSwap;

/// <summary>把任意 RGBA 图片确定性投影到共同方形亮度画布。</summary>
/// <remarks>
/// Alpha 先在白色 sRGB 背景合成，再按 BT.601 的 0.299/0.587/0.114 提取亮度。FitContain 保持比例并居中；
/// 缩小时使用像素面积覆盖积分以抑制混叠，放大时使用像素中心双线性采样。内容外固定填 255，既不隐式裁切
/// 主体，也不使用平台相关缩放器。循环按目标行观察取消，资源长度在分配前用 checked 验证。
/// </remarks>
internal sealed class FrequencyPairCanvasProjector
{
    public FrequencyPairCanvas Project(PixelImage source, int canvasSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        MagnitudePhaseCanvasSize.Validate(canvasSize);
        _ = checked(canvasSize * canvasSize);
        var scale = Math.Min((double)canvasSize / source.Size.Width, (double)canvasSize / source.Size.Height);
        var width = Math.Clamp((int)Math.Round(source.Size.Width * scale, MidpointRounding.AwayFromZero), 1, canvasSize);
        var height = Math.Clamp((int)Math.Round(source.Size.Height * scale, MidpointRounding.AwayFromZero), 1, canvasSize);
        var content = new FrequencyPairContentRectangle((canvasSize - width) / 2, (canvasSize - height) / 2, width, height);
        var sourceLuma = CreateWhiteCompositeLuma(source, cancellationToken);
        var target = new double[checked(canvasSize * canvasSize)];
        Array.Fill(target, 255d);
        var shrink = width < source.Size.Width || height < source.Size.Height;
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                target[((content.Y + y) * canvasSize) + content.X + x] = shrink
                    ? SampleArea(sourceLuma, source.Size.Width, source.Size.Height, x, y, width, height)
                    : SampleBilinear(sourceLuma, source.Size.Width, source.Size.Height, x, y, width, height);
            }
        }
        return new FrequencyPairCanvas(canvasSize, content, target, Fingerprint(canvasSize, content, target));
    }

    private static double[] CreateWhiteCompositeLuma(PixelImage source, CancellationToken cancellationToken)
    {
        var result = new double[checked((int)source.Size.PixelCount)];
        for (var y = 0; y < source.Size.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Size.Width; x++)
            {
                var (r, g, b, a) = source.GetPixel(x, y);
                var alpha = a / 255d;
                var red = (r * alpha) + (255d * (1d - alpha));
                var green = (g * alpha) + (255d * (1d - alpha));
                var blue = (b * alpha) + (255d * (1d - alpha));
                result[(y * source.Size.Width) + x] = (.299d * red) + (.587d * green) + (.114d * blue);
            }
        }
        return result;
    }

    private static double SampleBilinear(double[] source, int sourceWidth, int sourceHeight,
        int targetX, int targetY, int targetWidth, int targetHeight)
    {
        var sx = Math.Clamp(((targetX + .5d) * sourceWidth / targetWidth) - .5d, 0d, sourceWidth - 1d);
        var sy = Math.Clamp(((targetY + .5d) * sourceHeight / targetHeight) - .5d, 0d, sourceHeight - 1d);
        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        var x1 = Math.Min(x0 + 1, sourceWidth - 1);
        var y1 = Math.Min(y0 + 1, sourceHeight - 1);
        var tx = sx - x0;
        var ty = sy - y0;
        var top = Lerp(source[(y0 * sourceWidth) + x0], source[(y0 * sourceWidth) + x1], tx);
        var bottom = Lerp(source[(y1 * sourceWidth) + x0], source[(y1 * sourceWidth) + x1], tx);
        return Lerp(top, bottom, ty);
    }

    private static double SampleArea(double[] source, int sourceWidth, int sourceHeight,
        int targetX, int targetY, int targetWidth, int targetHeight)
    {
        var left = (double)targetX * sourceWidth / targetWidth;
        var right = (double)(targetX + 1) * sourceWidth / targetWidth;
        var top = (double)targetY * sourceHeight / targetHeight;
        var bottom = (double)(targetY + 1) * sourceHeight / targetHeight;
        double sum = 0d, area = 0d;
        for (var sy = (int)Math.Floor(top); sy < Math.Ceiling(bottom); sy++)
        {
            if ((uint)sy >= (uint)sourceHeight) continue;
            var vertical = Math.Max(0d, Math.Min(bottom, sy + 1d) - Math.Max(top, sy));
            for (var sx = (int)Math.Floor(left); sx < Math.Ceiling(right); sx++)
            {
                if ((uint)sx >= (uint)sourceWidth) continue;
                var horizontal = Math.Max(0d, Math.Min(right, sx + 1d) - Math.Max(left, sx));
                var weight = horizontal * vertical;
                sum += source[(sy * sourceWidth) + sx] * weight;
                area += weight;
            }
        }
        return area <= 0d ? 255d : sum / area;
    }

    private static double Lerp(double first, double second, double amount) => first + ((second - first) * amount);

    private static string Fingerprint(int size, FrequencyPairContentRectangle content, double[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> facts = stackalloc byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(facts, size);
        BinaryPrimitives.WriteInt32LittleEndian(facts[4..], content.X);
        BinaryPrimitives.WriteInt32LittleEndian(facts[8..], content.Y);
        BinaryPrimitives.WriteInt32LittleEndian(facts[12..], content.Width);
        BinaryPrimitives.WriteInt32LittleEndian(facts[16..], content.Height);
        hash.AppendData(facts);
        Span<byte> bytes = stackalloc byte[8];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..24].ToLowerInvariant();
    }
}
