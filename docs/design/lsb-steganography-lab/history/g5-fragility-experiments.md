# G5 脆弱性实验

- 实际修改：新增 JPEG 95/80/60、缩放 75%/50% 往返、Gaussian 0.6/1.2、Median 3×3 allowlist 协调用例，返回 Frame/Header/Payload BER、读取状态和 PSNR。
- 自动证据：固定图高斯预设从同一 stego 重跑得到逐字节一致结果并暴露 LSB 失败。
- 设计取舍：直接调用既有 `IImagePerturbationOperator`；无扫描、组合链、DAG 或第二套公式。
- 遗留：各自然图片恢复率不作保证；JPEG/缩放要求全不透明。
- 回滚：移除单个用例，Robustness Lab 不受影响。
