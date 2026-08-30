# G7 本地开发封板

状态：本地自动门禁完成；人工与发布阶段延期（2026-08-30）。

已实际执行：

- `dotnet restore ImageLabPlugin.slnx --locked-mode`；
- Debug `-warnaserror` build：零警告、零错误；tests 191/191、零跳过；
- Release `-warnaserror` build：零警告、零错误；tests 191/191、零跳过；
- 新增 README、指南、用户说明、数学原理、测试专文和 G0–G7 历史，并同步四个公共入口；
- 检查未新增 NuGet、AIFLOW、Workflow Action、Workbench Command、Windows CI 或发布脚本。

设计思路：本地门禁只验证开发配置和自动证据，不冒充发布。总数从起始 149 增至 191；既有测试未删除、未跳过、未放宽。
资源门禁采用 16 MP/64 MiB、单 BytePlane、四张 1024 代理和按需完整导出的结构约束，没有伪造性能数字。

未执行：实施计划第 14 节有限人工清单、真实 Host Catalog/Dock/保存恢复/卸载、ZIP、Windows CI、目标设备性能与泄漏、
正式发布。回滚可按 G1–G6 边界移除新 Document；共享 YCbCr 原语被既有回归共同使用，不应在不跑全门禁时单独回退。
