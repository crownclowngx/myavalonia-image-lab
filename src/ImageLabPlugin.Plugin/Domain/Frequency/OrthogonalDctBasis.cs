namespace ImageLabPlugin.Domain.Frequency;

/// <summary>正交 DCT-II 的余弦基与缩放系数；只承担数值事实，不决定输入是否中心化。</summary>
internal sealed class OrthogonalDctBasis
{
    private readonly double[,] _cosines;
    private readonly double[] _scales;

    public OrthogonalDctBasis(int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        Size = size;
        _cosines = new double[size, size];
        _scales = new double[size];
        for (var frequency = 0; frequency < size; frequency++)
        {
            _scales[frequency] = frequency == 0 ? 1d / Math.Sqrt(size) : Math.Sqrt(2d / size);
            for (var position = 0; position < size; position++)
                _cosines[position, frequency] = Math.Cos(((2d * position + 1d) * frequency * Math.PI) / (2d * size));
        }
    }

    public int Size { get; }
    public double Cosine(int position, int frequency) => _cosines[position, frequency];
    public double Scale(int frequency) => _scales[frequency];
}
