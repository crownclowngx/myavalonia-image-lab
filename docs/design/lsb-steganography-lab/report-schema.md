# LSB 实验报告 schema 1

JSON 使用 camelCase，并在 `channels` 中保存 R/G/B 实际存在的分项样本、位分布、卡方/p 和方向邻接。CSV 使用一行摘要，除聚合字段外固定包含 `r/g/b_cover_one_ratio` 与 `r/g/b_stego_one_ratio`；未选通道为空值。完整列以序列化器和 Golden 测试为准。

JSON 对应字段还包含 `seedMeaning`。`recipeId` 固定编码通道、bit 和位置版本。不可用或非有限值为 `null`/空 CSV 单元格，不能写裸 `Infinity` 或 `NaN`。

## 隐私门禁

报告不得包含 Payload 文本/字节、恢复内容、完整 Frame、绝对输入/输出路径、用户名、异常堆栈或图片像素。seed 可以记录，但必须标为公开实验参数而不是“密钥”。报告固定包含教学用途、统计限制、CRC 非认证以及与 DCT 鲁棒水印不同的说明。

序列化完成后才调用 `IAtomicFileWriter`；失败不会留下半个正式目标文件。schema 字段或语义变化必须显式升版本。
