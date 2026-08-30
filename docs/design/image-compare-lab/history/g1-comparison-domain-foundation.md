# G1 比较领域基础记录

状态：完成（2026-08-30）。

实际修改：新增双图验证、像素值对象和 `FullReferenceQualityAnalyzer`。质量统计以确定顺序的 Welford/Chan 在线
均值、二阶矩和协方差完成，额外内存 O(1)；同时累计 RGB/Alpha 误差与变化数。既有
`ImageQualityCalculator.Compare` 保持 Y-PSNR/全局 Y-SSIM 返回契约，但不再提取两张全尺寸亮度平面。

证据：完全一致、单像素、透明 RGB、Alpha-only、尺寸错误、取消和现有水印回归通过。浮点公式未放宽既有测试。
风险是数值尾差受扫描顺序影响，因此 V1 不并行分块。回滚时只有在 68 项既有测试继续通过时才保留该重构。
