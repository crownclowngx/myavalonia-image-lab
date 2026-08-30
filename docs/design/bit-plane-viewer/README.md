# 位平面观察器

位平面观察器是 ImageLab 的第七个 Persistable Document。它把用户显式选择图片的 R、G、B、Alpha 或 Y
通道量化为 8 位样本，显示 bit 7（MSB）到 bit 0（LSB）、任意掩码组合、原通道重建、逐位统计与像素探针。

## 阅读顺序

- 第一次使用：[新手使用说明](user-manual.md)。
- 查阅交互、资源与边界：[使用指南](guide.md)。
- 理解位运算、Y 和熵：[数学原理](mathematical-principles.md)。
- 开发维护：[实施计划](implementation.md)与[自动测试门禁](testing.md)。
- 追溯设计与实际修改：[G0–G7 历史](history/README.md)。

当前状态是“开发实现与本地自动门禁完成”：2026-08-30 的 locked restore、Debug/Release warn-as-error build
以及两种配置的 191/191 测试均通过、零跳过。尚未执行有限人工交互清单、真实 Host、ZIP、Windows CI、
安装/卸载和正式发布门禁。

本工具只报告确定的位事实，不判断图片是否包含隐写、是否被篡改或是否来自某类传感器。
