# ImageLab 文档中心

ImageLab 当前提供十四项产品能力：频域隐式水印、频域分析器、图像比较实验室、鲁棒性实验室、感知指纹、位平面观察器、LSB 隐写与统计实验、卷积核实验台、小波实验室、频域滤波、频谱遮罩编辑器、周期噪声与陷波器、奇异值分解重建和调色板与颜色迁移。`docs` 根目录只保留本索引与未来能力清单；所有现有设计、使用和测试资料统一放在 `design` 下。

## 我是第一次使用

不需要先理解公式，直接选择目标能力的新手说明书：

1. [频域隐式水印新手说明书](design/frequency-watermark/user-manual.md)：把文字或文件藏入图片，再检测和提取。
2. [频域分析器新手说明书](design/spectrum-inspector/user-manual.md)：从“低频和高频是什么”开始观察频谱。
3. [图像比较实验室新手说明书](design/image-compare-lab/user-manual.md)：比较两张同尺寸图片并定位差异。
4. [鲁棒性实验室新手说明书](design/robustness-lab/user-manual.md)：用受控扰动测试水印恢复边界。
5. [感知指纹新手说明书](design/image-fingerprint/user-manual.md)：比较两张图片的 aHash、dHash 和 pHash。
6. [位平面观察器新手说明书](design/bit-plane-viewer/user-manual.md)：从 bit 7、bit 0 和掩码开始观察 8 位通道。
7. [LSB 隐写与统计实验新手说明书](design/lsb-steganography-lab/user-manual.md)：写入独立像素域 Frame，并观察位置、统计和脆弱性。
8. [卷积核实验台新手说明书](design/convolution-playground/user-manual.md)：编辑空间核并观察边界、差异、频响和像素贡献。
9. [小波实验室新手说明书](design/wavelet-lab/user-manual.md)：观察多层子带、重建、阈值去噪和有限载体比较。
10. [频域滤波新手说明书](design/frequency-filter/user-manual.md)：实验三类径向滤波器、输出语义、副作用和空间近似。
11. [频谱遮罩编辑器新手说明书](design/frequency-mask-editor/user-manual.md)：在中心化频谱上绘制共轭安全遮罩并观察重建。
12. [周期噪声与陷波器新手说明书](design/periodic-noise-removal/user-manual.md)：复核候选频率峰，以必须人工采用的共轭陷波草案观察损失。
13. [奇异值分解重建新手说明书](design/svd-decomposition/user-manual.md)：观察奇异值、Rank-k、秩一分量与颜色策略差异。
14. [调色板与颜色迁移新手说明书](design/palette-and-color-transfer/user-manual.md)：观察颜色分布、主色、统计迁移与固定调色板量化。

## 我需要开发或维护

进入 [设计文档总览](design/README.md)。每项能力目录都有统一结构：

- `implementation.md`：现有实施计划；
- `testing.md`：自动测试和门禁证据；
- `guide.md`：精确的开发者/高级用户指南；
- `user-manual.md`：降低技术背景要求的新手说明；
- `mathematical-principles.md`：涉及的数学原理背景；
- `history/`：实施阶段记录。

公共架构、Host/Standalone 职责、领域边界和发布资料见 [design/shared](design/shared/README.md)。尚未实现或计划中的方向见 [未来能力](future-capabilities.md)。最近完成的分析能力从[频域滤波](design/frequency-filter/README.md)、[频谱遮罩编辑器](design/frequency-mask-editor/README.md)、[周期噪声与陷波器](design/periodic-noise-removal/README.md)、[奇异值分解重建](design/svd-decomposition/README.md)和[调色板与颜色迁移](design/palette-and-color-transfer/README.md)专用入口进入。

## 项目与最短开发流程

```text
ImageLabPlugin/
├─ src/
│  ├─ ImageLabPlugin.Plugin/       # 唯一真实插件程序集和正式交付内容
│  └─ ImageLabPlugin.Standalone/   # 只供本地开发的 Avalonia 窗口
├─ tests/ImageLabPlugin.Tests/     # 业务、状态和注册行为测试
└─ docs/                           # 本文档中心
```

在解决方案根目录运行：

```powershell
dotnet restore
dotnet build -c Debug -warnaserror
dotnet test -c Debug --no-build
dotnet run --project src/ImageLabPlugin.Standalone
```

Standalone 适合检查 AXAML、编译绑定、命令和插件对象图，不能替代真实 Host、正式 ZIP 或发布验收。`ImageLabPlugin.Plugin` 是唯一正式插件项目；Standalone 和 Tests 直接引用它，不应复制 View、ViewModel、服务或贡献清单。

## 开发边界

- `myavalonia.plugin.image.lab` 是持久身份，不能随显示名或目录名改变。
- manifest 由构建生成，不手工长期维护。
- 插件只通过公开 Plugin SDK 接入 Host，不引用 Host 内部项目。
- 当前实现登记十五个 Persistable Document，分别是水印写入、提取与验证、频域分析器、图像比较实验室、鲁棒性实验室、感知指纹、位平面观察器、LSB 隐写与统计实验、卷积核实验台、小波实验室、频域滤波、频谱遮罩编辑器、周期噪声与陷波器、奇异值分解重建和调色板与颜色迁移。
- 当前不登记 Tool、Workflow Action 或 Workbench Command，也不使用 AIFLOW。
- 当前没有执行 Windows CI、真实 Host、ZIP 和发布封板，不能用 Standalone 结果冒充这些结论。
