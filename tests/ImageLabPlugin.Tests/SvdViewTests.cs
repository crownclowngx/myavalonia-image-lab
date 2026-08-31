using ImageLabPlugin.Features.SvdDecomposition;
using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class SvdViewTests
{
    [Theory]
    [InlineData(200d, 0d, 5, 0)]
    [InlineData(200d, 14d, 5, 0)]
    [InlineData(200d, 100d, 5, 2)]
    [InlineData(200d, 200d, 5, 4)]
    public void 曲线命中排除边距并覆盖首末索引(double width, double x, int count, int expected)
    {
        Assert.Equal(expected, SingularValueCurveControl.MapIndex(width, x, count));
    }

    [Fact]
    public void 曲线安全处理单值和过窄尺寸()
    {
        Assert.Equal(0, SingularValueCurveControl.MapIndex(20d, 10d, 5));
        Assert.Equal(0, SingularValueCurveControl.MapIndex(200d, 150d, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SingularValueCurveControl.MapIndex(200d, 10d, 0));
    }
}
