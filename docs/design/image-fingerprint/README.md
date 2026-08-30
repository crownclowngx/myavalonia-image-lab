# 感知指纹

“感知指纹”把两张用户显式选择的图片统一归一化后，分别计算 aHash、dHash 和 pHash，以 64 位摘要、汉明距离、位相似度和版本化参考结论辅助人工复核。它不扫描目录，不比较文件字节，也不把结果描述为来源概率。

## 建议阅读顺序

- 第一次使用：[新手使用说明书](user-manual.md)
- 参数、交互和限制：[使用指南](guide.md)
- 公式与数值协议：[数学原理](mathematical-principles.md)
- JSON 字段：[报告 schema](report-schema.md)
- 自动证据：[测试与门禁](testing.md)
- 架构与实施边界：[实施计划](implementation.md)
- 实际实施过程：[G0–G8 历史记录](history/README.md)

当前结论是“开发实现与本地自动门禁完成”，不是“已发布”。Windows CI、ZIP、真实 Host、安装/卸载和发布封板均未执行。
