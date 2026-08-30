# G1 两个 Document 与生命周期外壳记录

状态：完成（2026-08-30）

模板 `MainDocument/MainView` 和模板命令已删除。Module 现在只登记：

- `myavalonia.plugin.image.lab.document.watermark.embed` → 水印写入；
- `myavalonia.plugin.image.lab.document.watermark.inspect` → 提取与验证。

两个类型都是 `IPersistablePluginDocument`，由 Host 每个 Document Scope 创建。Standalone 不复制业务对象图，而是调用真实 `ImageLabPluginModule.Configure`，分别创建两个 Scope 并在窗口关闭时释放。

门禁证据：组合根测试验证两个 Persistable Document、零普通 Document、零 Tool；两个 Scope 的状态隔离；Headless 环境独立加载两个真实 View。模板示例身份不再存在。

偏差：Standalone 当前固定展示两个页签，不承担 Host manifest、Dock 或保存协调器模拟。回滚时可以隐藏贡献，但不能复用已删除的模板 ID 作为产品身份。
