# G6 UI 与导出记录

状态：完成自动化开发门禁（2026-08-30）。

新增编译绑定 View、原图预览、频谱、遮罩、重建、参数/检查侧栏、256-bin 轻量曲线控件和 Uniform 黑边排除映射。
Module 以稳定 ID 登记第三个 Persistable Document；Standalone 增加第三个真实 Scope 和页签。PNG 通过
`IImageCodec` 编码和 `IAtomicFileWriter` 发布，名称与状态明确代理尺寸。

证据：三个 View Headless 加载，正式 Avalonia PNG 编解码器完成 Document 分析—四预览—重建闭环；组合根、
Scope 和 68 项测试通过。真实窗口全量人工点击尚未执行，不能据此声称真实 Host 已验收。
