using ImageLabPlugin.Domain.Comparison;
using ImageLabPlugin.Domain.Imaging;
using ImageLabPlugin.Domain.SvdDecomposition;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SvdDomainTests
{
    [Fact]
    public void 稠密矩阵复制输入并保持行列和转置约定()
    {
        var values = new[] { 1d, 2d, 3d, 4d, 5d, 6d };
        var matrix = new DenseMatrix(2, 3, values);
        values[0] = 99d;

        Assert.Equal(1d, matrix[0, 0]);
        Assert.Equal(6d, matrix[1, 2]);
        var transposed = matrix.Transpose();
        Assert.Equal((3, 2), (transposed.Rows, transposed.Columns));
        Assert.Equal(new[] { 1d, 4d, 2d, 5d, 3d, 6d }, transposed.Values.ToArray());
        Assert.Throws<ArgumentException>(() => new DenseMatrix(1, 1, new[] { double.NaN }));
        Assert.Throws<ArgumentException>(() => new DenseMatrix(1, 1, new[] { double.PositiveInfinity }));
    }

    [Fact]
    public void 因子防御复制且资源估算覆盖单三通道边界()
    {
        var u = new[] { 1d };
        var sigma = new[] { 2d };
        var v = new[] { 1d };
        var factors = new SvdFactors(1, 1, u, sigma, v,
            new SvdDiagnostics(true, 0, 0d, 0d, 0d, SvdRecipeFingerprint.NumericProtocol));
        u[0] = sigma[0] = v[0] = 9d;
        Assert.Equal(1d, factors.U.Span[0]);
        Assert.Equal(2d, factors.SingularValues.Span[0]);
        Assert.Equal(1d, factors.V.Span[0]);

        var single = SvdResourceEstimate.Create(128, 128, 1);
        var triple = SvdResourceEstimate.Create(256, 256, 3);
        Assert.Equal(16_384, single.Rows * single.Columns);
        Assert.True(triple.EstimatedPeakBytes > single.EstimatedPeakBytes * 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => SvdResourceEstimate.Create(257, 256, 1));
    }

    [Theory]
    [MemberData(nameof(GoldenMatrices))]
    public void Jacobi对手算矩阵给出降序奇异值并重建原矩阵(
        int rows, int columns, double[] values, double[] expectedSingularValues)
    {
        var source = new DenseMatrix(rows, columns, values);
        var factors = new JacobiSvdDecomposer().Decompose(source);
        var reconstructed = new LowRankReconstructor().Reconstruct(factors, factors.RankLimit);

        Assert.Equal(expectedSingularValues.Length, factors.SingularValues.Length);
        for (var index = 0; index < expectedSingularValues.Length; index++)
            Assert.InRange(Math.Abs(factors.SingularValues.Span[index] - expectedSingularValues[index]), 0d, 1e-9);
        AssertMatrixClose(source, reconstructed, 2e-9);
        Assert.True(factors.Diagnostics.Converged);
        Assert.True(factors.Diagnostics.MaximumUOrthogonalityError <= 2e-9);
        Assert.True(factors.Diagnostics.MaximumVOrthogonalityError <= 2e-9);
    }

    [Fact]
    public void 宽高矩阵互为转置时奇异值一致且经济型尺寸正确()
    {
        var tall = new DenseMatrix(3, 2, new[] { 3d, 0d, 0d, 2d, 0d, 0d });
        var wide = tall.Transpose();
        var decomposer = new JacobiSvdDecomposer();
        var first = decomposer.Decompose(tall);
        var second = decomposer.Decompose(wide);

        Assert.Equal(first.SingularValues.ToArray(), second.SingularValues.ToArray());
        Assert.Equal(6, first.U.Length); Assert.Equal(4, first.V.Length);
        Assert.Equal(4, second.U.Length); Assert.Equal(6, second.V.Length);
        AssertMatrixClose(wide, new LowRankReconstructor().Reconstruct(second, 2), 1e-10);
    }

    [Fact]
    public void Rank零和全秩遵守边界且能量误差单调()
    {
        var source = new DenseMatrix(2, 2, new[] { 4d, 0d, 0d, 3d });
        var factors = new JacobiSvdDecomposer().Decompose(source);
        var reconstructor = new LowRankReconstructor();
        Assert.Equal(new double[4], reconstructor.Reconstruct(factors, 0).Values.ToArray());
        AssertMatrixClose(source, reconstructor.Reconstruct(factors, 2), 1e-12);
        Assert.Throws<ArgumentOutOfRangeException>(() => reconstructor.Reconstruct(factors, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reconstructor.Reconstruct(factors, 3));

        var energy = new SingularValueEnergyAnalyzer().Analyze(factors);
        Assert.Equal(25d, energy.TotalEnergy, 10);
        Assert.Equal(0.64d, energy.Samples[0].CumulativeEnergy, 10);
        Assert.Equal(1d, energy.Samples[1].CumulativeEnergy, 10);
    }

    [Fact]
    public void 零矩阵能量不可用且不产生非有限值()
    {
        var factors = new JacobiSvdDecomposer().Decompose(new DenseMatrix(2, 3, new double[6]));
        var energy = new SingularValueEnergyAnalyzer().Analyze(factors);
        Assert.Equal(SvdEnergyStatus.NotApplicable, energy.Status);
        Assert.Equal(0, energy.NumericRank);
        Assert.All(energy.Samples, sample =>
        {
            Assert.Equal(0d, sample.EnergyShare);
            Assert.True(double.IsFinite(sample.CumulativeEnergy));
        });
    }

    [Fact]
    public void 分量投影使用对称色标且分量之和恢复Rank二()
    {
        var source = new DenseMatrix(2, 2, new[] { 4d, 0d, 0d, -3d });
        var factors = new JacobiSvdDecomposer().Decompose(source);
        var channel = new SvdChannelFactors(ImageChannel.Luma, 0d, source, factors);
        var projector = new SvdComponentProjector();
        var first = projector.Project(channel, 0);
        var second = projector.Project(channel, 1);
        Assert.True(first.DisplayScale >= Math.Max(Math.Abs(first.RawMinimum), Math.Abs(first.RawMaximum)));
        var sum = new double[4];
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 2; column++)
        {
            var index = (row * 2) + column;
            sum[index] = factors.SingularValues.Span[0] * factors.GetU(row, 0) * factors.GetV(column, 0) +
                factors.SingularValues.Span[1] * factors.GetU(row, 1) * factors.GetV(column, 1);
        }
        AssertMatrixClose(source, new DenseMatrix(2, 2, sum), 1e-10);
        var zeroPixel = first.Preview.GetPixel(1, 0);
        Assert.Equal(((byte)238, (byte)238, (byte)238), (zeroPixel.R, zeroPixel.G, zeroPixel.B));
        Assert.Equal(255, first.Preview.GetAlpha(0, 0));
        Assert.Equal(255, second.Preview.GetAlpha(1, 1));
    }

    [Fact]
    public void RGB全秩重建保持Alpha并复用共享质量分析器()
    {
        var image = CreateImage(2, 2, new byte[]
        {
            10, 20, 30, 1, 40, 50, 60, 2,
            70, 80, 90, 3, 100, 110, 120, 4
        });
        var executor = new SvdColorStrategyExecutor(new ImageChannelConverter(), new JacobiSvdDecomposer());
        var decomposition = executor.Decompose(image, "proxy", SvdColorStrategy.IndependentRgb, ImageChannel.Luma);
        var reconstructor = new LowRankReconstructor();
        var matrices = decomposition.Channels.Select(item => reconstructor.Reconstruct(item.Factors, 2)).ToArray();
        var rebuilt = new SvdImageReconstructor(new ImageChannelConverter()).Reconstruct(image, decomposition, matrices);
        var analyzer = new SvdReconstructionAnalyzer(new SingularValueEnergyAnalyzer(),
            new FullReferenceQualityAnalyzer(new ImagePairValidator()));
        var result = analyzer.Analyze(image, rebuilt.Image, decomposition, matrices, 2);

        Assert.Equal(image.Rgba.ToArray(), rebuilt.Image.Rgba.ToArray());
        Assert.Equal(0, rebuilt.Clipping.ClippedPixels);
        Assert.Equal(0d, result.Quality.RootMeanSquareErrorRgb);
        Assert.Equal(1d, result.AggregateEnergy);
    }

    [Fact]
    public void YCbCr的色度矩阵只中心化一次且Rank零回到中性色()
    {
        var image = CreateImage(1, 1, new byte[] { 30, 60, 90, 77 });
        var executor = new SvdColorStrategyExecutor(new ImageChannelConverter(), new JacobiSvdDecomposer());
        var decomposition = executor.Decompose(image, "proxy", SvdColorStrategy.IndependentYCbCr, ImageChannel.Luma);
        Assert.Equal(128d, decomposition.Channels[1].Neutral);
        Assert.Equal(YCbCrColorSpace.FromRgb(30, 60, 90).ChromaBlue - 128d,
            decomposition.Channels[1].SourceMatrix[0, 0], 10);
        var zero = decomposition.Channels.Select(item => new LowRankReconstructor().Reconstruct(item.Factors, 0)).ToArray();
        var rebuilt = new SvdImageReconstructor(new ImageChannelConverter()).Reconstruct(image, decomposition, zero).Image;
        var pixel = rebuilt.GetPixel(0, 0);
        Assert.Equal((byte)0, pixel.R); Assert.Equal((byte)0, pixel.G); Assert.Equal((byte)0, pixel.B);
        Assert.Equal((byte)77, pixel.A);
    }

    [Fact]
    public void 分解在预先取消时立即终止()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new JacobiSvdDecomposer().Decompose(
            new DenseMatrix(3, 3, new[] { 1d, 2d, 3d, 4d, 5d, 6d, 7d, 8d, 10d }), cancellation.Token));
    }

    [Fact]
    public void 达到最大Sweep时返回结构化未收敛错误()
    {
        var exception = Assert.Throws<SvdDecompositionException>(() =>
            new JacobiSvdDecomposer(0, JacobiSvdDecomposer.RelativeOrthogonalityTolerance).Decompose(
                new DenseMatrix(2, 2, new[] { 1d, 1d, 0d, 1d })));
        Assert.Equal(SvdFailureReason.NotConverged, exception.Reason);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void 最大代理档位的方阵在冻结资源边界内完成有限分解(int size)
    {
        var values = new double[checked(size * size)];
        for (var index = 0; index < size; index++) values[(index * size) + index] = size - index;

        var factors = new JacobiSvdDecomposer().Decompose(new DenseMatrix(size, size, values));

        Assert.Equal(size, factors.RankLimit);
        Assert.Equal((double)size, factors.SingularValues.Span[0]);
        Assert.Equal(1d, factors.SingularValues.Span[^1]);
        Assert.True(factors.Diagnostics.Converged);
    }

    public static TheoryData<int, int, double[], double[]> GoldenMatrices => new()
    {
        { 1, 1, new[] { 5d }, new[] { 5d } },
        { 1, 3, new[] { 3d, 4d, 0d }, new[] { 5d } },
        { 3, 1, new[] { 3d, 4d, 0d }, new[] { 5d } },
        { 2, 2, new[] { 4d, 0d, 0d, 3d }, new[] { 4d, 3d } },
        { 2, 2, new[] { 1d, 0d, 0d, 1d }, new[] { 1d, 1d } },
        { 2, 2, new[] { 1d, 1d, 0d, 1d }, new[] { 1.618033988749895d, 0.6180339887498948d } },
        { 2, 2, new[] { 0d, 0d, 0d, 0d }, new[] { 0d, 0d } },
        { 3, 2, new[] { 3d, 6d, 4d, 8d, 0d, 0d }, new[] { Math.Sqrt(125d), 0d } }
    };

    private static void AssertMatrixClose(DenseMatrix expected, DenseMatrix actual, double tolerance)
    {
        Assert.Equal((expected.Rows, expected.Columns), (actual.Rows, actual.Columns));
        for (var index = 0; index < expected.Values.Length; index++)
            Assert.InRange(Math.Abs(expected.Values.Span[index] - actual.Values.Span[index]), 0d, tolerance);
    }

    private static PixelImage CreateImage(int width, int height, byte[] rgba) =>
        new(new ImageSize(width, height), rgba);
}
