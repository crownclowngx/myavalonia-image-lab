# G4：IFFT、重建与诊断

从 FrequencyMaskApplier 提取无业务语义的 FrequencyInverseTransformer，统一原地 IFFT、有限值、虚部门禁和 crop。重建复用 Y 回写；质量、空间差异和频率差异沿用现有服务。SpectrumDisplayScale 让原/结果频谱共享上限，旧 API 保持委托行为。
