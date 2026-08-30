# 频域分析器

这项能力把图片转换成可观察的频率分布，并允许保留全部、低频、中频、高频或自定义环带后重建分析代理。

## 建议阅读顺序

- 第一次使用：从 [新手使用说明书](user-manual.md) 开始。
- 查阅通道、坐标、显示模式和导出语义：阅读 [使用指南](guide.md)。
- 理解 FFT、DCT、幅值、相位与频带：阅读 [数学原理](mathematical-principles.md)。
- 开发与维护：阅读 [实施计划](implementation.md) 和 [测试门禁](testing.md)。
- 追溯实施过程：查看 [G0–G7 历史记录](history/README.md)。

分析和导出的对象可能是缩小后的“分析代理”，不是原尺寸图片；界面状态栏会明确显示两者尺寸。

参数化 Ideal/Butterworth/Gaussian 滤波已在独立的[频域滤波](../frequency-filter/README.md)中实现；本 Document 继续只负责频谱观察和 0/1 频带重建，不反向调用另一个 Feature。
