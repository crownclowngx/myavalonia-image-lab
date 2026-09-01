# ImageLabPlugin

这是 ImageLab 的 Managed Plugin 解决方案。当前提供频域隐式水印相关双 Document，以及“频域分析器”“图像比较实验室”“鲁棒性实验室”“感知指纹”“位平面观察器”“LSB 隐写与统计实验”“卷积核实验台”“小波实验室”“频域滤波”“频谱遮罩编辑器”“周期噪声与陷波器”“奇异值分解重建”“调色板与颜色迁移”“内容感知缩放”“梯度域融合”“频谱艺术”和“混合图像”共十九个 Persistable Document：
前者在图片 Y 通道的 8×8 DCT 中频系数写入受容量限制的 Payload；频域分析器提供六通道 FFT/DCT 与频带重建；
比较实验室对两张同尺寸图片提供同步视图与客观指标；鲁棒性实验室用确定性扰动链、单参数扫描、分步水印诊断、
成功率曲线和 Profile 矩阵测量恢复边界；感知指纹使用 aHash、dHash、pHash、汉明距离和受控稳定性试验比较两张显式图片；
位平面观察器拆分 R/G/B/Alpha/Y 的 8 位样本并提供掩码重建、统计、探针和 PNG 导出；LSB 隐写与统计实验以独立 `ILSB` Frame、可复现槽位、统计对比和受控扰动解释像素域 LSB 的可检测性与脆弱性；卷积核实验台用真二维卷积、四种边界/归一化、双核梯度、频响和像素贡献解释空间核；小波实验室提供 Haar/CDF 5/3、多层子带、阈值去噪、有限扫描及 DCT/DWT 载体比较；频域滤波提供三家族、四方向、三输出语义、副作用诊断和有限空间核近似；频谱遮罩编辑器提供共轭安全的画笔、橡皮、矩形、圆环、频带锁定、历史和联动 IFFT 重建；周期噪声与陷波器提供稳健候选峰、必须人工采用的共轭 Notch 草案、六视图比较和不可逆损失诊断；奇异值分解重建在 128/256 分析代理上提供单边 Jacobi SVD、Rank-k、能量、秩一分量和固定颜色策略比较；调色板与颜色迁移提供 Alpha 加权分布、确定性主色、CIELAB 统计迁移、固定调色板量化和 ΔE00 诊断；内容感知缩放提供固定 Sobel、区域偏置、确定性最小缝、删除/预规划插入、逐步播放和普通缩放对照；梯度域融合提供二值遮罩、整数放置、三种 guidance、确定性红黑迭代和直接 Alpha 对照；频谱艺术把文字或已有图片映射为共轭安全的 FFT 幅度图案并联动显示质量和频域诊断；混合图像以控制点相似变换、Gaussian 低高频、有符号组合、四尺度和共享量程频谱解释近看／远看主体切换。

真实插件只位于 `src/ImageLabPlugin.Plugin`；`Standalone` 通过同一个 Module 和 DI 入口预览“水印写入”、
“提取与验证”“频域分析器”“图像比较实验室”“鲁棒性实验室”“感知指纹”“位平面观察器”“LSB 隐写与统计实验”“卷积核实验台”“小波实验室”“频域滤波”“频谱遮罩编辑器”“周期噪声与陷波器”“奇异值分解重建”“调色板与颜色迁移”“内容感知缩放”“梯度域融合”“频谱艺术”和“混合图像”十九个真实 Document，不复制业务实现。

> 第一次开始开发前，请先阅读 [项目文档与快速开始](docs/README.md)。其中说明了三个子项目和
> Standalone 窗口的职责、接入真实 Host 的边界，以及临时部署和正式 ZIP 发布流程。

```powershell
dotnet restore
dotnet build ImageLabPlugin.slnx -c Debug -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build
dotnet run --project src/ImageLabPlugin.Standalone
```

当前阶段不执行 Windows CI、ZIP 或发布门禁。准备发布时再按照
[部署与发布文档](docs/design/shared/deployment-and-release.md)执行真实 Host 和正式包验收。

全部资料从 [文档中心](docs/README.md) 或 [设计文档总览](docs/design/README.md) 进入。十八项产品能力各自拥有统一目录：

- [频域隐式水印](docs/design/frequency-watermark/README.md)
- [频域分析器](docs/design/spectrum-inspector/README.md)
- [图像比较实验室](docs/design/image-compare-lab/README.md)
- [鲁棒性实验室](docs/design/robustness-lab/README.md)
- [感知指纹](docs/design/image-fingerprint/README.md)
- [位平面观察器](docs/design/bit-plane-viewer/README.md)
- [LSB 隐写与统计实验](docs/design/lsb-steganography-lab/README.md)
- [卷积核实验台](docs/design/convolution-playground/README.md)
- [小波实验室](docs/design/wavelet-lab/README.md)
- [频域滤波](docs/design/frequency-filter/README.md)
- [频谱遮罩编辑器](docs/design/frequency-mask-editor/README.md)
- [周期噪声与陷波器](docs/design/periodic-noise-removal/README.md)
- [奇异值分解重建](docs/design/svd-decomposition/README.md)
- [调色板与颜色迁移](docs/design/palette-and-color-transfer/README.md)
- [Seam Carving／内容感知缩放](docs/design/seam-carving/README.md)
- [Poisson Blending／梯度域融合](docs/design/poisson-blending/README.md)
- [Spectral Art／频谱艺术](docs/design/spectral-art/README.md)
- [Hybrid Image／混合图像](docs/design/hybrid-image/README.md)

每个目录都包含现有实施计划、测试门禁、详细指南、新手使用说明、数学原理和实施历史。当前实现完成本地开发自动门禁；真实 Host、ZIP、Windows CI 与发布封板仍未执行。

Frequency Filter／频域滤波已完成[专用实现与证据文档](docs/design/frequency-filter/README.md)和本地自动门禁；该结论不代表发布完成。
Frequency Mask Editor／频谱遮罩编辑器已完成[专用实现与证据文档](docs/design/frequency-mask-editor/README.md)和本地自动门禁；该结论同样不代表发布完成。
Periodic Noise Removal／周期噪声与陷波器已完成[专用实现与证据文档](docs/design/periodic-noise-removal/README.md)和本地自动门禁；自动候选不代表噪声结论，该结论也不代表发布完成。
SVD Decomposition／奇异值分解重建已完成[专用实现与证据文档](docs/design/svd-decomposition/README.md)和本地自动门禁；它只解释分析代理的低秩近似，不是文件压缩器，该结论也不代表发布完成。
Palette And Color Transfer／调色板与颜色迁移已完成[专用实现与证据文档](docs/design/palette-and-color-transfer/README.md)和本地自动门禁；它只解释固定 sRGB D65 协议下的颜色统计与量化，不是专业色彩管理或自动美化器，该结论也不代表发布完成。

Seam Carving／内容感知缩放已完成[专用实现与证据文档](docs/design/seam-carving/README.md)和本地自动门禁；
它只解释固定能量协议下的路径选择，不是语义分割或自动修图，该结论也不代表发布完成。

Poisson Blending／梯度域融合已完成[专用实现与证据文档](docs/design/poisson-blending/README.md)和本地自动门禁；
它只证明固定离散协议的数值结果，不是 AI 抠图、自动配准或视觉质量保证，该结论也不代表发布完成。

Spectral Art／频谱艺术已完成[专用实现与证据文档](docs/design/spectral-art/README.md)和本地自动门禁；它只展示频谱可见图案，不是 Payload 水印、扫码器或隐写安全证明，该结论也不代表发布完成。
