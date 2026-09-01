# ImageLab 公共图像领域边界

## 结论

ImageLab 的公共领域只包含已被真实产品证明稳定的图像事实：像素尺寸、RGBA 像素、六通道平面、sRGB/XYZ D65/CIELAB/HSV 数值转换、目标尺寸面积缩放、抗混叠代理、流式全参考质量、双直方图、有界差异场、8×8 DCT/IDCT、低频 DCT、FFT/IFFT、频率坐标、径向能量/稳健对数功率背景与固定频谱显示量程。文件路径、PNG/JPEG、Avalonia `Bitmap`、窗口、JSON、密码和 Frame 都不属于公共图像领域；水印 Profile 属于 Watermarking 领域，只可作为实验比较维度引用。

```text
Domain/Imaging
  ImageSize              尺寸与 16,000,000 像素上限
  PixelImage             自有 RGBA8888 缓冲区
  LumaPlane              连续 double 亮度平面
  ColorSpaceConverter    RGB ↔ YCbCr 的 Y-only 投影/重建
  SrgbColorSpace         sRGB 编码、线性 RGB 与 XYZ D65
  CieLabColorSpace       XYZ D65 与 CIELAB，固定 δ=6/29
  HsvColorSpace          标准 HSV 与灰阶 Hue N/A
  CieDeltaE              ΔE76 与 CIEDE2000
  ImageChannelConverter 六通道抽取与选定通道重建
  ImageAnalysisProxyProjector 面积平均分析代理
  ImageQualityCalculator 既有水印兼容入口；内部复用 O(1) 额外内存分析器
  ImagePreviewProjector 有界分析预览
  ImageAreaResampler    最大边与明确目标尺寸的面积平均

Domain/Frequency
  Dct8x8Transform        纯 8×8 DCT-II / IDCT
  Fft1D/Fft2DTransform   可取消 radix-2 FFT/IFFT
  FrequencySpectrum      只读复数频谱与补零语义
  FrequencyCoordinates   中心化、半径与共轭索引
  SpectrumProjector      幅度、相位与频点 DTO
  SpectrumDisplayScale   多张频谱共用的显式显示上限
  RadialEnergyAnalyzer   256-bin 与频带占比
  RadialLogPowerBaseline 128 桶稳健中位数/MAD 背景
  FrequencyBandMaskFactory 共轭对称径向遮罩
  FrequencyGainMask      不可变 `[0,1]` 共轭对称实数增益
  FrequencyInverseTransformer 原地 IFFT、有限值、`1E-8` 与 crop
  FrequencyMaskApplier   只负责增益乘法并委托共享逆变换
  DctBlockAnalyzer       完整 8×8 单块报告

Domain/Watermarking
  Profile、Payload、容量、检测与验证状态、QIM

Domain/Comparison
  ImagePairValidator              同尺寸逐像素前置条件
  FullReferenceQualityAnalyzer    PSNR-Y/RGB、全局 SSIM-Y、RGB/Alpha 误差
  ImageHistogramAnalyzer          双图六通道 256-bin 计数
  ImageDifferenceProxyAnalyzer    先差异后面积聚合的有界基础差异场
  ImageDifferenceProxyProjector   六档 RGB 绝对差异着色
  DifferenceHeatmapProjector      固定量纲 MaxRGB/Y 伪彩色
  ImagePairPixelInspector         Candidate - Reference 像素对报告
  ImageComparisonSummary          不含路径或 UI 的统一领域摘要

Domain/Robustness
  RobustnessRecipe/Scan           强类型步骤、单轴扫描与资源上限
  DeterministicTrialRandom        与密码学随机源隔离的可复现实验随机性
  Operators                       单责像素、颜色、滤波和几何 Strategy
  RobustnessResults               BER、失败分类、曲线、质量和 16×16 局部网格

Domain/Fingerprinting
  FingerprintLumaNormalizer       白底 Alpha、BT.601 与面积/双线性归一化
  IImageFingerprintAlgorithm     唯一朴素 Strategy；aHash、dHash、pHash 显式登记
  ImageFingerprint               稳定算法身份、64 位值与行优先位序
  FingerprintDistanceCalculator  同算法汉明距离与位相似度
  FingerprintDecisionPolicy      版本化参考阈值和非概率结论

Domain/Frequency
  OrthogonalDctBasis             不决定中心化语义的正交 DCT 数值基元
  LowFrequencyDctTransform       32×32 输入的左上 8×8 低频 DCT

Domain/FrequencyMaskEditing
  FrequencyMaskRecipe/Operation  归一化有界操作和规范指纹
  FrequencyMaskRasterizer        画笔、橡皮、矩形、圆环和强度重放
  ConjugateMaskWriter            自共轭安全的原子配对写入
  MaskEditHistory                不保存完整遮罩的 undo/redo 游标

Domain/Checksums
  Crc32                          协议中立 IEEE CRC-32 数值原语；不承担认证

Domain/Steganography
  LsbFrameCodec                  独立 ILSB Frame 与结构化读取状态
  LsbSlotLayout/ILsbSlotOrder    Alpha=255、RGB 顺序与两个朴素 Strategy
  LsbEmbedding/Extraction        不变输入 replacement 与严格回读
  LsbStatisticsAnalyzer          Scope、位分布、PoV 卡方和方向邻接

Domain/Convolution
  ConvolutionKernel              3..31 奇数方、中心锚点、不可变系数
  ConvolutionPresetFactory       显式有限目录，不执行图片处理
  BorderIndexMapper              Constant/Replicate/Reflect-101/Wrap
  SpatialConvolver               行优先真二维卷积与 raw/裁切统计
  GradientCombiner               Gx/Gy 的非线性 Magnitude 组合
  KernelFrequencyResponseAnalyzer 256² 归一化核响应与双核摘要
  ConvolutionDifference/Inspector 同尺寸差异和逐项贡献

Domain/ColorTransfer
  ColorDistributionAnalyzer Alpha 加权在线统计、固定直方图/密度与 JSD
  RgbColorAggregator         固定 32³ 的 5-bit RGB 实际均值聚合
  DominantColorClusterer     确定性加权 Lab k-means 与 fingerprint
  SrgbGamutMapper            保持 L*/hue 的 chroma 二分映射
  LabStatisticsTransfer      CIELAB 独立通道均值/标准差迁移
  FixedPaletteRemapper       精确 ΔE76 最近色与稳定 cluster tie-break
  PerceptualDifferenceAnalyzer 固定数组 ΔE00 汇总
  ColorPixelInspector        分图片坐标的 sRGB/HSV/Lab/palette 事实

Domain/SpectralArt
  SpectralPattern            不可变有界权重、来源、采样与指纹
  SpectralPatternMapper      闭开区域、保护带、Contain/Stretch 映射
  SpectralAmplitudeWriter    径向稳健尺度、相位保持与精确共轭写入
  SpectralArtReconstructor   消费唯一工作频谱并复用 Y 回写
  SpectralArtDiagnostics     能量、可见性、共轭、质量和差异事实
```

## 依赖方向

`Domain` 不依赖 Avalonia、文件系统、JSON、DI 或密码库。`Application` 通过图片、文字栅格、报告、剪贴板和原子写入窄端口协调领域对象。`Infrastructure` 才接入 Avalonia 编解码/字体、平台密码学、Host 文件交互、JSON 与磁盘发布。十八个 Document 依赖应用用例接口，不直接执行像素扫描、FFT、DCT、卷积、聚类、颜色迁移、BER、加密或文件编码。

该方向满足 SOLID 中的单一职责、接口隔离与依赖倒置：新增频谱查看器可以复用 `PixelImage`、`LumaPlane` 与 DCT；新增水印算法必须进入自己的 Watermarking 领域，不能把算法路由塞进 Imaging。

## 所有权与边缘规则

- `PixelImage` 构造时复制 RGBA 输入；跨层不会共享可变外部数组。
- `WatermarkPayload` 同样拥有私有副本，并在 `Dispose` 时清零。
- 只有 Alpha 全部不低于 250 的 8×8 块可成为载体；透明块逐字节不改。
- 宽高不能整除 8 时，只处理完整块；右边和下边余量逐字节不改。
- 所有输出保持原始尺寸和 Alpha；源 `PixelImage` 不被原地修改。
- 频谱 Session 只缓存一份只读复数频谱；重建使用一份短生命周期工作副本，不建立滤镜注册中心、万能图像服务或全局可变缓存。
- Spectral Art Session 独占源图、Y 平面和只读频谱；一次 Render 只创建一个完整工作 `Complex[]`，全部频域诊断后由 IFFT 原地消费。Pattern、recipe、serializer 和成功语义保持产品专用。
- 频谱遮罩历史保存最多 128 条操作描述而非完整 `double[]`；导入只接受严格 Recipe 并重新光栅化，不信任外部增益数组。
- 比较 Session 由单个 scoped Document 独占，两张全图长期保留；两张显示代理、基础差异场和当前投影最大边均为 1024。
- 质量统计按行确定性扫描，仅保留常量个累加器，不再为两张图创建全尺寸 `double[]` 亮度平面。
- Alpha 不进入颜色指标；透明 RGB 仍参与统计。尺寸不同时只返回结构化原因，不静默缩放、裁切或对齐。
- 鲁棒性算子永不原地修改输入；随机算子从案例稳定事实派生子种子，不能使用 `Random.Shared` 或水印安全随机源。
- 指纹 Session 长期只持有两张完整图和两张 1024 代理；算法 singleton 不缓存图片或矩阵。稳定性最多 21 点串行执行，只保留当前样本预览。
- 缩放/裁剪/补边改变尺寸后，全参考质量明确为 `N/A/SizeMismatch`；平移、固定画布旋转和透视按同坐标质量统计，不做隐藏配准。
- LSB 只把 Alpha=255 像素作为 R/G/B 槽位；输入不原地修改，Frame/位置/统计属于独立 Steganography 领域，不复用 DCT 水印 Frame 或 Carrier。
- LSB Session 由单个 scoped Document 独占，释放时清零 Frame；位置图和 bit 图最大边 1024，受控扰动每次从同一 stego 基线开始。
- 卷积 Session 由单个 scoped Document 独占完整源图和 512/1024/2048 代理；Alpha 不参与，完整结果绑定 recipe fingerprint，参数变化后禁止导出。
- 核响应固定 256²，只包含归一化线性核；偏置、边界、字节裁切和 Magnitude 非线性不进入 H(u,v)。
- 颜色 Session 由单个 scoped Document 独占目标、参考和一个当前结果；预览最大边 512，完整扫描不缓存逐像素 Lab。
- 颜色统计中 A=0 完全排除，0<A<255 以 A/255 加权；处理结果 Alpha 原字节保持，全透明 RGBA 四字节不变。
- 颜色迁移不要求目标/参考同尺寸，也不缩放或建立像素对应；固定调色板排序只改显示，不改变 cluster identity。

## 扩展规则

后续工具只有满足以下条件才可把能力提升到公共领域：至少两个产品用例需要；语义不包含某个 UI 或协议；可以用纯数值测试验证；内存所有权明确。否则先留在具体 Feature/Watermarking 内部。
