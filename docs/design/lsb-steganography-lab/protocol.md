# ILSB Frame V1 协议

## Header

固定 20 字节；多字节整数均为 little-endian。

| Offset | Length | Field | V1 |
| ---: | ---: | --- | --- |
| 0 | 4 | Magic | ASCII `ILSB` |
| 4 | 1 | Version | `1` |
| 5 | 1 | PayloadKind | `1=UTF-8`、`2=Binary` |
| 6 | 2 | Flags | `0` |
| 8 | 4 | PayloadLength | `0..65,536` |
| 12 | 4 | PayloadCrc32 | 原始 Payload 的 IEEE CRC-32 |
| 16 | 4 | HeaderCrc32 | Offset `0..15` 的 IEEE CRC-32 |

完整 Frame 是 `Header || Payload`。CRC 使用多项式 `0xEDB88320`、初值/终值异或全 1。CRC 不是签名、MAC 或身份认证。

## 位序与槽位

Frame 按字节顺序，每字节从 bit 7 到 bit 0 写入。目标字节 `v`、消息 bit `m`、位平面 `b`：

```text
mask = 1 << b
result = (v & ~mask) | (m << b)
```

图片按 y、x 行优先；只有 Alpha=255。RGB 槽位固定为 `pixel0.R, pixel0.G, pixel0.B, pixel1.R...`。

顺序版本为 `sequential-v1`。伪随机版本为 `splitmix64-sparse-fisher-yates-v1`：SplitMix64、拒绝采样消除取模偏差、稀疏 partial Fisher-Yates 无放回选择。seed 是公开复现参数。修改 PRNG 常量、拒绝规则或交换规则必须新增版本，不能静默漂移。

通道、bit、位置类型和 seed 不在 Header 内；提取前必须显式给出。错误参数只产生结构化失败，不自动猜测、扫描或爆破。
