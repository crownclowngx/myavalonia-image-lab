# G5 应用 Session 与导出

- 日期：2026-08-30；状态：完成。
- 六个窄接口分别准备 Session、代理预览、探针、核响应、完整尺寸和导出。
- Session 一次解码后持有完整图与代理；应用层只返回 PixelImage/领域 DTO，不返回 Avalonia Bitmap。
- 代理和完整尺寸分别计算、分别计时；完整结果绑定 SHA-256 recipe fingerprint。
- 导出前再次核对当前指纹，固定 PNG，经既有 `IAtomicFileWriter` 发布。
- 测试覆盖一次解码、会话释放、代理/完整分离、过期拒绝和 PNG 端口。
