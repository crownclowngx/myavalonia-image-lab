# G9：本地开发封板

2026-09-01 已执行最终本地命令：locked restore 通过；Debug/Release 构建均 0 警告、0 错误；Debug/Release 测试均 706/706 通过、0 失败、0 跳过；`git diff --check` 通过。完整命令与耗时以 [testing.md](../testing.md) 为唯一证据入口。

本 Gate 只覆盖本地开发门禁：locked restore、Debug/Release warn-as-error build/test 与 `git diff --check`。它不包含 Windows CI、真实 Host、ZIP、安装、签名或发布门禁。
