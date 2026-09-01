# Spectral Art／频谱艺术

Spectral Art 是 ImageLab 第十七项产品能力、第十八个多实例 Persistable Document。它把文字、Logo 或已有二维码图片规范化为有界 Pattern，在 Y 通道 FFT 的合法半平面写入幅度，并同步写入严格共轭副本。它展示的是“频谱中可见的图案”，不是 Payload 水印、二维码生成器、扫码器或隐写安全工具。

## 阅读入口

- [新手说明书](user-manual.md)：从选择载体到导出结果。
- [开发与高级使用指南](guide.md)：参数、状态、资源、复用边界和失败语义。
- [数学原理](mathematical-principles.md)：坐标、共轭、径向稳健尺度、幅度公式和 IFFT。
- [测试证据](testing.md)：自动门禁及未执行项。
- [Recipe schema](recipe-schema.md) 与 [Report schema](report-schema.md)：独立文件协议。
- [实施计划与落地记录](implementation.md)、[G0–G9 历史](history/README.md)。

## 复用边界

| 分类 | 内容 |
| --- | --- |
| 直接复用 | `PixelImage`、`ImageSize`、图片预算/编解码、FFT、频率坐标、Y 通道、质量、差异、文本读取、原子写入 |
| 共享提取 | `RadialLogPowerBaseline`、`FrequencyInverseTransformer`、`SpectrumDisplayScale`、`UniformImageCoordinateMapper`、目标尺寸面积缩放 |
| 专用实现 | Pattern、区域门禁与映射、幅度公式、诊断、Session、recipe/report、Document、View |
| 禁止复用 | DCT-QIM/LSB Payload 协议、Frequency Mask 增益语义、Periodic Notch recipe、其他产品的 Session/serializer/Document/View |

当前只完成本地开发封板。未执行 Windows CI、真实 Host、ZIP、安装、签名或发布门禁。
