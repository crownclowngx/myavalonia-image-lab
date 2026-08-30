# ImageLab 公共图像领域边界

## 结论

ImageLab 的公共领域只包含与具体工具无关、且已被频域水印证明需要的图像事实：像素尺寸、RGBA 像素、亮度平面、颜色变换、8×8 DCT/IDCT 和质量指标。文件路径、PNG/JPEG、Avalonia `Bitmap`、窗口、密码、Frame 与水印 Profile 都不属于公共图像领域。

```text
Domain/Imaging
  ImageSize              尺寸与 16,000,000 像素上限
  PixelImage             自有 RGBA8888 缓冲区
  LumaPlane              连续 double 亮度平面
  ColorSpaceConverter    RGB ↔ YCbCr 的 Y-only 投影/重建
  ImageQualityCalculator PSNR 与全局 SSIM
  ImagePreviewProjector 有界分析预览

Domain/Frequency
  Dct8x8Transform        纯 8×8 DCT-II / IDCT

Domain/Watermarking
  Profile、Payload、容量、检测与验证状态、QIM
```

## 依赖方向

`Domain` 不依赖 Avalonia、文件系统、DI 或密码库。`Application` 通过 `IImageCodec`、`IRandomSource`、`IAtomicFileWriter` 和文件对话框端口协调领域对象。`Infrastructure` 才把 Avalonia 编解码、平台密码学、Host 文件交互与磁盘发布接入。两个 Document 依赖应用用例接口，不直接执行 DCT、加密或文件编码。

该方向满足 SOLID 中的单一职责、接口隔离与依赖倒置：新增频谱查看器可以复用 `PixelImage`、`LumaPlane` 与 DCT；新增水印算法必须进入自己的 Watermarking 领域，不能把算法路由塞进 Imaging。

## 所有权与边缘规则

- `PixelImage` 构造时复制 RGBA 输入；跨层不会共享可变外部数组。
- `WatermarkPayload` 同样拥有私有副本，并在 `Dispose` 时清零。
- 只有 Alpha 全部不低于 250 的 8×8 块可成为载体；透明块逐字节不改。
- 宽高不能整除 8 时，只处理完整块；右边和下边余量逐字节不改。
- 所有输出保持原始尺寸和 Alpha；源 `PixelImage` 不被原地修改。
- V1 不预建 FFT、DWT、滤镜注册中心、万能图像服务或全局可变缓存。

## 扩展规则

后续工具只有满足以下条件才可把能力提升到公共领域：至少两个产品用例需要；语义不包含某个 UI 或协议；可以用纯数值测试验证；内存所有权明确。否则先留在具体 Feature/Watermarking 内部。
