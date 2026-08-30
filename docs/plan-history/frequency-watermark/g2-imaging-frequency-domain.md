# G2 Imaging/Frequency Domain 记录

状态：完成（2026-08-30）

建立了 UI/文件系统无关的 `ImageSize`、`PixelImage`、`LumaPlane`、`ColorSpaceConverter`、`ImageQualityCalculator`、`ImageDifferenceProjector`、`Dct8x8Transform` 与 `FrequencySpectrumProjector`。

设计遵循最小公共领域：RGBA 与亮度拥有明确缓冲区；DCT 只接受 64 个数值；频谱投影输出普通 `PixelImage`，不知道 Avalonia `Bitmap`。Watermarking 的 Payload、Profile 和 QIM 留在自己的领域，没有进入公共 Imaging。

门禁证据：非法尺寸与 16M 像素上限、DCT/IDCT `1e-9` 往返、差异/频谱尺寸、1024 最大边预览、Alpha、透明块、非 8 倍数边缘和源对象不变均有自动测试。

偏差：V1 使用清晰所有权的连续数组，没有引入池化。16M 上限与有界分析预览控制最坏规模；发布设备峰值内存观测延期。回滚可移除 Watermarking，但公共图像模型可供后续频谱工具继续复用。
