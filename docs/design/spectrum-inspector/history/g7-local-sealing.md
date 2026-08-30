# G7 本地开发封板记录

状态：自动门禁完成；人工/发布阶段延期（2026-08-30）。

已执行：

- `dotnet restore ImageLabPlugin.slnx --locked-mode`；
- Debug `-warnaserror` build 与 68/68 tests；
- Release 配置 `-warnaserror` build 与 68/68 tests；
- 用户指南、测试门禁、公共领域边界、索引和 G0–G7 记录同步；
- 代码与项目中未新增 AIFLOW、Workflow Action、Workbench Command、Windows CI 或发布流程。

未执行：Standalone 的 512/1024/2048 六通道全量人工交互、真实 Host Catalog/Dock/恢复/卸载、ZIP、
Windows CI 和正式发布。Release 仅为本地编译配置回归，不是发布门禁。因此当前结论是“开发实现与自动门禁
完成”，不是“已发布”。回滚时先移除第三个 Module 贡献和 Spectrum Feature，再按依赖方向移除应用/领域代码；
两个既有水印 Document 与协议保持不变。
