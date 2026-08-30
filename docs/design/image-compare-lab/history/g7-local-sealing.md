# G7 本地开发封板记录

状态：自动门禁完成；人工/发布阶段延期（2026-08-30）。

已执行：

- `dotnet restore ImageLabPlugin.slnx --locked-mode`；
- Debug `-warnaserror` build 与 97/97 tests；
- Release `-warnaserror` build 与 97/97 tests；
- 用户指南、测试专文、公共领域边界、未来能力、索引和 G0–G7 记录同步；
- 代码与项目未新增 NuGet、AIFLOW、Workflow Action、Workbench Command、Windows CI 或发布流程。

结构证据：完整统计 O(1) 额外内存；长期两张全图；两张显示代理、五 byte/像素基础差异场与当前 RGBA 投影最大边
1024；直方图固定 12×256 个 long。自动门禁没有机器相关严格耗时断言。

未执行：Standalone 全量人工交互、真实 Host Catalog/Dock/恢复/卸载、ZIP、Windows CI、目标设备性能和正式发布。
Release 只表示本地配置回归。因此当前结论是“开发实现与自动门禁完成”，不是“已发布”。
