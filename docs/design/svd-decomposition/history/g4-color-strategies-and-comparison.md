# G4：颜色策略与比较

- 单通道继续复用 `ImageChannelConverter`；RGB/YCbCr 各在一次像素循环中组合并保留 Alpha。
- Cb/Cr 进入矩阵时减 128，组合时只加回一次；最终统一 AwayFromZero 舍入和裁切诊断。
- 比较固定 Y、RGB、YCbCr 三案例和共同 k，串行执行、取消保留部分结果且没有“最佳策略”字段。
