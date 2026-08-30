# Frequency Mask Editor／频谱遮罩编辑器

> 当前状态：V1 已完成本地开发实现与 Debug/Release 自动门禁；尚未执行发布门禁。

该能力登记为稳定 ID `myavalonia.plugin.image.lab.document.frequency-mask-editor` 的第十二个多实例
Persistable Document。用户可在中心化 FFT 频谱上使用衰减画笔、恢复橡皮、矩形和圆环编辑实数增益遮罩，
并使用频带锁定、反转、重置、撤销、重做和全局强度联动观察空间域重建。

共轭安全是领域不变量，不是可关闭的 UI 选项。数值核心、配方、历史、Session、文件边界和 View 分层实现；
Frequency Filter 与编辑器共享同一 `FrequencyMaskApplier`，没有复制第二套频谱乘法/IFFT 实现。

## 阅读顺序

- 第一次使用：[新手说明书](user-manual.md)
- 参数、状态、生命周期与导出：[使用与开发指南](guide.md)
- 共轭、强度、几何离散化与 IFFT：[数学原理](mathematical-principles.md)
- JSON 兼容边界：[配方 schema](recipe-schema.md)
- 自动证据与明确未证明事项：[测试与门禁](testing.md)
- 设计到实现：[实施计划](implementation.md)和[实施历史](history/README.md)

本轮未使用 AIFLOW，未登记 Workflow Action、Workbench Command 或 Tool，未新增 Windows CI，
也未执行真实 Host、ZIP、安装升级或发布门禁。
