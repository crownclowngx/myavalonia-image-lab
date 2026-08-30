# ImageLab 频域水印 V1 协议

> 状态：首次发布前开发基线。只有完成延期的真实 Host 与发布封板后，V1 才形成外部长期兼容承诺。

## 处理顺序

写入路径固定为：原始 Payload → 可选 Brotli（仅实际变小时）→ 可选 AES-256-GCM → RS(255,223) → bit 展开 → 冗余映射 → Y 通道 8×8 DCT-QIM。读取严格逆序。CRC 只检查控制头随机损坏；SHA-256 摘要检查恢复结果；只有 AES-GCM 提供密码学认证。

## Control Header

未纠错 Header 固定 80 字节，全部整数使用 little-endian；经一块缩短 RS 编码后为 112 字节。

| Offset | 长度 | 字段 |
| ---: | ---: | --- |
| 0 | 4 | ASCII `ILW1` |
| 4 | 1 | Protocol Version = 1 |
| 5 | 1 | Header Length = 80 |
| 6 | 1 | Profile：1 Stealth、2 Balanced、3 Robust |
| 7 | 1 | Flags：bit0 Brotli、bit1 AES-GCM、bit2 保留给签名 |
| 8 | 1 | Payload：0 Binary、1 Text、2 Json |
| 9 | 1 | KDF：加密时 1 = PBKDF2-SHA256 |
| 10 | 1 | 每 RS 块校验符号数 = 32 |
| 11 | 1 | Data 冗余副本数 |
| 12 | 4 | Protected Length |
| 16 | 4 | RS Encoded Length |
| 20 | 4 | Original Length |
| 24 | 16 | Salt；未加密时全零 |
| 40 | 12 | AES-GCM Nonce；未加密时全零 |
| 52 | 16 | Mapping Seed |
| 68 | 8 | 原始 Payload SHA-256 前缀 |
| 76 | 4 | 前 76 字节 IEEE CRC-32 |

未知版本、未知 Profile、未知 Payload 类型、未知 Flag、Signed Flag、非法长度关系或 CRC 错误都必须拒绝，不能猜测解析。

## 密码与映射

- KDF：PBKDF2-HMAC-SHA256，600,000 次，16 字节随机 Salt，输出 64 字节 master。
- 使用 HMAC-SHA256 上下文字符串分别派生 Encryption Key 与 Mapping Key，避免同一密钥跨用途复用。
- AES-GCM 使用 256 位 Encryption Key、12 字节随机 Nonce、16 字节 Tag；Header 前 76 字节作为 AAD。
- Mapping Key 与 16 字节随机 Mapping Seed 再经 HMAC 合成位置密钥。
- 未加密 Frame 仍使用随机 Mapping Seed 和公开上下文 SHA-256 生成确定性位置；这不是加密。
- 密码为空表示不加密；密码最长 1024 个字符。错误密码与图片篡改统一报告认证失败，避免错误地泄露原因。

## 载体映射

Control Channel 使用固定 QIM step 40、三副本，共 2,688 个槽位，并均匀分散在所有可用槽位。Data Channel 排除控制槽后使用 Mapping Key 的确定性 Fisher-Yates 排列。

每个合格 8×8 块提供 `(2,2)`、`(3,1)`、`(1,3)`、`(3,2)` 四个中频系数。Stealth/Balanced/Robust 的 Data QIM step 与冗余分别是 `20/1`、`28/2`、`36/3`。V1 只修改 Y，不使用 Cb/Cr。

## 资源与安全上限

- 原始 Payload：16 MiB。
- 编码图片文件：64 MiB，在整体读取前检查路径文件长度。
- 解码领域图片：16,000,000 像素，在领域大缓冲区分配前检查。
- RS 数据编码上限：20 MiB。
- Brotli 解压必须精确等于 Header 的 Original Length，不能追加或少字节。

## Golden vector

测试输入为 UTF-8 `golden-vector-v1`、Text、Balanced、无密码、计数随机源。SHA-256 为：

- Encoded Header：`D388DF37888F1C2CB1478147E1FD52CADBF891A3F5A0BE2900A977B84208CE11`
- Encoded Data：`65AF15A952F66721276D79BD9A913C81D65CD87AFAD97FD79297AD8AD64B032E`
- Mapping Key：`D9CA6FDDADA7AE75E4C107B6B8B6CF5FBAF9CF69A1CA9EF74C704525BE3A93F8`

任何有意线格式变更都必须增加新版本或明确迁移方案，不能静默更新该向量。

## 参数依据

PBKDF2-HMAC-SHA256 的 600,000 次开发基线参考 [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)。实现使用 .NET 10 的一次性 [`Rfc2898DeriveBytes.Pbkdf2`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0) API，不使用已标记过时的构造函数。正式发布时仍应在目标设备上复核交互延迟与当时的安全建议。
