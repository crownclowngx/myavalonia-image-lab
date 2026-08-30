# G3 预设与组合算子

- 日期：2026-08-30；状态：完成。
- 低通、运动、锐化、Unsharp、High Boost、三类梯度、两类 Laplacian 和方向浮雕都由同一 Factory 生成。
- `GradientCombiner` 只组合除数后的 Gx/Gy，Magnitude 完成后才应用一次偏置。
- 双核探针分别保留 X/Y 两套贡献和累加值，不制造等价单核。
- Robustness 的 Gaussian/Unsharp 未被迁移，避免无必要改变既有扰动协议；既有测试全量回归证明兼容。
