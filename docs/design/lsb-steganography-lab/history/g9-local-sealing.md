# G9 本地开发封板

当前状态：已完成本地开发封板。

- 已完成：专用 README、手册、指南、数学、协议、报告、测试和 G0–G9 历史；公共入口同步。
- 最终证据：2026-08-30 `dotnet restore --locked-mode` 成功；Debug 与 Release warn-as-error build 均零警告/零错误；两配置均 241/241、零失败、零跳过。相对 191 基线新增 50 个 runner 用例。
- 明确未执行：Windows CI、ZIP、真实 Host、安装/升级/卸载和发布门禁。
- 回滚：按 G8→G6→G1 逆序移除；共享 CRC 和既有扰动继续由原能力使用。
