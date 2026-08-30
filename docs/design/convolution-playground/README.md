# 卷积核实验台

卷积核实验台是 ImageLab 的第九个多实例 Persistable Document。它面向“看懂一个空间核实际做了什么”，而不是提供不可解释的一键美图滤镜集合。

## 已实现闭环

1. 显式载入一张 PNG/JPEG，并建立最大边 512、1024 或 2048 的抗混叠分析代理。
2. 选择预设，或粘贴 3×3 至 31×31 的奇数方形自定义核。
3. 明确选择 Constant、Replicate、Reflect-101 或 Wrap 边界，以及四种归一化、偏置和 RGB/R/G/B/Y/Cb/Cr 通道。
4. 在代理上执行可取消的真二维离散卷积，同时生成绝对差异和 256×256 核频率响应。
5. 对某一代理像素复算核贡献；双核 Magnitude 分别保留 Gx/Gy 贡献。
6. 显式执行完整尺寸卷积。结果绑定配方指纹，参数变化后旧结果立即禁止导出。
7. 只把当前完整尺寸结果原子导出为 PNG。

## 阅读顺序

- 新手先读[新手说明书](user-manual.md)。
- 参数、错误与状态见[使用与开发指南](guide.md)。
- 预设公式见[核目录](kernel-catalog.md)。
- 数学约定见[数学原理](mathematical-principles.md)。
- 自动证据与未覆盖边界见[测试与门禁](testing.md)。
- [实施计划](implementation.md)和[历史记录](history/README.md)用于追踪设计到实现的变化。

## 明确边界

- V1 固定中心锚点、真卷积、奇数方核和 CPU double 路径；不支持偶数核、移动锚点、GPU、HDR 或任意滤镜链。
- Magnitude 是双核非线性组合，没有虚构的等价单核。
- 频率响应只描述归一化后的线性核，不包含边界、偏置、裁切和 YCbCr 回写。
- 分析代理不是完整尺寸结果；JPEG 不作为输出格式。
- 本轮没有 AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP 或发布验收。
