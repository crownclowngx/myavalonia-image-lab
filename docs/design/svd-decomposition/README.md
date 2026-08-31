# SVD Decomposition／奇异值分解重建

SVD Decomposition V1 已实现为 ImageLab 的第十四个多实例 Persistable Document，稳定 ID 为
`myavalonia.plugin.image.lab.document.svd-decomposition`。它在最大边 128/256 的抗混叠分析代理上提供单边
Jacobi SVD、奇异值与累计能量、Rank-k 重建、单个秩一分量的有符号投影，以及 Y/RGB/YCbCr 固定三策略比较。

它是低秩近似实验工具，不是图片文件压缩器；PNG 和报告都只描述当前分析代理，不计算文件压缩率。

## 推荐阅读顺序

1. [新手使用说明](user-manual.md)：完成一次载图、分解、Rank 与分量实验。
2. [开发与高级使用指南](guide.md)：理解分层、缓存、状态、生命周期和扩展边界。
3. [数学与数值协议](mathematical-principles.md)：矩阵轴向、Jacobi、能量、颜色和误差。
4. [报告 schema](report-schema.md)：严格 JSON 与稳定 CSV 字段。
5. [测试与本地门禁](testing.md)：Golden、架构门禁及已证明/未证明结论。
6. [原始实施计划与完成状态](implementation.md)和[实施历史](history/README.md)。

## V1 边界

- 只处理最大边 128/256 的分析代理，小图不放大；不对原尺寸大图执行 SVD。
- 单通道可选 R/G/B/Y/Cb/Cr；RGB 和 YCbCr 分别独立分解三个矩阵，共用一个 k。
- k 与分量变化只读取已缓存因子，不重新分解；源图或代理变化会释放整个 Session。
- JSON 将精确重建的无穷 PSNR 表达为 `isExact=true, psnrDb=null`，不输出非法浮点值。
- Domain 不引用 Avalonia、文件、JSON、DI 或 Host SDK；Document 不实现矩阵算法。
- 本轮没有使用 AIFLOW，没有新增 Windows CI，也没有执行 ZIP、真实 Host、安装或发布门禁。
