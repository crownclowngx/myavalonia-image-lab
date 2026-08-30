# G4：投影与重建

- `WaveletSubbandProjector` 提供对称、线性、对数三种有界灰度投影，不回写系数。
- `WaveletImageReconstructor` 解析原策略、裁回源尺寸、计算量化前 max/RMS 误差并复用通道回写。
- R/G/B/Y/Cb/Cr 沿用项目统一公式，Alpha 保留，裁切数显式报告。
- UI 只消费 `PixelImage` 投影和统计，不持有可写 double 数组。
