# G7：Document 与组合根

- 新增稳定 ID 和第十一个 Persistable Document；普通 Document、Tool、Workflow Action 仍为零。
- 快照 schema 1 只保存轻量参数，恢复不自动解码/FFT。
- Document 使用 generation、独立取消与 stale 规则；两个 DI Scope 状态隔离测试通过。
