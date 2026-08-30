# G2：Haar 数值核心

- `WaveletTransformBase` 统一扩展、X→Y 正变换、Y→X 逆变换、多层 LL 递归和裁剪。
- `HaarWaveletTransform` 使用正交归一化公式，行/列工作区不与输入重叠。
- 门禁覆盖 2×2 手算 Golden、方向、奇数/退化尺寸、1–6 层、确定性、重建和 Parseval。
- 取消在行、列和层边界检查；扩展预算在分配前阻断。
