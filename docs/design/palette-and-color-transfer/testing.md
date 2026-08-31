# 调色板与颜色迁移测试与本地门禁

## 2026-08-31 实跑结果

| 配置 | build | test |
| --- | --- | --- |
| Debug | 0 警告、0 错误 | 520/520 通过，0 失败，0 跳过 |
| Release | 0 警告、0 错误 | 520/520 通过，0 失败，0 跳过 |

起始基线为 479/479；本轮净增 41 个用例/数据行，历史测试没有删除、跳过或放宽。

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

## 已证明

- sRGB 分段、D65 白点、Lab/HSV 原色、灰阶 Hue N/A、字节往返；
- Sharma/Wu/Dalal CIEDE2000 参考对、对称性与 ΔE76；
- 色域映射的 L*/hue 方向、确定次数和结构化分类；
- 全透明/半透明/不透明权重、隐藏 RGB 排除与直方图守恒；
- 固定数组尺寸、JSD 相同/互斥/对称；
- 32³ 聚合、二色比例、聚类重复 fingerprint、排序不改变身份；
- 不同尺寸迁移、强度 0 逐字节相等、保留 L*、非法强度；
- 固定调色板输出集合、Alpha/透明 RGBA、计数与探针；
- Session 换图失效、快照不含像素/palette 且恢复不自动读取；
- JSON/CSV schema、BOM、转义、N/A、隐私与非有限数拒绝；
- 第十五个唯一 Persistable Document、Scoped 隔离和 Headless View/控件加载；
- Domain 依赖方向、中文核心注释、NuGet 不变、无 AIFLOW/Windows workflow。

## 性能门禁

测试断言固定数组和聚合上限，不使用依赖机器速度的毫秒阈值。完整扫描按行取消；未建立逐像素 Lab 或 ΔE 数组。

## 未证明

真实 Host、多实例布局恢复、ZIP、安装/升级/卸载、Windows CI、16MP 长时间压力、不同 GPU/DPI、自然图片主观评测、
ICC/P3/HDR 与发布安全审查均延期。Standalone/Headless 只证明开发期对象图和布局可以加载。
