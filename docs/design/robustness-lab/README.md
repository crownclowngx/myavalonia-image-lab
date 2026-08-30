# 鲁棒性实验室

这项能力对水印图片施加一条明确的扰动链，用单参数扫描和多次试验观察水印何时开始无法恢复，以及失败最早出现在哪一步。

## 建议阅读顺序

- 第一次使用：从 [新手使用说明书](user-manual.md) 开始。
- 查阅配方、参数 ID、指标与隐私边界：阅读 [使用指南](guide.md)。
- 理解扫描、概率、BER 和图像质量指标：阅读 [数学原理](mathematical-principles.md)。
- 开发与维护：阅读 [实施计划](implementation.md)、[报告格式](report-schema.md) 和 [测试门禁](testing.md)。
- 追溯实施过程：查看 [G0–G9 历史记录](history/README.md)。

实验结果只对当前图片、Payload、Profile、扰动配方、种子和实现版本成立，不应外推为“任何情况下都不可破坏”。
