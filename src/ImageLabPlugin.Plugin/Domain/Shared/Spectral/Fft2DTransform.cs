using System.Numerics;

namespace ImageLabPlugin.Domain.Shared.Spectral;

/// <summary>用一维变换协调二维矩阵的行、列 FFT，并在每个边界观察取消。</summary>
internal sealed class Fft2DTransform(Fft1DTransform transform)
{
    public void Forward(Complex[] values, int width, int height, CancellationToken cancellationToken = default) =>
        Execute(values, width, height, inverse: false, cancellationToken);

    public void Inverse(Complex[] values, int width, int height, CancellationToken cancellationToken = default) =>
        Execute(values, width, height, inverse: true, cancellationToken);

    private void Execute(Complex[] values, int width, int height, bool inverse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateDimensions(values.Length, width, height);
        for (var row = 0; row < height; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = values.AsSpan(row * width, width);
            if (inverse) transform.Inverse(span); else transform.Forward(span);
        }

        // 列在行主序数组中不连续，因此只分配一份“最高列”缓冲并重复使用，不为每列制造临时垃圾。
        var column = new Complex[height];
        for (var x = 0; x < width; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var y = 0; y < height; y++) column[y] = values[(y * width) + x];
            if (inverse) transform.Inverse(column); else transform.Forward(column);
            for (var y = 0; y < height; y++) values[(y * width) + x] = column[y];
        }
    }

    private static void ValidateDimensions(int count, int width, int height)
    {
        if (width <= 0 || height <= 0 || (width & (width - 1)) != 0 || (height & (height - 1)) != 0)
        {
            throw new ArgumentException("二维 FFT 的宽高必须是大于零的 2 的幂。 ");
        }

        if (count != checked(width * height))
        {
            throw new ArgumentException("二维 FFT 缓冲长度与宽高不一致。", nameof(count));
        }
    }
}
