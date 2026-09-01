# Hybrid Image 测试与本地门禁

## 最终证据

日期：2026-09-01；环境：Windows、.NET 10、C# 14、Avalonia 12.1；工作树包含本次 Hybrid Image 实现。

| 门禁 | 结果 |
| --- | --- |
| `dotnet restore ImageLabPlugin.slnx --locked-mode` | 通过；锁定依赖无变化 |
| Debug `--no-restore -warnaserror` 构建 | 通过；0 警告、0 错误；约 1.66 s |
| Debug `--no-build --no-restore` 测试 | 706/706 通过；0 失败、0 跳过；约 6 s |
| Release `--no-restore -warnaserror` 构建 | 通过；0 警告、0 错误；约 4.87 s |
| Release `--no-build --no-restore` 测试 | 706/706 通过；0 失败、0 跳过；约 3 s |
| `git diff --check` | 通过；无空白错误 |

起始基线是 666/666；本次新增 40 个 Hybrid 专用自动测试，总数增至 706。

## 专用覆盖

- G1：两点/三点相似变换、镜像拒绝、短基线、解析逆变换、文化和顺序无关指纹、裁切往返；
- G2：identity/亚像素双线性、像素中心、越界无效、预取消、最大矩形 tie-break、固定种子暴力面积对照、用户裁切边界；
- G3：Gaussian 核长度/对称/归一、常量保持、Reflect101、有符号高频、gain=0、黑图、白底 Alpha；
- G4：raw-before-byte 面积平均、奇数尺寸、红青错位边缘、理论 f50 单调性；
- G5：A/B 各解码一次、代理/完整尺寸源隔离、generation 提交、迟到拒绝、资源预算；
- G6：strict recipe round-trip、未知/重复字段、篡改、当前完整结果导出、输入覆盖拒绝、真实目标回读；
- G7：十九个 Document 顺序、零 Tool、Scope 隔离、singleton 服务、快照脱敏、类型/AXAML 编译、letterbox 坐标和依赖扫描。

## 已证明

- Domain 不依赖 Avalonia、IO、JSON、DI 或 Features；
- 固定 Gaussian 和相似变换采用 sealed 服务，没有机械 Strategy/Factory；
- 候选结果只有在所有分量、四尺度、重影和频谱完成后才可按 generation 原子提交；
- 完整尺寸直接使用首次解码原图，不由代理放大；
- recipe/report/snapshot 不保存绝对路径或图像数组；
- 未新增 NuGet、AIFLOW、Workflow Action、Workbench Command、Windows CI 或发布文件。

## 尚未证明

- 未使用真实人脸、建筑与文字配对执行主观感知人工观察；
- 未验证所有显示器、观看距离、视力或图片组合都产生明显主体切换；
- 未执行真实 Host Catalog/Dock/布局恢复/卸载、AssemblyLoadContext、ZIP、安装、签名或发布门禁；
- 未做外部 profiler 或不同硬件上的交互延迟/峰值内存测量。

这些事项不影响本地自动门禁结论，但在正式发布前必须按产品风险重新执行并记录。
