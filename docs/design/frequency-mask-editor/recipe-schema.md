# 频谱遮罩配方 schema 1

配方为 UTF-8 JSON，最大 1 MiB。导入执行严格 DTO → Domain 构造 → 规范指纹校验，不读取 CLR 类型名，未知字段、schema 或 kind 均拒绝。

## 顶层字段

| 字段 | 规则 |
| --- | --- |
| `schema` | 必须为 `1` |
| `productId` | 必须为 `myavalonia.plugin.image.lab.document.frequency-mask-editor` |
| `createdWithVersion` | 创建版本提示，不参与运行时类型发现 |
| `coordinateProtocol` | 必须为 `centered-display-normalized-v1` |
| `baseline` | V1 必须为 `all-pass` |
| `strength` | 有限 `[0,1]` |
| `originalPaddedWidth/Height` | 可选、成对、`1..2048`，仅作复现提示 |
| `operations` | 最多 128 条、总计最多 32768 个 stroke 点 |
| `fingerprint` | 规范化 Recipe 的 SHA-256 前 16 个小写十六进制字符 |

## 稳定 kind

- `brush`：points、radius、targetGain、opacity、可选 bandLock。
- `erase`：points、radius、opacity、可选 bandLock，目标固定为 1。
- `rectangle`：start、end、targetGain、opacity、可选 bandLock。
- `ring`：start 为中心、innerRadius、outerRadius、targetGain、opacity、可选 bandLock。
- `invertAll`：反转整张遮罩。
- `resetAllPass`：恢复全通，仍是一条可撤销操作。

坐标、半径、增益、opacity 和 bandLock 均要求有限且满足领域范围。外部文件不保存或导入 gain 数组；执行时必须重新光栅化，
从而重新证明共轭安全。
