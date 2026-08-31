# G6：Document、组合与 Standalone

- 登记稳定 ID，Module 当前共有十四个 Persistable Document；Document Scope 间 Session 与 Bitmap 隔离。
- 快照 schema 1 只保存轻量参数，未知/缺失路径安全恢复且不自动读图或分解。
- 真实 Module/DI 同时供插件与 Standalone 使用，Standalone 新增独立 Scope 和真实 View，不复制业务。
