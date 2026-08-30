# ImageLab 文档中心

ImageLab 当前提供四项核心能力：频域隐式水印、频域分析器、图像比较实验室和鲁棒性实验室。`docs` 根目录只保留本索引与未来能力清单；所有现有设计、使用和测试资料统一放在 `design` 下。

## 我是第一次使用

不需要先理解公式，直接选择目标能力的新手说明书：

1. [频域隐式水印新手说明书](design/frequency-watermark/user-manual.md)：把文字或文件藏入图片，再检测和提取。
2. [频域分析器新手说明书](design/spectrum-inspector/user-manual.md)：从“低频和高频是什么”开始观察频谱。
3. [图像比较实验室新手说明书](design/image-compare-lab/user-manual.md)：比较两张同尺寸图片并定位差异。
4. [鲁棒性实验室新手说明书](design/robustness-lab/user-manual.md)：用受控扰动测试水印恢复边界。

## 我需要开发或维护

进入 [设计文档总览](design/README.md)。每项能力目录都有统一结构：

- `implementation.md`：现有实施计划；
- `testing.md`：自动测试和门禁证据；
- `guide.md`：精确的开发者/高级用户指南；
- `user-manual.md`：降低技术背景要求的新手说明；
- `mathematical-principles.md`：涉及的数学原理背景；
- `history/`：实施阶段记录。

公共架构、Host/Standalone 职责、领域边界和发布资料见 [design/shared](design/shared/README.md)。尚未实现或计划中的方向见 [未来能力](future-capabilities.md)；下一项“感知指纹”已有独立的 [V1 实施计划](design/image-fingerprint/implementation.md)，但当前尚未实现，不能按用户说明书使用。

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
- 当前实现登记五个 Persistable Document，分别是水印写入、提取与验证、频域分析器、图像比较实验室和鲁棒性实验室。
- 当前不登记 Tool、Workflow Action 或 Workbench Command，也不使用 AIFLOW。
- 当前没有执行 Windows CI、真实 Host、ZIP 和发布封板，不能用 Standalone 结果冒充这些结论。
