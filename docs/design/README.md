# ImageLab 设计文档

本目录按“能力”组织文档。每项能力都把实施计划、测试证据、开发者指南、新手使用说明、数学原理和历史记录放在同一目录，避免同一主题分散在多处。

| 能力 | 面向普通用户 | 面向开发者 |
| --- | --- | --- |
| [频域隐式水印](frequency-watermark/README.md) | 写入、检测和提取隐藏信息 | DCT-QIM、协议、安全与纠错 |
| [频域分析器](spectrum-inspector/README.md) | 观察频谱并按频带重建 | FFT/DCT、通道、遮罩和资源边界 |
| [图像比较实验室](image-compare-lab/README.md) | 比较两张同尺寸图片 | 指标、直方图、差异投影和报告 |
| [鲁棒性实验室](robustness-lab/README.md) | 测试水印经历扰动后的恢复能力 | 扰动链、扫描、BER、复现和报告 |
| [感知指纹（待实施）](image-fingerprint/implementation.md) | 判断两张显式图片的感知指纹是否接近 | aHash、dHash、pHash、汉明距离、稳定性与门禁计划 |

跨能力的架构、领域边界、工作台命令、工作流和发布资料见 [公共设计资料](shared/README.md)。

## 每个能力目录的约定

- `README.md`：该能力的入口和建议阅读顺序。
- `user-manual.md`：尽量不要求技术背景的新手说明书。
- `guide.md`：准确描述参数、状态、限制和开发边界的使用指南。
- `mathematical-principles.md`：实现所依赖的数学概念、公式和解释。
- `implementation.md`：V1 实施计划、阶段和验收条件。
- `testing.md`：自动测试、本地命令、已证明与未证明的结论。
- `history/`：各阶段实际实施记录，不作为当前入口文档。

“感知指纹”当前只有实施计划，尚未提供可用 Document；完成各实施包后再按上述约定补齐实际使用、数学、测试与历史文档。
