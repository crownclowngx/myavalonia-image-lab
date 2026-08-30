# ImageLab 公共图像领域边界

## 结论

ImageLab 的公共领域只包含已被水印、频域分析器或比较实验室证明可复用的图像事实：像素尺寸、RGBA 像素、六通道平面、颜色变换、抗混叠代理、流式全参考质量、双直方图、有界差异场、8×8 DCT/IDCT、FFT/IFFT、频率坐标与径向能量。文件路径、PNG/JPEG、Avalonia `Bitmap`、窗口、JSON、密码、Frame 与水印 Profile 都不属于公共图像领域。

```text
Domain/Imaging
  ImageSize              尺寸与 16,000,000 像素上限
  PixelImage             自有 RGBA8888 缓冲区
  LumaPlane              连续 double 亮度平面
  ColorSpaceConverter    RGB ↔ YCbCr 的 Y-only 投影/重建
  ImageChannelConverter 六通道抽取与选定通道重建
  ImageAnalysisProxyProjector 面积平均分析代理
  ImageQualityCalculator 既有水印兼容入口；内部复用 O(1) 额外内存分析器
  ImagePreviewProjector 有界分析预览

Domain/Frequency
  Dct8x8Transform        纯 8×8 DCT-II / IDCT
  Fft1D/Fft2DTransform   可取消 radix-2 FFT/IFFT
  FrequencySpectrum      只读复数频谱与补零语义
  FrequencyCoordinates   中心化、半径与共轭索引
  SpectrumProjector      幅度、相位与频点 DTO
  RadialEnergyAnalyzer   256-bin 与频带占比
  FrequencyBandMaskFactory 共轭对称径向遮罩
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
```

## 依赖方向

`Domain` 不依赖 Avalonia、文件系统、JSON、DI 或密码库。`Application` 通过图片、报告、剪贴板和原子写入窄端口协调领域对象。`Infrastructure` 才接入 Avalonia 编解码、平台密码学、Host 文件交互、JSON 与磁盘发布。四个 Document 依赖应用用例接口，不直接执行像素扫描、FFT、DCT、加密或文件编码。

该方向满足 SOLID 中的单一职责、接口隔离与依赖倒置：新增频谱查看器可以复用 `PixelImage`、`LumaPlane` 与 DCT；新增水印算法必须进入自己的 Watermarking 领域，不能把算法路由塞进 Imaging。

## 所有权与边缘规则

- `PixelImage` 构造时复制 RGBA 输入；跨层不会共享可变外部数组。
- `WatermarkPayload` 同样拥有私有副本，并在 `Dispose` 时清零。
- 只有 Alpha 全部不低于 250 的 8×8 块可成为载体；透明块逐字节不改。
- 宽高不能整除 8 时，只处理完整块；右边和下边余量逐字节不改。
- 所有输出保持原始尺寸和 Alpha；源 `PixelImage` 不被原地修改。
- 频谱 Session 只缓存一份只读复数频谱；重建使用一份短生命周期工作副本，不建立滤镜注册中心、万能图像服务或全局可变缓存。
- 比较 Session 由单个 scoped Document 独占，两张全图长期保留；两张显示代理、基础差异场和当前投影最大边均为 1024。
- 质量统计按行确定性扫描，仅保留常量个累加器，不再为两张图创建全尺寸 `double[]` 亮度平面。
- Alpha 不进入颜色指标；透明 RGB 仍参与统计。尺寸不同时只返回结构化原因，不静默缩放、裁切或对齐。

## 扩展规则

后续工具只有满足以下条件才可把能力提升到公共领域：至少两个产品用例需要；语义不包含某个 UI 或协议；可以用纯数值测试验证；内存所有权明确。否则先留在具体 Feature/Watermarking 内部。
