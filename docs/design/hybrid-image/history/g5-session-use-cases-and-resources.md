# G5：Session、用例与资源

完成四个窄用例：准备双输入、求解对齐、渲染代理、渲染完整尺寸。`HybridImageSession` 独占两张原图、两张代理、亮度、指纹、对齐和最后有效结果；无状态服务为 singleton。

每张输入只解码一次。完整尺寸直接消费首次解码的原始亮度，不放大代理。候选提交同时校验 Session 指纹、generation 与 recipe fingerprint；迟到候选被拒绝，最后有效结果保留。资源估算在 warp、卷积和 FFT 大数组前执行。
