# G8 本地开发封板

状态：本地自动门禁完成（2026-08-30）。

实际执行结果：

- `dotnet restore ImageLabPlugin.slnx --locked-mode` 成功；
- Debug `-warnaserror` build：零警告、零错误；
- Debug tests：149/149 通过、零跳过；
- Release `-warnaserror` build：零警告、零错误；
- Release tests：149/149 通过、零跳过；
- `git diff --check` 无空白错误；
- 根 README、docs 索引、未来能力、公共领域边界和整套专用文档已同步。

未执行：Standalone 全量人工场景、真实 Host、ZIP、Windows CI、安装/卸载、目标设备性能和正式发布。Release 只表示本地配置回归，因此结论是“开发实现与本地自动门禁完成”，不是“已发布”。
