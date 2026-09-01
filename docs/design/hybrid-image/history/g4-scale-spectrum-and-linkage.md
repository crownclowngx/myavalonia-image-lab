# G4：尺度、频谱与联动

完成 raw double 的 1×/1/2×/1/4×/1/8×面积平均、奇数尺寸规则、有符号分量预览和有界频谱代理。`SpectrumProjector` 增加多频谱共享量程窄入口，一次扫描 A/B/低/高/raw，不复制完整 Complex 工作区。

结果 DTO 统一携带 Session、recipe、generation、四尺度、重影、频谱与诊断；任一阶段失败时不会形成可提交的部分结果。
