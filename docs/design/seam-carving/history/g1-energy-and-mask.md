# G1：亮度、Sobel 与区域偏置

完成 `SeamLumaProjector`、`SobelEnergyCalculator`、能量摘要、线性/对数显示投影和三态蒙版。
Golden 覆盖 Alpha 白底、隐藏 RGB、原色、常量/阶跃、1×N/N×1、clamp 边界和固定偏置。
显示投影只读领域能量；Domain 不依赖 Avalonia。
