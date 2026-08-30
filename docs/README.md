# ImageLabPlugin 开发快速开始

本解决方案用于开发 `myavalonia.plugin.image.lab` Managed Plugin。它把真实插件、独立 Avalonia 开发窗口和
自动化测试放在同一个解决方案中，使界面与业务代码既能快速预览，也能由 MyAvaloniaManagement Host
按正式插件协议加载。

## 项目结构

```text
ImageLabPlugin/
├─ ImageLabPlugin.slnx
├─ src/
│  ├─ ImageLabPlugin.Plugin/       # 唯一真实插件程序集和正式交付内容
│  └─ ImageLabPlugin.Standalone/   # 只供本地开发的 Avalonia 窗口
├─ tests/
│  └─ ImageLabPlugin.Tests/        # 插件业务、状态和注册行为测试
└─ docs/                       # 当前项目随模板生成的开发说明
```

`ImageLabPlugin.Plugin` 是唯一正式插件项目。Standalone 和 Tests 都直接引用它，不能各自复制一套 View、
ViewModel、服务或贡献清单。

## 最短开发流程

在解决方案根目录打开 PowerShell：

```powershell
dotnet restore
dotnet build -c Debug -warnaserror
dotnet test -c Debug --no-build
dotnet run --project src/ImageLabPlugin.Standalone
```

Standalone 适合快速检查 AXAML、编译绑定、命令和插件自身对象图。写到可以联调时，再把干净的插件目录
部署到真实 Host；发布前则必须生成正式 ZIP。不要把 Standalone 能运行当成 Host 验收已经通过。

## 产品与开发文档

1. [未来可能支持能力列表](future-capabilities.md)
2. [V1 鲁棒性实验室实施计划](design/robustness-lab-v1-implementation-plan.md)
3. [鲁棒性实验室用户指南](robustness-lab-user-guide.md)
4. [鲁棒性实验室测试门禁](robustness-lab-testing.md)
5. [鲁棒性报告 schema](design/robustness-lab-report-schema.md)
6. [鲁棒性实验室 G0–G9 实施记录](plan-history/robustness-lab/README.md)
7. [V1 图像比较实验室实施计划](design/image-compare-lab-v1-implementation-plan.md)
4. [图像比较实验室用户指南](image-compare-lab-user-guide.md)
5. [图像比较实验室测试门禁](image-compare-lab-testing.md)
6. [图像比较实验室 G0–G7 实施记录](plan-history/image-compare-lab/README.md)
7. [V1 频域分析器实施计划](design/spectrum-inspector-v1-implementation-plan.md)
8. [频域分析器用户指南](spectrum-inspector-user-guide.md)
9. [频域分析器测试门禁](spectrum-inspector-testing.md)
10. [频域分析器 G0–G7 实施记录](plan-history/spectrum-inspector/README.md)
11. [V1 频域隐式水印实施计划](design/frequency-watermark-v1-implementation-plan.md)
12. [公共图像领域边界](design/image-domain-boundaries.md)
13. [V1 线格式、密码学与安全边界](design/frequency-watermark-v1-protocol.md)
14. [水印用户使用说明](frequency-watermark-user-guide.md)
15. [水印自动测试与质量门禁](frequency-watermark-testing.md)
16. [水印 G0–G9 实施记录](plan-history/frequency-watermark/README.md)
17. [项目、Host 与 Standalone 窗口职责](project-and-window-responsibilities.md)
18. [临时部署、正式发布与验收](deployment-and-release.md)

## 开发前记住

- `myavalonia.plugin.image.lab` 是持久身份，发布后不要因为显示名、项目名或文件夹改名而改变它。
- manifest 由 Build 包生成，不要手写或复制一份长期维护。
- 插件只通过公开 Plugin SDK 接入 Host，不引用 Host 内部项目。
- 新增插件运行时 NuGet 包时，要同时更新根目录 `Directory.Packages.props`、Plugin 项目的
  `PackageReference` 和 `ManagedPluginPrivatePackage`；完整示例见部署文档。
- 当前交付目标是 Windows x64；插件替换后必须完整重启 Host，不支持热更新。
- 当前实现登记五个 Persistable Document；鲁棒性实验室是第五个多实例、可持久化工作上下文。
- 当前不登记 Tool、Workflow Action 或 Workbench Command，也不使用 AIFLOW。
- 当前不执行 Windows CI、真实 Host、ZIP 和发布封板；这些结论不能由 Standalone 代替。
