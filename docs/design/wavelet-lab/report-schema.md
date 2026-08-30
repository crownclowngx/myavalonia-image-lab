# Wavelet Lab 报告 schema

## JSON：`wavelet-experiment-v1`

| 字段 | 含义 |
| --- | --- |
| `schema` | 固定 `wavelet-experiment-v1` |
| `sourcePath` | 兼容字段名；V1 只写源文件名，不写绝对目录 |
| `recipeFingerprint` | 当前不可变配方 SHA-256 前 16 个十六进制字符 |
| `transform` / `channel` | Haar/Cdf53 与六通道稳定名称 |
| `levels` / `threshold` | 实际分解层数与 double 阈值 |
| `scanCases[]` | 已完成的有限扫描案例；取消后只包含完成项 |
| `watermarkBenchmark` | 可选 DCT/DWT 共同 Payload 报告 |
| `limitations[]` | 无参考结论和水印外推限制 |
| `createdAtUtc` | UTC 生成时间 |

`scanCases` 保存 `sequence`、`levels`、`threshold`、非零系数统计、`residualRms`、可选 PSNR/SSIM 和耗时。
非有限浮点按 `System.Text.Json` 命名浮点字符串表达，不能生成非法 JSON。

`watermarkBenchmark` 的 schema 为 `wavelet-watermark-benchmark-v1`，包含 Payload 长度、两个载体各自最大容量、
每个 `caseId + carrierId` 的完整性、置信度、纠错前物理信道 `rawBitErrorRate`、隐蔽性指标和限制。载体 ID 固定为
`dct-frequency-qim-v1` 与 `dwt-pair-qim-v1`。

## CSV

CSV 为案例表，UTF-8 无 BOM，首行为：

```text
section,sequence,carrier,case,levels,threshold,retainedRatio,residualRms,psnrLuma,ssimLuma,integrity,confidence,rawBer
```

`section=scan` 使用扫描字段；`section=watermark` 使用载体、案例、完整性和置信度。空值保持空字段，不伪造成 0；
逗号、双引号和换行按 RFC 4180 风格双引号转义。CSV 是扁平表，不替代 JSON 中的限制和完整结构。
