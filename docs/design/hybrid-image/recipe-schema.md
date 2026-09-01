# Hybrid Image Recipe Schema 1

协议名：`hybrid-image-v1`；最大 JSON 大小：256 KiB；编码：UTF-8。

## 必需字段

| 字段 | 规则 |
| --- | --- |
| `schema` | 必须为 `1` |
| `protocol` | 必须为 `hybrid-image-v1` |
| `roleA` / `roleB` | 固定 `A-low-reference` / `B-high-aligned` |
| `fingerprintA/B` | 各 24 位十六进制内容指纹，不含路径 |
| `points` | 2–8 个完整点对；Id 唯一；坐标为 `[0,1]` 有限数 |
| `crop` | `[0,1]` 内非空、左闭右开的 A 归一化矩形 |
| `lowSigmaPixels/highSigmaPixels` | `[0.8,32]` 有限数 |
| `lowGain/highGain` | `[0,2]` 有限数 |
| 固定算法字段 | `gray-white-background`、`gaussian-3sigma`、`reflect101`、`bilinear-pixel-center`、`to-even-clamp` |
| `scaleDivisors` | 必须精确为 `[1,2,4,8]` |
| `recipeFingerprint` | 规范化领域配方 SHA-256 前 24 位 |

读取器拒绝未知/重复/缺失字段、尾随内容、注释、未知协议、未知固定事实、非有限数、重复 Id、越界点、超长文本和指纹不一致。导入不读取旧路径；用户重新选择 A/B 后必须核对内容指纹。
