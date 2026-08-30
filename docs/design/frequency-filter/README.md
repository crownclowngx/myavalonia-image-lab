# Frequency Filter／频域滤波

> 当前状态：V1 已完成本地开发实现与 Debug/Release 自动门禁；尚未执行发布门禁。

这项能力在现有全局 FFT 基础上提供可解释的理想、Butterworth 与 Gaussian 低通、高通、带通和带阻实验，
并联动展示幅度谱、滤波遮罩、逆变换结果、副作用诊断及等价空间域近似比较。

实现复用现有 FFT、统一频率坐标、六通道投影和空间卷积 raw-double 核心，并登记为稳定 ID
`myavalonia.plugin.image.lab.document.frequency-filter` 的第十一个 Persistable Document。算法是无状态 singleton，
图片、FFT Session、结果、取消和 Bitmap 均属于每个 Document Scope。

## 阅读顺序

- 第一次使用：阅读[新手说明书](user-manual.md)。
- 参数、缓存、状态与导出：阅读[使用与开发指南](guide.md)。
- 公式、过渡带、输出语义与空间近似：阅读[数学原理](mathematical-principles.md)。
- 自动证据和未证明事项：阅读[测试与门禁](testing.md)。
- 设计到实现：阅读[实施计划](implementation.md)和[实施历史](history/README.md)。

本轮没有使用 AIFLOW，没有登记 Workflow Action、Workbench Command 或 Tool，没有新增 Windows CI，也没有执行真实 Host、ZIP、安装升级或发布门禁。
