# Magnitude/Phase Swap Recipe Schema 1

已实现协议名：`magnitude-phase-swap-v1`；最大 JSON：256 KiB；UTF-8；未知、重复或缺失字段全部拒绝。

实现 DTO 使用 camelCase 枚举名（例如 `SourceA`、`LinearAtoB`、`ShortestArcAtoB`）和数值 `0` 表示非插值模式未使用的 amount；固定算法事实分别写入 `canvas`、`sampling`、`conjugate`、`phaseInterpolation` 与 `projection`。这组字段是 schema 1 的真实线格式，早期 G0 表格中的短横线示意名不再作为可导入 JSON。

## 必需字段

| 字段 | 规则 |
| --- | --- |
| `schema` | 必须为 `1` |
| `protocol` | 必须为 `magnitude-phase-swap-v1` |
| `fingerprintA/B` | 各 24 位小写十六进制规范画布内容指纹，不含路径 |
| `canvasSize` | `256`、`512` 或 `1024` |
| `createdWithVersion` | 当前写为 `1.0.0` |
| `magnitudeMode` | `SourceA`、`SourceB`、`LinearAtoB` 或 `UnitNonZero` |
| `magnitudeAmount` | 仅 `LinearAtoB` 使用 `[0,1]`；其他模式必须为数值 `0` |
| `phaseMode` | `SourceA`、`SourceB`、`ShortestArcAtoB` 或 `Zero` |
| `phaseAmount` | 仅 `ShortestArcAtoB` 使用 `[0,1]`；其他模式必须为数值 `0` |
| `projectionKind` | `PhysicalClamp` 或 `SignedScientific`，必须与模式相容 |
| `canvas` | 固定 `white-srgb-bt601-fit-contain` |
| `sampling` | 固定 `area-down-bilinear-pixel-center-up` |
| `conjugate` | 固定 `unshifted-conjugate-representative` |
| `phaseInterpolation` | 固定 `shortest-arc-positive-pi-tie` |
| `projection` | 固定 `to-even-clamp-or-p995-signed` |
| `recipeFingerprint` | 规范化领域事实 SHA-256 前 24 位小写十六进制 |

## 合法组合

- A 幅度 + B 相位：`SourceA` + `SourceB`；
- B 幅度 + A 相位：`SourceB` + `SourceA`；
- A/B 幅度-only：对应 `SourceA/SourceB` + `Zero`；
- A/B 相位-only：`UnitNonZero` + 对应 source，且使用 `SignedScientific`；
- 幅度插值：`LinearAtoB` + 固定 source phase；
- 相位插值：固定 source magnitude + `ShortestArcAtoB`。

V1 读取器不接受同时进行幅度插值和相位插值的自由二维组合，避免界面与解释范围无界扩张。所有 double 必须有限并使用 invariant culture。导入 Recipe 不读取文件；用户重新选择 A/B 并准备画布后，只有两个内容指纹都匹配才允许执行。
