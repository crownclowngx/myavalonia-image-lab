# 频域隐式水印

这项能力使用两个 Document：“水印写入”负责把文本、JSON 或文件作为 Payload 写入图片，“提取与验证”负责检测、恢复并验证 Payload。

## 建议阅读顺序

- 第一次使用：从 [新手使用说明书](user-manual.md) 开始。
- 需要理解每个选项和严格边界：阅读 [使用指南](guide.md)。
- 想知道为什么能隐藏、如何纠错：阅读 [数学原理](mathematical-principles.md)。
- 开发与维护：阅读 [实施计划](implementation.md)、[协议与安全边界](protocol.md) 和 [测试门禁](testing.md)。
- 追溯实施过程：查看 [G0–G9 历史记录](history/README.md)。

使用结论不能替代安全结论：“肉眼不明显”不等于不可检测，“完整性有效”也不等于能够证明作者身份。
