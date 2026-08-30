# 鲁棒性实验报告 schema 1

## 版本与兼容

JSON 和 CSV 的 `schemaVersion/schema_version` 当前为 `1`。配方 schema 与报告 schema 分开版本化；改变算子公式、PRNG、插值、舍入或字段语义时必须显式升级，不能用插件版本替代数据版本。

## JSON

顶层字段包括：`schemaVersion`、`recipeHash`、`completedAtUtc`、`isComplete`、`experimentSeed`、`randomAlgorithm`、`sourceName`、`payloadLength`、`payloadDigestId`、`cases` 和 `curves`。

每个案例包含稳定案例键、完成状态、最终诊断、分步观察、首次不可恢复步骤、失败后恢复、Attack-only/End-to-end 质量、16×16 局部网格和结构化算子错误。每个信道诊断包含 Physical/Voted BER 的错误 bit 数与比较 bit 数、RS 修复数、平均置信度和 P10 置信度。不可计算值为 `null` 或结构化 `unavailableReason`，不是 0。

PSNR 的正无穷由 JSON 序列化器写成合法字符串 `Infinity`；消费者不能把它解析成缺失或 0。

## CSV

CSV 是一案例一行的扁平复核表，固定包含 schema、配方哈希、Profile、扫描点、trial、完成/成功、失败原因、首次失败、失败后恢复、两类 Header/Data BER、分层 RS、置信度、两组 PSNR-Y 和算子错误。完整分步观察和局部网格只在 JSON 中提供。

CSV 使用 UTF-8、InvariantCulture 和 RFC 4180 风格引号转义；列内逗号、引号或换行会被正确引用。

## 隐私边界

两种格式都只允许载体文件名，不允许绝对路径；只记录 Payload 长度和截断 SHA-256 实验标识，不记录 Payload、恢复内容、密码、Mapping Key、salt、nonce、完整 Frame、图片像素或异常堆栈。输出先完整序列化，再经 `IAtomicFileWriter` 发布；失败不会留下半个正式目标文件。
