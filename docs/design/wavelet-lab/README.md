# Wavelet Lab／小波实验室

Wavelet Lab V1 已实现为 ImageLab 的第十个多实例 Persistable Document，稳定 ID 为
`myavalonia.plugin.image.lab.document.wavelet-lab`。它提供 Haar/CDF 5/3 二维离散小波、多层 packed 金字塔、
LL/LH/HL/HH 子带投影、可逆重建、Hard/Soft 阈值、MAD/Universal 建议、有限参数扫描、完整尺寸 PNG、
JSON/CSV 报告，以及共同 Payload 下的 DCT/DWT 实验性载体比较。

## 推荐阅读顺序

1. [新手使用说明](user-manual.md)：从载图到导出的一次完整实验。
2. [开发与复用指南](guide.md)：稳定 ID、分层、状态机、资源和扩展边界。
3. [数学原理](mathematical-principles.md)：轴向、扩展、Haar、CDF 5/3、阈值和差分 QIM。
4. [测试证据](testing.md)：Golden、自动门禁和没有证明的结论。
5. [报告 schema](report-schema.md)：JSON/CSV 的版本和字段含义。
6. [实施计划与真实状态](implementation.md)及[历史记录](history/README.md)。

## V1 边界

- Domain 不引用 Avalonia、文件系统、JSON、DI 或 Host SDK；Application 不实现 DWT 数学循环。
- 只登记 Haar 与 CDF 5/3 两种朴素 Strategy；没有动态算法发现、通用 Pipeline、Mediator 或事件总线。
- 分析代理用于交互；PNG 导出只接受当前配方指纹对应的显式完整尺寸结果。
- 无干净参考图时不提供“最佳去噪质量”结论；水印比较只描述当前图片、Payload、强度和扰动。
- 本轮没有使用 AIFLOW，没有添加 Windows CI，也没有执行 ZIP、真实 Host 或发布门禁。
