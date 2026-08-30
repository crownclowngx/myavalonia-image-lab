# G7：DWT 水印与 DCT/DWT 比较

- 新增独立 `dwt-pair-qim-v1`：Haar/Y、LH/HL、确定性系数对、差分 QIM、`DWT1` Frame 和 CRC-32。
- DCT Adapter 直接复用既有 Frame、RS 纠错、8×8 DCT Carrier 与 Golden，不改写协议。
- benchmark 在执行前检查共同 Payload 容量，复用既有 JPEG/缩放/噪声/模糊/亮度/对比度 Strategy。
- 报告保留载体各自容量、完整性、置信度、隐蔽性和外推限制，不输出普遍优劣结论。
