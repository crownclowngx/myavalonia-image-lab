# G4 容量与嵌入引擎记录

状态：完成（2026-08-30）

实现了透明块筛选、Control/Data 分区、RS 开销反推、加密 Tag 开销、三种 Profile、确定性位置排列、DCT-QIM 嵌入、Y-only 重建、PSNR/SSIM、差异图和 DCT 频谱图。

`EmbedWatermarkUseCase` 的关键不变量是：先估算再生成 Frame；容量不足在调用 Carrier 前失败；Carrier 返回新 `PixelImage` 而不改源对象；实际 PNG/JPEG 输出字节必须由正式提取器恢复出完全相同 Payload，否则不返回 `EmbedResult`。Document 只有拿到该结果后才允许保存，磁盘发布使用同目录临时文件原子替换。

门禁证据：128×128 控制信道不足零修改；三种 Profile 像素量化往返；PNG 正式字节回读；Robust JPEG 100 输出自检；Alpha、透明块与非 8 倍数边缘不变；原子替换不遗留 `.tmp`。

偏差：没有覆盖源图入口。JPEG 低质量不是正式承诺；失败时建议改用 PNG/Robust，而不是静默降级或截断。
