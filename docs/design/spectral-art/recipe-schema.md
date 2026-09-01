# Spectral Art Recipe schema 1

- `schema`: 必须为 `1`。
- `protocol`: 必须为 `spectral-art-fft-amplitude-v1`。
- `createdWithVersion`: 写入器版本信息，不参与数学路由。
- `patternWidth` / `patternHeight`: `1..512`。
- `samplingMode`: `BinaryNearest` 或 `GrayscaleArea`。
- `sourceKind`: `Text`、`LogoImage` 或 `QrImage`；只表示来源类型，不保存原文字和路径。
- `patternData`: Pattern 的 little-endian IEEE-754 double 位模式，经 Brotli 后 Base64；解压长度必须精确匹配。
- `patternFingerprint`: 解压重建后的指纹门禁。
- `region`: `left/top/right/bottom` 有限归一化频率。
- `fitMode`: `Contain` 或 `Stretch`。
- `strength`: 有限 `[0,8]`。
- `recipeFingerprint`: 完整数学事实指纹。

读取前后均限制 4 MiB，拒绝未知字段、重复字段、注释、尾随逗号、尾随数据、未知枚举、非有限数、错误 schema、解压长度和指纹不一致。snapshot 不嵌入本 recipe，也不会恢复时自动读图或 FFT。
