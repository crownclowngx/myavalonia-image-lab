# ImageLabPlugin

这是 ImageLab 的 Managed Plugin 解决方案。当前提供“频域隐式水印”“频域分析器”和“图像比较实验室”：
前者在图片 Y 通道的 8×8 DCT 中频系数写入受容量限制的 Payload；频域分析器提供六通道 FFT/DCT 与频带重建；
比较实验室对两张同尺寸图片提供同步视图、完整像素指标、差异/热力图、像素检查、直方图和 JSON 摘要。

真实插件只位于 `src/ImageLabPlugin.Plugin`；`Standalone` 通过同一个 Module 和 DI 入口预览“水印写入”、
“提取与验证”“频域分析器”和“图像比较实验室”四个真实 Document，不复制业务实现。

> 第一次开始开发前，请先阅读 [项目文档与快速开始](docs/README.md)。其中说明了三个子项目和
> Standalone 窗口的职责、接入真实 Host 的边界，以及临时部署和正式 ZIP 发布流程。

```powershell
dotnet restore
dotnet build ImageLabPlugin.slnx -c Debug -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build
dotnet run --project src/ImageLabPlugin.Standalone
```

当前阶段不执行 Windows CI、ZIP 或发布门禁。准备发布时再按照
[部署与发布文档](docs/deployment-and-release.md)执行真实 Host 和正式包验收。

完整设计从 [V1 实施计划](docs/design/frequency-watermark-v1-implementation-plan.md) 开始；协议与安全细节见
[V1 协议](docs/design/frequency-watermark-v1-protocol.md)，使用方式见
[用户说明](docs/frequency-watermark-user-guide.md)。

频域分析器从 [V1 实施计划](docs/design/spectrum-inspector-v1-implementation-plan.md) 开始；使用方式和数值语义见
[频域分析器用户指南](docs/spectrum-inspector-user-guide.md)，自动证据见
[频域分析器测试门禁](docs/spectrum-inspector-testing.md)。

图像比较实验室的设计见 [V1 实施计划](docs/design/image-compare-lab-v1-implementation-plan.md)，使用方式见
[用户指南](docs/image-compare-lab-user-guide.md)，自动证据见
[测试门禁](docs/image-compare-lab-testing.md)。当前实现完成本地开发自动门禁；真实 Host、ZIP、Windows CI 与发布封板仍未执行。
