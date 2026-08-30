# 周期陷波配方 schema 1

## 根字段

配方 UTF-8 JSON 上限 1 MiB，根对象包含：

| 字段 | 语义 |
| --- | --- |
| `productId` | 固定 `myavalonia.plugin.image.lab.document.periodic-noise-removal` |
| `schemaVersion` | 固定 `1` |
| `algorithmVersion` | 固定 `periodic-notch-v1` |
| `channel` | `R/G/B/Y/Cb/Cr` |
| `transition` | `Ideal/Butterworth/Gaussian` |
| `radius` | `(0,0.25]` cycles/pixel |
| `strength` | `[0,1]` 振幅衰减 |
| `butterworthOrder` | Butterworth 为 1–12；其他过渡规范化为 1 |
| `notches` | 最多 32 个 canonical 中心 |
| `fingerprint` | 规范配方 SHA-256 前 16 个小写十六进制字符 |

每个 notch 只含 `fx`、`fy`、`origin` (`Manual/Automatic`) 和 `enabled`。不保存候选分数、源路径、图片、Bitmap、FFT、
逐 bin mask 或处理结果。来源影响完整配方指纹，但不影响数学指纹。

## 严格拒绝

导入拒绝空文件、超量输入、注释、尾逗号、重复属性、未知字段、未知枚举、非有限数、越界频率、超过 32 对中心、
不支持版本、错误产品 ID 和指纹不一致。导入后中心仍由 `NotchMaskFactory` 重新光栅化并通过共享共轭门禁。

候选摘要使用另一套只读 schema，只包含 Session 指纹、频谱尺寸、分数、突出度、风险和建议标记，不能当作配方导入。
