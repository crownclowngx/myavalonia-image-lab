using Xunit;

namespace ImageLabPlugin.Tests;

public sealed class PoissonBlendingViewTests
{
    [Fact]
    public void Xaml包含五个专用控件中文可访问名称和非颜色限制说明()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "ImageLabPlugin.Plugin", "Features", "PoissonBlending", "PoissonBlendingView.axaml"));
        foreach (var control in new[] { "PoissonSourceMaskCanvas", "PoissonPlacementCanvas", "PoissonFieldView", "PoissonConvergenceChart", "PoissonComparisonControl" })
            Assert.Contains(control, source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", source, StringComparison.Ordinal);
        Assert.Contains("半透明", source, StringComparison.Ordinal);
        Assert.Contains("不证明视觉质量更好", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        { if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException();
    }
}
