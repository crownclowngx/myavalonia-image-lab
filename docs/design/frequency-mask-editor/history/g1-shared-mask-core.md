# G1：共享实数遮罩核心

- 新增不可变 `FrequencyGainMask`，构造时验证有限、范围、尺寸和共轭对称。
- 新增 `FrequencyMaskApplier` 及 padded 入口，统一频谱复制、增益乘法、IFFT 和虚部门禁。
- Frequency Filter 保留稳定返回类型并委托共享核心；既有回归保持通过。
