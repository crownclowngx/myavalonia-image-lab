# Hybrid Image Report Schema 1

协议名：`hybrid-image-report-v1`。JSON 与 CSV 表达同一组脱敏事实。

报告包含：

- A/B 内容指纹和原始尺寸；
- recipe fingerprint；
- B→A 缩放、旋转、平移、残差状态、RMS/max 与覆盖率；
- 完整裁切矩形；
- 两个 σ、理论 f50 和两个 gain；
- raw min/max/mean、下溢、上溢、总裁切数与比例；
- 四个尺度的真实像素尺寸；
- 运行耗时、实现版本与解释限制。

报告禁止绝对路径、图片像素、频谱数组、控制点截图、用户目录和原始文件名。CSV 使用 invariant culture 并对每个字段执行 RFC 风格双引号转义。
