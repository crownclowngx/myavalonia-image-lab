# ImageLab 设计文档

本目录按“能力”组织文档。每项能力都把实施计划、测试证据、开发者指南、新手使用说明、数学原理和历史记录放在同一目录，避免同一主题分散在多处。

| 能力 | 面向普通用户 | 面向开发者 |
| --- | --- | --- |
| [频域隐式水印](frequency-watermark/README.md) | 写入、检测和提取隐藏信息 | DCT-QIM、协议、安全与纠错 |
| [频域分析器](spectrum-inspector/README.md) | 观察频谱并按频带重建 | FFT/DCT、通道、遮罩和资源边界 |
| [图像比较实验室](image-compare-lab/README.md) | 比较两张同尺寸图片 | 指标、直方图、差异投影和报告 |
| [鲁棒性实验室](robustness-lab/README.md) | 测试水印经历扰动后的恢复能力 | 扰动链、扫描、BER、复现和报告 |
| [感知指纹](image-fingerprint/README.md) | 判断两张显式图片的感知指纹是否接近 | aHash、dHash、pHash、汉明距离、稳定性与报告 |
| [位平面观察器](bit-plane-viewer/README.md) | 观察 R/G/B/Alpha/Y 的各个位及掩码重建 | 8 位掩码、位统计、探针、五通道重建与 PNG 导出 |
| [LSB 隐写与统计实验](lsb-steganography-lab/README.md) | 在像素低位写入有限载荷并观察脆弱性 | ILSB Frame、槽位、统计、BER、报告和资源边界 |
| [卷积核实验台](convolution-playground/README.md) | 编辑卷积核并联动观察空间结果与频率响应 | 真卷积、边界、归一化、双核梯度、解释与门禁 |
| [小波实验室](wavelet-lab/README.md) | 观察多尺度子带、重建、阈值去噪和载体差异 | Haar/CDF 5/3、packed 金字塔、有限扫描、DCT/DWT Adapter |
| [频域滤波](frequency-filter/README.md) | 低通、高通、带通和带阻实验 | Ideal/Butterworth/Gaussian、IFFT、副作用与空间近似 |
| [频谱遮罩编辑器](frequency-mask-editor/README.md) | 在频谱上绘制、锁定、撤销并观察重建 | 共轭安全增益、配方、历史、IFFT 与严格 JSON |
| [周期噪声与陷波器](periodic-noise-removal/README.md) | 检测或手选周期频率峰并复核陷波损失 | 稳健峰检测、共轭 Notch、草案采用、误判防护与损失诊断 |

跨能力的架构、领域边界、工作台命令、工作流和发布资料见 [公共设计资料](shared/README.md)。

## 最近完成能力

| 能力 | 当前状态 | 计划入口 |
| --- | --- | --- |
| Wavelet Lab／小波实验室 | V1 本地开发封板 | [专用实现与证据入口](wavelet-lab/README.md) |
| Frequency Filter／频域滤波 | V1 本地开发封板 | [专用实现与证据入口](frequency-filter/README.md) |
| Frequency Mask Editor／频谱遮罩编辑器 | V1 本地开发封板 | [专用实现与证据入口](frequency-mask-editor/README.md) |
| Periodic Noise Removal／周期噪声与陷波器 | V1 本地开发封板 | [专用实现与证据入口](periodic-noise-removal/README.md) |

## 每个能力目录的约定

- `README.md`：该能力的入口和建议阅读顺序。
- `user-manual.md`：尽量不要求技术背景的新手说明书。
- `guide.md`：准确描述参数、状态、限制和开发边界的使用指南。
- `mathematical-principles.md`：实现所依赖的数学概念、公式和解释。
- `implementation.md`：V1 实施计划、阶段和验收条件。
- `testing.md`：自动测试、本地命令、已证明与未证明的结论。
- `history/`：各阶段实际实施记录，不作为当前入口文档。

十二项产品能力均已完成开发实现与本地自动门禁，Module 当前登记十三个 Persistable Document。上述状态不等于已通过真实 Host、ZIP、Windows CI 或发布验收。
