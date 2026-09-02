using System.Text.RegularExpressions;
using ImageLabPlugin.Domain.Shared.Spectral;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.Shared.Spatial;
using ImageLabPlugin.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImageLabPlugin.Tests;

/// <summary>锁定同程序集 Domain 的 Shared Kernel 单向依赖和 Capability 隔离边界。</summary>
public sealed class SharedDomainArchitectureTests
{
    [Fact]
    public void Shared不依赖应用基础设施UI序列化DI或文件系统()
    {
        var shared = ReadAll(Path.Combine(DomainRoot(), "Shared"));
        foreach (var forbidden in new[]
        {
            "ImageLabPlugin.Application", "ImageLabPlugin.Infrastructure", "ImageLabPlugin.Features",
            "using Avalonia", "MyAvaloniaManagement", "System.Text.Json",
            "Microsoft.Extensions.DependencyInjection", "File."
        })
        {
            Assert.DoesNotContain(forbidden, shared, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Imaging不反向依赖Analysis()
    {
        var imaging = ReadAll(Path.Combine(DomainRoot(), "Shared", "Imaging"));
        Assert.DoesNotContain("ImageLabPlugin.Domain.Shared.Analysis", imaging, StringComparison.Ordinal);
    }

    [Fact]
    public void 普通Capability只依赖自身和Shared()
    {
        var domain = DomainRoot();
        var reference = new Regex(@"ImageLabPlugin\.Domain\.([A-Za-z0-9_]+)", RegexOptions.CultureInvariant);
        foreach (var capability in Directory.EnumerateDirectories(domain)
                     .Where(path => !StringComparer.Ordinal.Equals(Path.GetFileName(path), "Shared")))
        {
            var ownName = Path.GetFileName(capability);
            foreach (var file in Directory.EnumerateFiles(capability, "*.cs", SearchOption.AllDirectories))
            foreach (Match match in reference.Matches(File.ReadAllText(file)))
            {
                var referenced = match.Groups[1].Value;
                Assert.True(referenced is "Shared" || StringComparer.Ordinal.Equals(referenced, ownName),
                    $"{Path.GetRelativePath(domain, file)} 不应直接依赖同级 Capability {referenced}。");
            }
        }
    }

    [Fact]
    public void 旧目录旧命名空间和重复共享服务登记均不存在()
    {
        var domain = DomainRoot();
        foreach (var oldDirectory in new[] { "Imaging", "Comparison", "Frequency", "Checksums" })
            Assert.False(Directory.Exists(Path.Combine(domain, oldDirectory)), $"旧目录仍存在：{oldDirectory}");
        Assert.False(Directory.Exists(Path.Combine(domain, "Robustness", "Operators")));

        var production = ReadAll(Path.Combine(RepositoryRoot(), "src"));
        Assert.DoesNotMatch(@"ImageLabPlugin\.Domain\.(Imaging|Comparison|Frequency|Checksums)(?:\.|;)", production);
        Assert.DoesNotContain("ImageLabPlugin.Domain.Robustness.Operators", production, StringComparison.Ordinal);

        var services = new ServiceCollection();
        services.AddImageLabPluginServices();
        foreach (var sharedService in new[]
        {
            typeof(RadialLogPowerBaseline), typeof(FrequencySpectrumBuilder),
            typeof(ChannelDifferenceProjector), typeof(SpatialConvolver),
            typeof(FullReferenceQualityAnalyzer)
        })
        {
            Assert.Single(services, value => value.ServiceType == sharedService);
        }
    }

    private static string DomainRoot() => Path.Combine(RepositoryRoot(), "src", "ImageLabPlugin.Plugin", "Domain");

    private static string ReadAll(string directory) => string.Join('\n',
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(File.ReadAllText));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ImageLabPlugin.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("未找到 ImageLabPlugin.slnx。");
    }
}
