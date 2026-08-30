# G3 Frame 与安全记录

状态：完成（2026-08-30）

V1 Frame 已冻结为 80 字节 Header、RS 后 112 字节 Control Header 和分块 RS Data。压缩只在 Brotli 至少节省 8 字节时启用。加密使用 PBKDF2-SHA256 600,000 次、HMAC 上下文密钥分离、AES-256-GCM、随机 Salt/Nonce 和 Header AAD。

安全失败策略：未知版本/Profile/Flag、Signed Flag、非法长度、CRC、Brotli 长度、摘要、RS 超界、错误密码和认证篡改都不返回 Payload。错误密码与图片改变合并为认证失败。生产随机源只调用平台 CSPRNG；测试确定性随机源只存在于 Tests。

门禁证据：golden vector、压缩往返、安全随机差异、错误密码、未知 Flag、17 符号超界、16 MiB Payload 上限和敏感快照测试通过。详细字段和向量见 [协议文档](../../design/frequency-watermark-v1-protocol.md)。

偏差：V1 明确没有签名和信任存储；Signed Flag 被拒绝而不是显示伪造的“未知签名”。首次发布前允许升级协议，发布后只能新增版本。
