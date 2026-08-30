# G4：重建与损失

复用 `FrequencyMaskApplier`、六通道转换、差异投影和全参考质量分析；新增窄的 `FrequencyGainSpectrumProjector`，
确保处理后频谱来自 `F×H`。诊断覆盖能量移除、修改 bin、峰抑制、raw 越界、颜色裁切、MAE、PSNR/SSIM 和虚部残差。
