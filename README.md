# ImageLabPlugin

这是 ImageLab 的 Managed Plugin 解决方案。第一个产品能力是“频域隐式水印”：在图片 Y 通道的 8×8 DCT
中频系数写入受容量限制的 Payload，并支持检测、纠错、可选解密、完整性验证与导出。

真实插件只位于 `src/ImageLabPlugin.Plugin`；`Standalone` 通过同一个 Module 和 DI 入口预览“水印写入”与
“提取与验证”两个真实 Document，不复制业务实现。

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
