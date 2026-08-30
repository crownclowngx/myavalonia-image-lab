# G5 Persistable Document 与生命周期

状态：完成（2026-08-30）。

实际登记稳定 ID `myavalonia.plugin.image.lab.document.bit-plane-viewer`，Module 当前恰好七个 Persistable Document、
零普通 Document、零 Tool。Document 管理路径、通道、焦点位、掩码、预设、探针、取消、两个 generation 和 Bitmap。

设计思路：每实例 scoped 所有权避免共享“当前图片”；无状态算法和用例 singleton。快照 schema 1 只含轻量参数，恢复
不读取文件。Bitmap 替换先接管新对象再 Dispose 旧对象，迟到对象直接释放。

证据：Module 顺序/数量、两个 Scope 隔离、快照不含像素/统计、未知 schema 回退、恢复零解码、真实 Document 闭环均通过。
路径、通道、掩码等持久状态推进 Revision；纯运行状态不保存。专门替身故意忽略取消并让旧图片最后返回，测试证明旧
Session 被释放且不能覆盖新图片的 2×1 结果。
