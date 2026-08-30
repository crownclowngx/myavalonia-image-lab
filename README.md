# ImageLabPlugin

这是 ImageLab 的 Managed Plugin 解决方案。当前提供频域隐式水印相关双 Document，以及“频域分析器”“图像比较实验室”“鲁棒性实验室”“感知指纹”“位平面观察器”“LSB 隐写与统计实验”“卷积核实验台”和“小波实验室”共十个 Persistable Document：
前者在图片 Y 通道的 8×8 DCT 中频系数写入受容量限制的 Payload；频域分析器提供六通道 FFT/DCT 与频带重建；
比较实验室对两张同尺寸图片提供同步视图与客观指标；鲁棒性实验室用确定性扰动链、单参数扫描、分步水印诊断、
成功率曲线和 Profile 矩阵测量恢复边界；感知指纹使用 aHash、dHash、pHash、汉明距离和受控稳定性试验比较两张显式图片；
位平面观察器拆分 R/G/B/Alpha/Y 的 8 位样本并提供掩码重建、统计、探针和 PNG 导出；LSB 隐写与统计实验以独立 `ILSB` Frame、可复现槽位、统计对比和受控扰动解释像素域 LSB 的可检测性与脆弱性；卷积核实验台用真二维卷积、四种边界/归一化、双核梯度、频响和像素贡献解释空间核；小波实验室提供 Haar/CDF 5/3、多层子带、阈值去噪、有限扫描及 DCT/DWT 载体比较。

真实插件只位于 `src/ImageLabPlugin.Plugin`；`Standalone` 通过同一个 Module 和 DI 入口预览“水印写入”、
“提取与验证”“频域分析器”“图像比较实验室”“鲁棒性实验室”“感知指纹”“位平面观察器”“LSB 隐写与统计实验”“卷积核实验台”和“小波实验室”十个真实 Document，不复制业务实现。

> 第一次开始开发前，请先阅读 [项目文档与快速开始](docs/README.md)。其中说明了三个子项目和
> Standalone 窗口的职责、接入真实 Host 的边界，以及临时部署和正式 ZIP 发布流程。

```powershell
dotnet restore
dotnet build ImageLabPlugin.slnx -c Debug -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build
dotnet run --project src/ImageLabPlugin.Standalone
```

当前阶段不执行 Windows CI、ZIP 或发布门禁。准备发布时再按照
[部署与发布文档](docs/design/shared/deployment-and-release.md)执行真实 Host 和正式包验收。

全部资料从 [文档中心](docs/README.md) 或 [设计文档总览](docs/design/README.md) 进入。九项产品能力各自拥有统一目录：

- [频域隐式水印](docs/design/frequency-watermark/README.md)
- [频域分析器](docs/design/spectrum-inspector/README.md)
- [图像比较实验室](docs/design/image-compare-lab/README.md)
- [鲁棒性实验室](docs/design/robustness-lab/README.md)
- [感知指纹](docs/design/image-fingerprint/README.md)
- [位平面观察器](docs/design/bit-plane-viewer/README.md)
- [LSB 隐写与统计实验](docs/design/lsb-steganography-lab/README.md)
- [卷积核实验台](docs/design/convolution-playground/README.md)
- [小波实验室](docs/design/wavelet-lab/README.md)

每个目录都包含现有实施计划、测试门禁、详细指南、新手使用说明、数学原理和实施历史。当前实现完成本地开发自动门禁；真实 Host、ZIP、Windows CI 与发布封板仍未执行。

Wavelet Lab／小波实验室已完成[专用实现与证据文档](docs/design/wavelet-lab/README.md)；当前完成状态仍仅代表本地开发门禁，不代表发布。
