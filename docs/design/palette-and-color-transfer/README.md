# 调色板与颜色迁移

## 当前状态

V1 已完成生产实现与本地自动门禁，登记为 ImageLab 第十五个多实例 Persistable Document，稳定 ID 为
`myavalonia.plugin.image.lab.document.palette-color-transfer`。2026-08-31 实跑 Debug/Release 构建均为 0 警告、
0 错误，两配置测试均为 520/520 通过、0 失败、0 跳过。

这里的“完成”只指开发实现和本地自动门禁。真实 Host、ZIP、安装/升级/卸载、Windows CI 和发布验收未执行。

## 阅读顺序

1. [新手说明书](user-manual.md)：怎样载入、分析、冻结、迁移、重映射和读懂误差；
2. [精确指南](guide.md)：参数、状态、生命周期、所有权和扩展边界；
3. [数学原理](mathematical-principles.md)：sRGB、D65、Lab、HSV、聚类、迁移、JSD 与色差；
4. [报告 Schema](report-schema.md)：JSON/CSV 字段、N/A 与隐私边界；
5. [测试与门禁](testing.md)：520 项本地证据及未证明事项；
6. [实施基线](implementation.md)与 [G0–G9 历史](history/README.md)。

## V1 能做什么

- 对目标/参考执行 Alpha 加权 RGB、HSV、CIELAB 统计和固定尺寸分布；
- 使用 32³ RGB 聚合和确定性加权 Lab k-means 提取 2–12 个主色；
- 按占比、L* 或 HSV Hue 排序，显示排序不改变 cluster identity；
- 显式冻结目标或参考调色板；
- 对不同尺寸目标/参考执行 CIELAB 独立通道均值/标准差迁移；
- 完整 Lab 或保留目标 L*，强度为 0–1，0 保证目标逐字节不变；
- 按 ΔE76 精确映射到冻结调色板，并以 CIEDE2000、PSNR 和 SSIM 报告差异；
- 导出完整尺寸 PNG，以及不含像素与绝对路径的版本化 JSON/CSV 报告；
- 保存轻量快照，但恢复时不自动读取图片或运行算法。

## 解释边界

主色是固定协议下的聚类中心，不是图片“唯一真正颜色”。统计迁移只匹配全局独立通道的一阶/二阶统计，
不理解主体、肤色或区域。ΔE00、PSNR 与 SSIM 只描述结果相对原目标的变化，不代表审美提升。

V1 固定 sRGB D65，不支持 ICC、P3、HDR、CMYK、LUT、局部蒙版、抖动或自动“最佳参数”。
实现不使用 AIFLOW，也没有 Workflow Action、Workbench Command 或通用算法管线。
