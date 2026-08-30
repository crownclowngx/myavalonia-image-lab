# G5 提取与验证引擎记录

状态：完成（2026-08-30）

读取器先从固定 Control Channel 三副本做加权投票与 RS/CRC，再依据 Header 判断密码和 Profile。只有解析出受支持 Header 后才派生 Mapping Key 并读取 Data Channel；随后执行 RS、AES-GCM、Brotli 和摘要验证。

`ExtractionReport` 区分未发现、需要密码、可提取、纠错恢复、完整性有效、版本不支持、不可恢复、认证失败和资源/格式拒绝。V1 的真实性为 `NotSigned`，不会把 SHA 摘要或 AES 完整性称为作者签名。

门禁证据：无水印随机图、PNG、JPEG 95 重编码、小噪声、错误密码、未知 Flag、ECC 16/17 边界和超大编码文件均有自动测试。恢复内容由 Report 拥有副本，协议 Payload 私有缓冲区随即清零。

回滚：若读取器出现安全问题，应隐藏写入并拒绝受影响版本，不能继续生成无法可靠自检的图片。
