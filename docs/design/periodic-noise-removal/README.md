# Periodic Noise Removal／周期噪声与陷波器

> 当前状态：V1 已完成本地开发实现与 Debug/Release 自动门禁；尚未执行发布门禁。

该能力以稳定 ID `myavalonia.plugin.image.lab.document.periodic-noise-removal` 登记为第十三个多实例
Persistable Document。它以 R/G/B/Y/Cb/Cr 的有界 FFT 为依据，检测或手动选择周期频率峰，生成共轭安全陷波草案，
并联动显示处理前后频谱、图像、符号/绝对差异和不可逆损失诊断。

候选不等于噪声结论。自动检测只更新候选，自动建议和手动点选只更新未确认草案；用户显式采用草案后还必须重新执行，
只有与当前 Session 和已采用配方指纹一致的非草案结果可以导出。

## 阅读顺序

- 第一次使用：[新手说明书](user-manual.md)
- 参数、状态机、架构与开发边界：[使用与设计指南](guide.md)
- 检测、共轭和三类 Notch 公式：[数学原理](mathematical-principles.md)
- 配方兼容与拒绝规则：[配方 schema](recipe-schema.md)
- 自动证据与明确未证明事项：[测试与门禁](testing.md)
- 产品冻结和完整实施要求：[实施记录](implementation.md)与[实施历史](history/README.md)

本轮未使用 AIFLOW，未登记 Workflow Action、Workbench Command 或 Tool，未新增 Windows CI，未执行真实 Host、ZIP、
安装升级或发布门禁。
