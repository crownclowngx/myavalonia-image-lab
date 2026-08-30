# G1 Frame 与容量

- 实际修改：新增 `LsbPayload`、`LsbFrameCodec`、`LsbReadStatus`、严格 UTF-8 和 checked `LsbCapacityCalculator`；将 CRC 提升到 `Domain/Checksums` 并让水印回归共用。
- 自动证据：CRC `123456789`、Header 固定字段、位序、空/二进制 Payload、损坏 Header CRC、容量 159/160/168 槽等确定值测试。
- 设计取舍：Header CRC 验证前不读取长度驱动分配；Payload 释放清零。
- 遗留：没有外部协议兼容目标。
- 回滚：删除独立 Steganography Frame 类型；共享 CRC 仍由既有水印使用。
