# SVD Decomposition 报告 schema

## JSON：`image-lab.svd-report/1`

| 区域 | 主要字段 | 含义 |
| --- | --- | --- |
| 根 | `schema`、`numericProtocol`、`product`、`createdAtUtc` | schema 与 `one-sided-jacobi-v1` 协议身份 |
| `source` / `proxy` | 路径、宽高、最大边、`label=分析代理` | 明确原图与实际计算尺寸 |
| `recipe` | strategy、singleChannel、rank、fingerprint | 当前可导出的配方事实 |
| `channels[]` | channel、neutral、rows/columns、全部 singularValues | 每个矩阵的原始分解结果 |
| `energy` | totalEnergy、numericRank、tolerance、status、samples | 相对奇异值、分量能量和累计能量 |
| `diagnostics` | converged、sweeps、U/V orthogonality、relative reconstruction | 数值收敛与校验 |
| `rankResult` | 理论/直接 Frobenius、相对误差、retainedEnergy、raw min/max | double 矩阵结果 |
| `imageQuality` | MAE、RMSE、PSNR-Y/RGB、SSIM-Y、Alpha | 量化后代理图片结果 |
| `clipping` | clippedPixels、clippedComponents | RGB 图片投影裁切 |
| `component` | index、sigma、energyShare、raw min/max、displayScale | 当前有符号分量摘要，不保存预览字节 |
| `comparison` | commonRank、completionStatus、固定 cases | Y/RGB/YCbCr 有限比较 |
| `limitations[]` | 分析代理、非压缩器、重复奇异值说明 | 解释边界 |

精确重建的 PSNR 使用：

```json
{ "isExact": true, "psnrDb": null }
```

报告不输出 `Infinity`、`NaN` 或字符串伪装数字。时间是观察数据，不是 SLA。

## CSV

CSV 为 UTF-8 无 BOM、CRLF、稳定列顺序：

```text
recordType,strategy,channel,index,rank,sigma,relativeSigma,energyShare,cumulativeEnergy,retainedEnergy,frobeniusError,relativeError,psnrRgbDb,psnrRgbExact,ssimY,sweeps,converged,orthogonality,elapsedMs
```

记录顺序固定为每通道 `singular-value`、每通道 `rank-result`、可选固定顺序 `strategy-case`、每通道
`diagnostics`。非适用字段留空；精确 PSNR 的 dB 字段留空并以 `psnrRgbExact=true` 表达。数字使用 invariant culture，
文本按 CSV 双引号规则转义。CSV 是扁平实验表，不替代 JSON 中的完整限制和分层结构。
