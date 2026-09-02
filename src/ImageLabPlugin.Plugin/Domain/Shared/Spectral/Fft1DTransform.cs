using System.Numerics;

namespace ImageLabPlugin.Domain.Shared.Spectral;

/// <summary>实现原地、迭代 radix-2 Cooley–Tukey FFT。</summary>
/// <remarks>
/// 正变换使用 <c>e^(-iθ)</c>，逆变换使用 <c>e^(+iθ)</c>。逆变换在一维末尾除以长度，因此二维
/// 行列逆变换自然得到 <c>1/(W×H)</c>。位反转先把输入排列成蝶形所需顺序，使每一级都能连续访问数据，
/// 同时避免递归和额外数组所有权。
/// </remarks>
internal sealed class Fft1DTransform
{
    public void Forward(Span<Complex> values) => Transform(values, inverse: false);

    public void Inverse(Span<Complex> values) => Transform(values, inverse: true);

    private static void Transform(Span<Complex> values, bool inverse)
    {
        ValidateLength(values.Length);
        for (var i = 1; i < values.Length; i++)
        {
            var reversed = ReverseBits(i, values.Length);
            if (i < reversed)
            {
                (values[i], values[reversed]) = (values[reversed], values[i]);
            }
        }

        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var angle = (inverse ? 2d : -2d) * Math.PI / length;
            var root = Complex.FromPolarCoordinates(1d, angle);
            var half = length / 2;
            for (var start = 0; start < values.Length; start += length)
            {
                var factor = Complex.One;
                for (var offset = 0; offset < half; offset++)
                {
                    var even = values[start + offset];
                    var odd = values[start + offset + half] * factor;
                    values[start + offset] = even + odd;
                    values[start + offset + half] = even - odd;
                    factor *= root;
                }
            }

            if (length > values.Length / 2)
            {
                break; // 防止极端长度左移溢出；受控图片缓冲不会接近该边界。
            }
        }

        if (inverse)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] /= values.Length;
            }
        }
    }

    private static void ValidateLength(int length)
    {
        if (length <= 0 || (length & (length - 1)) != 0)
        {
            throw new ArgumentException("FFT 缓冲长度必须是大于零的 2 的幂。", nameof(length));
        }
    }

    private static int ReverseBits(int value, int length)
    {
        var reversed = 0;
        for (var mask = length >> 1; mask != 0; mask >>= 1)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }

        return reversed;
    }
}
