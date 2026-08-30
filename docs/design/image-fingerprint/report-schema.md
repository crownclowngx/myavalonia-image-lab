# 感知指纹报告 schema 1

顶层固定字段包括 `schemaVersion`、`completedAtUtc`、`referenceName`、`candidateName`、两图尺寸与 Alpha、`normalizationId`、`decisionPolicyId`、`overview`、`disclaimer`、`algorithms` 和可空 `stability`。

每个算法包含稳定 `algorithmId`、A/B 规范摘要、`distance`、`bitSimilarityPercent`、`referenceThreshold`、`decision`、耗时和限制。稳定性点包含请求强度、实际输出尺寸、可空 JPEG 编码长度、错误和逐算法摘要/距离。

报告默认只保留文件名，不写绝对路径、原图或预览像素、PNG/JPEG 字节、EXIF、文件散列、用户名、异常堆栈或“同源概率”。UTF-8 内容先完整生成，再通过 `IAtomicFileWriter` 发布；失败或取消不留下半个正式目标。

Robustness Lab 的既有报告通过可选 `fingerprintObservations` 兼容扩展；CSV 追加 `ahash_distance`、`dhash_distance`、`phash_distance`。未启用时字段为空，不改变既有实验语义。
