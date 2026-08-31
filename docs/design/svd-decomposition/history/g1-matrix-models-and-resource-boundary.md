# G1：矩阵模型与资源边界

- 抽出 `ImageAreaResampler`，旧 `ImageAnalysisProxyProjector` 保留 512/1024/2048 白名单和逐字节语义。
- 建立 `DenseMatrix`、`SvdFactors`、诊断、能量、Rank、分量、策略和资源估算等不可变模型。
- 所有外部数组防御复制；行列乘法、样本数和峰值估算使用 checked；非有限值在领域边界拒绝。
