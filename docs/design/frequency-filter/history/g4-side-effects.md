# G4：副作用诊断

- 新增有符号/绝对差异、raw 越界与有限位置摘要、中心差分梯度能量和横纵剖面。
- 质量指标直接复用 `FullReferenceQualityAnalyzer`，不把 PSNR/SSIM 称作质量提升。
- 诊断只读输入，不修改图片或选择滤波器。
