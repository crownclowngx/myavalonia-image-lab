# ImageLabPlugin V1 图像比较实验室实施计划

> 计划状态：开发实现与本地自动门禁完成；人工/发布阶段延期
> 基线日期：2026-08-30  
> 产品名称：Image Compare Lab／图像比较实验室  
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3  
> 核心路线：同尺寸双图会话 + 同步视觉比较 + 流式全图统计 + 有界差异投影 + 可复用比较摘要  
> 实施原则：SOLID 优先；设计模式朴素使用；先冻结指标语义和资源边界，再实现纯领域算法、应用用例、Document 与交互

| 实施包 | 当前状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 已完成 | 冻结 V1 产品范围、术语、指标、资源、持久化和失败语义 | [实施记录](history/g0-product-and-numeric-baseline.md) |
| G1 | 已完成 | 建立双图比较值对象，并把现有质量计算改造成低内存、可取消的公共基础 | [实施记录](history/g1-comparison-domain-foundation.md) |
| G2 | 已完成 | 完成像素检查、差异投影、伪彩色热力图和六通道直方图 | [实施记录](history/g2-difference-and-histogram.md) |
| G3 | 已完成 | 完成比较 Session、应用用例、统一摘要和 JSON 输出 | [实施记录](history/g3-use-cases-and-summary.md) |
| G4 | 已完成 | 完成 Persistable Document、取消、快照、迟到结果和资源生命周期 | [实施记录](history/g4-document-lifecycle.md) |
| G5 | 已完成 | 完成并排、分割、叠加、闪烁、同步缩放和悬停联动界面 | [实施记录](history/g5-ui-and-interaction.md) |
| G6 | 已完成 | 接入 Module、Standalone、Headless UI 和跨 Document 复用入口 | [实施记录](history/g6-integration.md) |
| G7 | 自动门禁完成；人工验收延期 | 完成本地双配置门禁、专用文档、人工验收和开发阶段封板 | [实施记录](history/g7-local-sealing.md) |

本文定义 ImageLab 在“频域隐式水印”和“频域分析器”之后的第三个产品能力、第四个 Persistable Document。
它不是图片编辑器，也不负责猜测两张不同尺寸图片应该怎样缩放或配准。V1 只对像素坐标一一对应的同尺寸图片
给出可复现的全参考比较结果；遇到尺寸不同时必须显式阻断指标计算，并告诉用户原因和后续能力边界。

本文是 V1 实施时的总计划。每个 G 包完成后，必须在对应实施记录中填写实际修改、自动测试、数值证据、
性能数据、偏差、遗留风险和回滚方式；本文不得提前写入完成结论。

## 1. V1 目标与固定实施顺序

### 1.1 用户闭环

```text
选择参考图
    ↓
选择待比较图
    ↓
解码并显示两张图片的格式化尺寸信息
    ↓
验证尺寸完全一致；不一致时停止计算并显示可见警告
    ↓
执行全分辨率流式统计，建立有界显示代理
    ↓
查看 PSNR-Y、PSNR-RGB、全局 SSIM-Y、误差统计和变化像素比例
    ↓
在并排、拖动分割、透明叠加和闪烁模式间切换
    ↓
同步缩放与平移，悬停读取同一坐标的 RGBA、Y 和绝对变化
    ↓
查看可调放大的 RGB 差异图、固定量纲伪彩色热力图和六通道直方图
    ↓
复制或原子导出版本化 JSON 比较摘要，供其他实验或人工记录复用
```

### 1.2 固定实施顺序

1. G0 先冻结参考图/待比较图语义、颜色公式、Alpha、PSNR、全局 SSIM、热力图和尺寸错误语义；
2. G1 先用纯领域测试证明全图统计正确且不创建两份 `double[]` 亮度平面，再允许 UI 消费；
3. G2 分别实现像素、差异、热力图和直方图，不把所有算法塞进一个万能服务；
4. G3 用窄应用用例协调解码、验证、分析、摘要和导出，并建立 Session 所有权；
5. G4 让 scoped Document 管理实例状态、取消、generation、快照和关闭释放；
6. G5 最后接入视觉比较、同步视口、悬停和无障碍交互；
7. G6 接入第四个真实 Document、Standalone 和现有水印结果的公共摘要消费边界；
8. G7 执行本地 Debug/Release 门禁并同步专用文档，不执行发布阶段门禁。

### 1.3 分期边界

V1 是可信的“同尺寸基础比较器”，只实现可以从现有 `PixelImage`、颜色公式、PSNR、全局 SSIM、差异投影和
预览基础稳妥演进的能力。

以下能力必须先建立独立设计，再决定是否进入 V2，不得在 V1 实施中顺手加入：

- 滑动窗口局部 SSIM 和 SSIM Map；
- MS-SSIM 的滤波核、尺度、降采样和权重；
- CIE Lab 白点、RGB 传递函数、色域和 Delta E 2000 参考实现；
- Sobel/Scharr 等边缘与梯度差异，以及纹理描述子；
- 不同尺寸图片的缩放、裁剪、补边、平移、特征配准或自动对齐；
- 任何“相似/不相似”“质量合格/不合格”的通用阈值结论。

后续设计必须说明精度基准、性能预算、第三方参考实现、数据集授权、误差容限和用户可见的变换记录。尤其是
对齐后的指标只能描述“经明确变换后的两张图”，不能伪装成原始像素的一一比较。

## 2. 当前基线与已有事实

### 2.1 当前工程基线

当前仓库已经具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集；
- `ImageLabPlugin.Standalone` 复用真实 Module、View、Document 和 DI 服务；
- 三个 Persistable Document：“水印写入”“提取与验证”和“频域分析器”；
- 自有 RGBA8888 `PixelImage`、16,000,000 像素安全上限和 64 MiB 编码图片上限；
- RGB/YCbCr 六通道公式、面积平均分析代理、最近邻只读预览和图片编解码端口；
- `ImageQualityCalculator` 的亮度 PSNR 与全局 SSIM；
- `ImageDifferenceProjector` 的 RGB 绝对差异和 1–32 倍放大；
- Document Scope、`IDocumentLifetime`、generation、取消、快照恢复和 Bitmap 替换模式；
- Avalonia PNG/JPEG 正式编解码器、文件选择适配器和原子文件写入器；
- 当前 68 个 Domain、协议、用例、Document 生命周期、组合根和 Headless View 自动测试；
- Debug/Release、`--locked-mode`、`--warnaserror` 的本地开发门禁惯例；
- 当前明确延期的真实 Host、ZIP、Windows CI 和正式发布封板。

### 2.2 可直接复用的能力

- `ImageSize`、`PixelImage` 和构造时复制的像素所有权；
- `IImageCodec`、`IImageFileDialog` 和 `IAtomicFileWriter`；
- `ColorSpaceConverter.ToLuma` 与 `ImageChannelConverter` 的六通道冻结公式；
- `ImageAnalysisProxyProjector` 的面积平均代理，适合视觉预览；
- `ImagePreviewProjector` 的有界只读预览思想；
- `ImageQualityCalculator` 已有水印调用点及其回归测试；
- `ImageDifferenceProjector` 的源对象不变、尺寸验证和放大范围；
- Spectrum Inspector 已验证的 scoped Session、取消、迟到结果和快照模式；
- Headless Avalonia View 测试、Standalone 真实组合入口和原子输出方式。

### 2.3 已知缺口与必须修正的问题

- 当前 `ImageQualityCalculator` 会为两张图各生成一份全尺寸 `double[]` 亮度平面；16M 像素时仅两份亮度值
  就约占 244 MiB，不适合长期作为统一比较器；
- 当前 `ImageQualityMetrics` 只有 `Psnr` 和 `Ssim` 两个无单位字段，未明确它们都是 Y 通道结果；
- 当前全局 SSIM 是整图均值、样本方差和协方差公式，不是常见的滑窗 SSIM 平均；UI 和摘要必须诚实命名；
- 当前差异图只有 RGB ×N 投影，没有亮度/最大 RGB 标量热力图、图例或饱和像素统计；
- 当前没有双图 Session、像素对查询、直方图、统一摘要和 JSON schema；
- 当前没有同步双视口、分割线、叠加或闪烁控件；
- 当前文件对话框没有独立的比较报告导出意图；
- 当前 Module 和 Standalone 只认识三个 Document。

### 2.4 主工程约束

- Plugin Module 是贡献和服务登记的唯一事实源；
- Document 每个实例拥有独立 DI Scope；同一种比较 Document 可以同时打开多个实例；
- Tool 是插件级 singleton，不得持有参考图、待比较图、视口或报告状态；
- 插件只依赖公开 Plugin SDK/UI SDK，不引用 Host、Dock 或 Host 内部实现；
- Domain 不依赖 Avalonia、文件系统、JSON、DI、图片编码或窗口；
- View 不拥有算法、文件、报告写入和 Document 生命周期；
- V1 原则上不新增第三方图像、数学、图表或原生运行时依赖；
- 不使用 AIFLOW，不登记 Workflow Action 或 Workbench Command；
- 当前不新增 Windows CI，不执行 ZIP、真实 Host 或发布门禁。

## 3. Document 形态与状态所有权

### 3.1 贡献形态

“图像比较实验室”固定登记为第四个 Persistable Document：

| 字段 | 固定值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.image-compare-lab` |
| 显示名称 | `图像比较实验室` |
| 描述 | `以同步视图、像素差异、客观指标和直方图比较两张同尺寸图片` |
| 分类 | `图像分析` |
| Host 注册 | `AddPersistableDocument<ImageCompareLabDocument, ImageCompareLabView>` |
| 实例基数 | 多实例，每个实例独立双图、显示参数、Session、取消令牌和报告 |

选择 Document 而不是 Tool 的原因：

- 两张输入路径、参考方向、视口、参数和摘要构成一个可恢复的实验工作上下文；
- 用户可能同时比较水印输出、频带重建和其他实验结果；
- 关闭一个实例必须释放两张原图、显示代理、投影和 Bitmap；
- singleton Tool 会错误共享当前图片、闪烁状态和取消令牌；
- 比较结果需要 Dirty/Revision 和轻量快照语义。

### 3.2 Document 私有状态

持久状态：

- 参考图片路径与待比较图片路径；
- 当前显示模式：并排、分割、叠加、闪烁、RGB 差异或热力图；
- 分割线比例、叠加透明度和闪烁间隔；
- 差异放大倍数、热力图标量来源；
- 直方图通道；
- 缩放倍数和归一化视口中心；
- 是否显示像素准线、直方图和摘要侧栏。

只存在于当前运行实例的派生状态：

- 两张已解码 `PixelImage`；
- 两张同尺寸显示代理与原图/代理坐标映射；
- 全图质量指标、误差统计、直方图和统一摘要；
- RGB 差异代理、伪彩色热力图代理和颜色图例；
- Avalonia `Bitmap`；
- 当前操作进度、取消源、generation 和错误状态。

瞬时交互状态：

- 当前悬停坐标及两图像素值；
- Pointer 是否正在拖动分割线或平移；
- 当前闪烁帧；
- 控件实际尺寸、黑边和 Pointer 捕获；
- 防抖期间尚未提交的差异放大参数。

悬停、闪烁帧和纯查看模式切换不推进 Revision。路径、比较参数和持久视口发生变化时推进 Revision。若实现期
发现频繁缩放会制造无意义 Dirty，可只在 Pointer 操作结束时提交最终视口。

### 3.3 快照与恢复

- 快照 schema 从 `1` 开始；枚举按稳定英文字符串或显式数值保存，不序列化中文显示文字；
- 不把图片字节、RGBA、Bitmap、直方图、差异投影、指标、摘要 JSON 或异常堆栈写入快照；
- 恢复时验证路径、枚举、倍数、透明度、间隔、缩放和视口中心；非法值回退到安全默认值；
- 恢复后只显示路径与参数，不自动读取文件或执行全图比较；用户显式点击“比较”后才开始工作；
- 任一路径不存在、不可读或超过安全上限时保留配方并显示可恢复错误，不让 Host 恢复失败；
- 恢复旧快照时不假设两张文件内容未变化；重新比较后摘要记录本次实际尺寸与指标；
- 关闭 Document 时取消所有工作，停止闪烁并释放 Session 和 Bitmap。

## 4. V1 产品范围

### 4.1 必须完成

- 分别选择参考图和待比较图，并支持一键交换两者角色；
- 对尺寸相同的 PNG/JPEG 执行全分辨率、逐坐标比较；
- 尺寸不同时显示两边尺寸、宽高差和阻断原因，不计算任何像素对应指标；
- 左右并排、拖动分割线、透明叠加和可调间隔闪烁对比；
- 两图共享缩放、平移、准线和坐标，切换模式不丢失视口；
- 悬停显示两图同一原图坐标的 RGBA、Y、每通道有符号变化和绝对变化；
- RGB 绝对差异图，放大倍数固定支持 `1、2、4、8、16、32`；
- 以最大 RGB 差异或亮度差异生成固定量纲伪彩色热力图，并显示图例和饱和计数；
- 输出 PSNR-Y、PSNR-RGB、全局 SSIM-Y、MSE、MAE、RMSE、最大差异和变化像素比例；
- 输出 R、G、B、Y、Cb、Cr 的 256-bin 双直方图；
- 生成版本化 `ImageComparisonSummary`，Document、水印用例和后续实验可以消费同一结果类型；
- 复制摘要文本，并通过原子写入导出 UTF-8 JSON 摘要；
- 支持取消、失败状态、迟到结果保护、多 Document Scope 隔离、快照恢复和资源释放；
- 同步更新根 README、开发文档索引、公共图像领域边界、未来能力状态、用户指南、测试门禁和 G0–G7 记录。

### 4.2 明确不实现

- 自动缩放、裁剪、补边、旋转、透视校正、特征匹配或图像配准；
- 对尺寸不同的两图截取重叠左上角后静默计算；
- 局部 SSIM Map、MS-SSIM、Delta E 76/94/2000；
- 边缘、梯度、纹理、感知哈希、深度特征或语义相似度；
- HDR、浮点、16-bit、广色域、ICC Profile 或色彩管理；
- 把 JPEG 解码误差、透明 RGB 或色度子采样解释为篡改证据；
- 批量目录扫描、批量报告、历史数据库或比较工程文件；
- 在图片上绘制、修补、调色或保存修改后的图片；
- 自动给出“通过/失败”“相同/不同”“可接受/不可接受”的行业无关阈值；
- Windows CI、ZIP、正式发布、真实 Host 封板或市场级性能承诺。

## 5. SOLID 架构与依赖方向

### 5.1 分层

```text
Features/ImageCompareLab
  ImageCompareLabDocument          当前实例状态、命令、Revision 和生命周期
  ImageCompareLabView              纯布局、绑定与可访问文本
  ComparisonViewportControl        同步视口、裁剪、叠加、闪烁与 Pointer 转发
                 │
                 ▼
Application/ImageComparison
  IPrepareImageComparisonUseCase   解码、尺寸验证、全图分析和 Session 建立
  IProjectImageDifferenceUseCase   按参数生成有界差异/热力图
  IInspectImagePairUseCase         把原图坐标转换为像素对报告
  IExportComparisonSummaryUseCase  序列化并原子输出版本化摘要
                 │
                 ▼
Domain/Imaging + Domain/Comparison
  双图语义、指标、误差、直方图、差异投影、热力图和摘要
                 ▲
                 │
Infrastructure
  Avalonia 图片编解码、文件对话框、Bitmap 适配、剪贴板与原子文件写入
```

依赖只允许由上层指向下层抽象。Domain 不引用 Application、Feature 或 Infrastructure；应用用例不知道
Avalonia `Bitmap`；Document 不直接创建算法实现、JSON 选项、文件流或 `ServiceProvider`。

### 5.2 单一职责

- `ImagePairValidator`：只验证两图尺寸和比较前置条件；
- `FullReferenceQualityAnalyzer`：只做一次确定顺序的全图数值累计并返回指标；
- `ImageHistogramAnalyzer`：只累计六通道 256-bin 直方图；
- `ImageDifferenceProxyAnalyzer`：只把全分辨率绝对差异聚合成有界基础差异场；
- `ImageDifferenceProjector`：只把基础差异场按倍率着色成 RGB 显示图；
- `DifferenceHeatmapProjector`：只把固定量纲标量差异映射到伪彩色和图例；
- `ImagePairPixelInspector`：只返回一个坐标的像素对及变化；
- `ImageComparisonSummarySerializer`：只把稳定摘要 DTO 写成 JSON，不重新计算指标；
- 应用用例负责工作流，Document 负责 UI 状态，View/Control 负责展示与 Pointer 转发。

不得创建“万能 CompareService”“指标插件注册中心”“反射扫描器”或为了两个固定分支建立多层
Strategy/Abstract Factory。V1 使用普通构造注入、值对象、枚举 `switch` 和少量真实端口即可。

### 5.3 开闭与替换边界

- V1 的指标集合是固定产品协议，不开放运行时任意注册；
- 后续局部 SSIM、Delta E 或对齐应以新领域组件加入，再由应用层显式组合；
- 不要求所有分析器实现一个包含所有方法的大接口；纯领域算法优先使用具体无状态类；
- 只有应用用例、编解码、对话框、剪贴板和文件写入这些真实可替换边界建立接口；
- 测试通过窄用例接口注入替身，不需要继承 Document 或算法类；
- 当前水印用例继续获得兼容的 `ImageQualityMetrics`，不因比较实验室破坏调用方。

### 5.4 接口隔离

图片选择、报告保存和剪贴板必须是不同意图：

```csharp
internal interface IComparisonReportFileDialog
{
    Task<string?> PickSummaryOutputAsync(
        string suggestedName,
        CancellationToken cancellationToken);
}

internal interface ITextClipboard
{
    Task<bool> TrySetTextAsync(string text, CancellationToken cancellationToken);
}
```

现有 Avalonia 适配器可以实现多个窄接口，但比较 Document 不应因此依赖 Payload 选择、图片输出或 Host 的
`FilePickerSaveOptions`。若实现期确认现有公开窗口交互已经足够，适配细节留在 Infrastructure，不扩大应用端口。

### 5.5 应用用例契约

建议冻结四个窄用例：

```csharp
internal interface IPrepareImageComparisonUseCase
{
    Task<ImageComparisonResult> ExecuteAsync(
        ImageComparisonRequest request,
        CancellationToken cancellationToken);
}

internal interface IProjectImageDifferenceUseCase
{
    DifferenceProjectionResult Execute(
        ImageComparisonSession session,
        DifferenceProjectionOptions options,
        CancellationToken cancellationToken);
}

internal interface IInspectImagePairUseCase
{
    ImagePairPixelReport Execute(
        ImageComparisonSession session,
        ImagePoint sourcePoint);
}

internal interface IExportComparisonSummaryUseCase
{
    Task ExecuteAsync(
        ImageComparisonSummary summary,
        string targetPath,
        CancellationToken cancellationToken);
}
```

`ImageComparisonSession` 是 Document 私有、可释放的结果所有者。它保存两张原图、两张显示代理、坐标映射、
指标、直方图和摘要，不暴露可写 RGBA。投影用例只读取代理，不能修改全图或 Session。

## 6. 双图、颜色与像素语义

### 6.1 参考图与待比较图

- 第一张称为“参考图（Reference）”，第二张称为“待比较图（Candidate）”；
- 指标数学上多数对称，但摘要字段、悬停变化和报告文字固定使用 `Candidate - Reference`；
- “交换图片”交换路径与角色并重新比较，不只是交换两个 UI 标签；
- 两图均以编解码器输出的未预乘 RGBA8888 作为领域事实；
- 文件扩展名、编码格式和元数据不进入像素质量指标；V1 比较的是解码后像素，不比较文件字节。

### 6.2 尺寸规则

- 只有 `reference.Size == candidate.Size` 时才能建立有效 Session；
- 尺寸不同时返回结构化 `ImagePairMismatch`，包含两边宽高、宽高差和 `SizeMismatch` 原因；
- UI 保留两张独立预览供人工观察，但指标、悬停对应、差异、热力图和直方图对比入口必须禁用；
- 禁止默认拉伸、等比缩放、裁剪到交集或补透明边；
- 警告文字必须明确“尚未执行对齐或缩放，因此没有生成可比较指标”；
- 后续对齐设计应把变换矩阵、插值核、边界模式和有效区域写入摘要。

### 6.3 颜色公式

RGB 使用解码后的 8-bit R/G/B。Y、Cb、Cr 复用当前冻结公式：

```text
Y  =  0.299000 R + 0.587000 G + 0.114000 B
Cb = 128.000000 - 0.168736 R - 0.331264 G + 0.500000 B
Cr = 128.000000 + 0.500000 R - 0.418688 G - 0.081312 B
```

- 公式结果用于指标时保留 `double`，用于 256-bin 直方图时四舍五入并裁切到 `[0,255]`；
- V1 不宣称这些值经过 ICC、sRGB 线性化或 CIE 色彩管理；
- 透明像素的 RGB 仍按解码后的未预乘值参与 RGB/Y/Cb/Cr 比较；
- UI 与用户指南必须提醒：完全透明区域仍可能包含不同 RGB 字节。

### 6.4 Alpha 规则

- PSNR-Y、PSNR-RGB、全局 SSIM-Y、RGB 差异和六通道直方图不把 Alpha 当作颜色样本；
- 摘要单独输出 Alpha MAE、最大 Alpha 差异和 Alpha 变化像素数；
- 悬停同时显示两边 A 值、`ΔA` 和 `|ΔA|`；
- 差异图输出自身 Alpha 固定为 255，确保颜色差异可见；
- 若仅 Alpha 不同，颜色指标可显示完全一致，但摘要和状态必须明确存在 Alpha 变化。

### 6.5 坐标与变化符号

- 原图左上角为 `(0,0)`，x 向右、y 向下；
- `ΔR/ΔG/ΔB/ΔA/ΔY = Candidate - Reference`；
- 绝对差异使用 `|Δ|`；
- Pointer 位于 Uniform 图片黑边、边界外或 Session 失效时不返回伪造坐标；
- 最右和最下合法 Pointer 映射到 `width - 1`、`height - 1`，不能越界；
- 并排模式两块视口都必须映射到同一原图坐标，不能按各自控件像素直接比较。

## 7. 全参考质量指标

### 7.1 名称与单位

V1 固定输出：

| 字段 | 定义 | 单位/范围 |
| --- | --- | --- |
| `PsnrLumaDb` | Y 通道 MSE 对峰值 255 的 PSNR | dB，完全一致为 `+∞` |
| `PsnrRgbDb` | R/G/B 共 `3N` 样本 MSE 对峰值 255 的 PSNR | dB，完全一致为 `+∞` |
| `GlobalSsimLuma` | 全图 Y 的均值、样本方差与协方差公式 | `[-1,1]` |
| `MeanAbsoluteErrorRgb` | R/G/B 共 `3N` 样本绝对误差平均 | `[0,255]` |
| `RootMeanSquareErrorRgb` | R/G/B 共 `3N` 样本均方根误差 | `[0,255]` |
| `MaximumAbsoluteErrorRgb` | 全部 RGB 样本最大绝对误差 | `[0,255]` |
| `ChangedPixelCountRgb` | 至少一个 RGB 通道字节不同的像素数 | `0..N` |
| `ChangedPixelRatioRgb` | `ChangedPixelCountRgb / N` | `[0,1]` |

界面不得把 `GlobalSsimLuma` 简写成“标准 SSIM Map 平均”。它延续当前项目的全局结构统计语义，便于水印回归，
但和常见 11×11 高斯窗口实现的数值不可直接互换。

### 7.2 PSNR

```text
MSE-Y   = Σ(Yc - Yr)² / N
PSNR-Y  = 10 log10(255² / MSE-Y)

MSE-RGB  = Σ[(Rc-Rr)² + (Gc-Gr)² + (Bc-Br)²] / (3N)
PSNR-RGB = 10 log10(255² / MSE-RGB)
```

- MSE 为 0 时返回 `double.PositiveInfinity`，UI 显示 `∞`，JSON 输出使用 `null` 加 `isInfinite: true` 或稳定字符串，
  不能生成不合法 JSON 数字；
- 计算使用 `double`，字节先提升后相减，避免溢出；
- 不对透明像素加权，不忽略完全透明像素；
- 任何阈值都不是 V1 协议的一部分，调用方不能把 20 dB 等水印测试门禁冒充通用质量标准。

### 7.3 全局 SSIM-Y

沿用当前项目参数：

```text
C1 = (0.01 × 255)²
C2 = (0.03 × 255)²

SSIM = ((2 μr μc + C1) (2 σrc + C2)) /
       ((μr² + μc² + C1) (σr² + σc² + C2))
```

- `μr/μc` 是全图 Y 均值；
- `σr²/σc²/σrc` 使用样本分母 `max(1, N - 1)`，与当前实现保持兼容；
- 最终值因浮点舍入裁切到 `[-1,1]`；
- 单像素、定值图、完全一致图和反向亮度图必须有明确测试；
- 实现应采用稳定的在线均值/协方差累计或等价稳定算法，禁止为两图分配全尺寸亮度数组；
- 若重构后和当前实现存在浮点尾差，必须用 Golden Vector 证明误差在冻结容限内，不能悄悄改公式。

### 7.4 单次扫描与确定性

- 全图质量、RGB/Alpha 误差和变化计数在一次按行、按列的确定顺序扫描中累计；
- 每行或固定安全步长检查取消，取消不得返回半完成指标；
- V1 不为了速度并行分块，避免不同合并顺序造成平台间尾数漂移；
- 领域分析器无可变实例状态，可以登记 singleton；
- 不缓存每像素差异或亮度数组；直方图可在同一扫描中累计，也可由职责独立的分析器再扫描一次；
- 优先保持职责和可验证性，不用难以证明的 SIMD 或 unsafe 代码炫技。

## 8. 像素检查、差异图与热力图

### 8.1 像素对报告

`ImagePairPixelReport` 至少包含：

- 原图坐标 x/y；
- 参考图 RGBA 与 Y；
- 待比较图 RGBA 与 Y；
- `ΔR/ΔG/ΔB/ΔA/ΔY`；
- 每通道绝对变化；
- `MaximumRgbDifference`；
- 当前像素是否只有 Alpha 变化；
- 当前坐标是否处于有效 Session。

报告是不可变值对象，不包含 Bitmap、PointerEventArgs 或控件尺寸。UI 只负责格式化和显示。

### 8.2 RGB 绝对差异图

```text
output.R = clamp(|Rc - Rr| × amplification, 0, 255)
output.G = clamp(|Gc - Gr| × amplification, 0, 255)
output.B = clamp(|Bc - Br| × amplification, 0, 255)
output.A = 255
```

- 放大倍数只允许 `1、2、4、8、16、32`，默认 4；
- UI 必须把倍率显示为结果解释的一部分；
- 输出仅用于观察，不参与 PSNR/SSIM 或摘要质量判定；
- 基础差异场必须先在原分辨率逐像素计算绝对差异，再按目标格做面积平均；禁止先分别缩小两图再相减，
  因为不同方向的颜色变化可能在缩放时互相抵消；
- 着色投影读取有界基础差异场，不为 16M 像素图片长期保留第三张全尺寸 RGBA；
- 原图和代理源对象始终不可变；
- 改变倍率只重做小型投影，不重新解码或计算全图指标。

### 8.3 固定量纲伪彩色热力图

V1 提供两个标量来源：

```text
MaxRgb = max(|ΔR|, |ΔG|, |ΔB|)
Luma   = |Yc - Yr|
```

基础热力标量同样先在原分辨率逐像素计算，再按目标格做面积平均。着色时计算
`scaled = clamp(value × amplification, 0, 255)`，再通过一张固定、代码内显式定义的 256 项
感知连续色表映射到 RGB。颜色表不得按每张图自动归一化，否则相同颜色在不同比较中将失去统一含义。

- 图例显示原始差异值与当前倍率的对应关系；
- 当前状态和导出的应用层 Report 记录标量来源、倍率和被裁切到 255 的代理像素数；
- 色表应兼顾常见色觉差异，并用文字/数值图例补充，不能只靠颜色传递信息；
- 色表端点、关键采样点、单调亮度趋势和 256 项长度必须有测试；
- V1 不引入外部绘图库或颜色映射包。

### 8.4 代理与全图的区别

- 指标、误差统计和直方图基于完整解码尺寸；
- 并排、分割、叠加和闪烁使用最大边 1024 的同尺寸面积平均显示代理；差异和热力图使用同网格的基础差异场；
- 两张显示代理必须由相同目标尺寸和相同算法生成；差异基础场使用同一目标网格，但执行“先差异、后聚合”；
- UI 同时显示“原图比较尺寸”和“显示代理尺寸”；
- 悬停坐标先映射回原图，再读取两张完整 `PixelImage`，不得读取代理像素冒充原始值；
- 摘要不得把代理热力图的裁切计数描述成全图计数。

## 9. 六通道直方图

### 9.1 通道与 bin

- R、G、B 直接使用 0–255 字节作为 bin；
- Y、Cb、Cr 使用第 6.3 节公式，四舍五入并裁切到 0–255；
- 每个通道分别保存参考图与待比较图各 256 个 `long` 计数；
- 每个直方图计数总和必须等于像素数；
- Alpha 不混入六通道直方图；若需要 Alpha 分布，应在后续需求中明确加入，不静默扩展。

### 9.2 显示

- 默认显示 Y，用户可切换 R/G/B/Y/Cb/Cr；
- 两条曲线或面积使用不同线型、标记和文字图例，不能只靠红/绿颜色区分；
- y 轴支持线性和 `log10(count + 1)` 观察模式，但摘要始终保存原始计数；
- Hover 显示 bin、参考计数、待比较计数和差值；
- 切换通道和坐标显示不重新扫描图片；
- 绘制由轻量 Avalonia Control 完成，领域层不引用图表库。

### 9.3 统计边界

- 直方图只描述边际分布，不能证明空间结构相同；
- 两张像素排列完全不同但直方图相同是正常情况；
- UI 帮助和用户指南必须说明直方图与 SSIM/像素差异回答不同问题；
- V1 不输出 Earth Mover、Bhattacharyya、卡方或相关系数等直方图距离，除非以后单独冻结公式和用途。

## 10. 统一比较摘要

### 10.1 领域摘要

`ImageComparisonSummary` 是不依赖 UI、文件系统和 JSON 的不可变领域结果，至少包含：

- `AlgorithmId = "image-compare-v1"`；
- 参考与待比较图的像素尺寸，但不包含显示名或路径；
- 是否同尺寸及结构化不可比较原因；
- 颜色公式标识和 Alpha 参与规则；
- PSNR-Y、PSNR-RGB、全局 SSIM-Y；
- RGB/Alpha 的 MAE、RMSE、最大差异、变化像素数与比例；
- 六通道直方图。

文件显示名、来源实验、关联 ID 和比较完成时间属于应用元数据，由 `ImageComparisonReport` 包裹领域摘要；它们不进入
纯数值相等性。其他 Document 优先消费 `ImageComparisonSummary`，需要导出时再由应用层组装 Report，不能为了文件名
让 Domain 依赖路径或文件系统概念。

尺寸不匹配时摘要保留两边尺寸与结构化原因，指标和直方图使用明确的“未计算”可选状态，而不是 0、NaN 或空数组。

V1 的统一摘要用于“同一套计算事实被多个 Document 复用”，不是为了创建运行时指标框架。水印写入用例可以逐步
从同一分析器取得兼容的 PSNR-Y/全局 SSIM-Y，但不强制在 G1 立即修改水印 UI。

### 10.2 JSON schema

- `ImageComparisonReport` 文件 schema 从 `1` 开始，并同时写入领域摘要的 `algorithmId`；
- 字段使用稳定英文 camelCase，显示文字不作为协议；
- UTF-8、两空格缩进、固定属性顺序，便于评审和 Golden File；
- 非有限浮点不得直接交给默认 JSON 数字；完全一致时使用结构化 `{ "value": null, "isInfinite": true }`
  或经 G0 冻结的等价表示；
- 默认只写文件名，不写完整本机绝对路径；用户显式要求诊断路径时应作为后续独立选项；
- 不写图片像素、缩略图、密码、水印 Payload、异常堆栈或操作系统用户目录；
- 写入通过 `IAtomicFileWriter`，取消或失败不得留下半文件；
- V1 只保证本项目读取 schema 1；首次发布后任何破坏性变化必须增加 schema 并保留旧读路径。

### 10.3 人类可读摘要

- 复制文本包含两图名称/尺寸、核心指标、变化比例、Alpha 提示和算法标识；
- `∞` 必须附“像素误差为 0”的文字，不能只显示符号；
- 全局 SSIM 必须带“全局 Y”限定；
- 尺寸不同时输出“未比较”及原因，不能填 0、NaN 或空白冒充结果；
- V1 不输出未经数据集验证的等级、颜色徽章或通过阈值。

## 11. 视觉比较与同步视口

### 11.1 显示模式

- **并排**：左右各显示一张图，共享缩放、视口中心和准线；
- **分割**：同一视口叠放两图，垂直分割线左侧参考、右侧待比较，分割比例 `[0,1]`；
- **叠加**：参考图在下，待比较图按 `[0,1]` 透明度混合，默认 0.5；
- **闪烁**：同一位置交替显示两图，间隔 250–2000 ms，默认 500 ms；
- **RGB 差异**：显示当前倍率的差异代理；
- **热力图**：显示当前来源、倍率和固定图例的伪彩色代理。

模式只改变展示，不重新解码、计算指标或直方图。分割线、叠加和闪烁必须复用同一对显示代理与同一变换，
避免像素边界错位。

### 11.2 缩放与平移

- 缩放范围建议 25%–1600%，100% 的语义在 G0 冻结为代理像素 1:1；
- 滚轮以 Pointer 所在原图坐标为锚点缩放；
- 拖动平移更新共享视口中心；
- “适应窗口”和“100%”是显式命令；
- 并排模式中任一面板操作都更新两边视口；
- 视口中心使用归一化原图坐标保存，恢复到不同窗口尺寸时仍稳定；
- 所有裁切和坐标换算集中在可测试的 `ComparisonViewportMapper`，不在多个事件处理器重复公式。

### 11.3 闪烁与动画

- 闪烁只在 Document 可见、Session 有效且模式为闪烁时运行；
- 离开模式、关闭 Document 或应用进入关闭流程时立即停止计时器；
- 计时器只更新显示帧，不推进 Revision、不触发新分析、不持有 Session 之外的大对象；
- Headless 测试不依赖真实时间等待，计时状态通过可控 tick 或纯状态转换测试；
- 250 ms 下限避免无意义高频刷新，也降低光敏风险；UI 提供“暂停闪烁”；
- 用户指南明确闪烁可能引起不适，默认不自动启动。

### 11.4 View 与代码隐藏边界

- AXAML 负责布局、样式、编译绑定和可访问名称；
- `ComparisonViewportControl` 负责裁剪、叠放、准线和当前帧绘制，不读取文件或执行指标；
- code-behind 只把 Pointer/滚轮/拖动转换为归一化输入并转发给 Document 或控件状态；
- code-behind 不解码图片、不创建报告、不管理取消源、不修改 Domain；
- 直方图 Control 只消费已计算 bin；
- 所有绑定使用 `x:DataType`，保持编译绑定门禁。

## 12. 应用工作流、生命周期与资源预算

### 12.1 比较链

```text
验证两个路径非空且不同操作未在进行
    ↓
依次解码参考图与待比较图
    ↓
分别生成用于尺寸警告的有界基础预览
    ↓
验证尺寸；不一致时返回基础预览和结构化失败，并释放两张全图
    ↓
尺寸一致时生成同目标尺寸、最大边 1024 的面积平均显示代理
    ↓
执行全分辨率质量/误差扫描
    ↓
执行六通道直方图累计
    ↓
从全分辨率像素生成有界基础差异场
    ↓
从基础差异场生成默认 RGB ×4 差异投影
    ↓
建立只读 ImageComparisonSession 和统一摘要
    ↓
Document 在 generation 仍有效时一次性替换旧 Session 与 Bitmap
```

解码默认依次执行，不并行解码两张最大图，以控制峰值内存。算法可在 `Task.Run` 中执行 CPU 密集工作，但端口调用、
异常转换和结果提交仍由应用用例/Document 清晰分工。

### 12.2 资源预算

- 单张 `PixelImage` 最大约 61 MiB RGBA，两张长期原图最大约 122 MiB；
- 两张 1024 最大边 RGBA 代理合计不超过约 8 MiB；
- 有界基础差异场保存 RGB、MaxRGB 和 Y 五个 byte 标量，每像素约 5 字节，1024² 时约 5 MiB；
- 当前活动的差异或热力图 RGBA 投影不超过约 4 MiB；
- 六通道双直方图仅 `12 × 256 × sizeof(long)`，可忽略不计；
- 全图统计只保留常数个 `double/long` 累加器，不建立全尺寸亮度、差异或 Lab 数组；
- Session 长期结构目标不超过“两张全图 + 两张显示代理 + 一份有界基础差异场 + 小型统计”；
- 替换 Session 时先取消旧操作，再一次性切断旧 Session 和 Bitmap 引用；
- 测试验证缓冲区数量、最大代理尺寸和无全尺寸 `double[]` 的结构事实，不用不稳定的进程峰值断言。

### 12.3 取消与迟到结果

- 选择新路径、交换图片、重新比较或关闭 Document 时取消旧操作；
- 每次比较和投影携带递增 generation；
- 只有 generation、两个路径和当前 Session 身份全部一致时才能提交；
- 即使测试替身故意忽略取消，迟到结果也不能覆盖新状态；
- 取消保留上一份仍然有效的结果；若路径已经变化，旧结果立即标记失效并禁用导出；
- 长循环每行或固定步长检查取消；
- `OperationCanceledException` 显示“已取消”，其他已知输入错误显示中文可恢复信息，异常堆栈只进入开发诊断。

### 12.4 投影缓存

- Session 缓存一份无倍率的基础差异场，Document 只持有当前活动的差异或热力图投影；
- 用户改变倍率或来源时只从基础差异场生成新投影，完成后原子替换对应 Bitmap；
- 连续拖动或按键使用约 100–150 ms 防抖；
- V1 只有有限倍率和两个热力来源，不建立无限字典缓存；
- 切换回已丢弃参数时重新着色有界基础差异场，成本可控；
- Document Dispose 后任何投影不得访问已释放 Session。

## 13. 界面与交互设计

### 13.1 总体布局

```text
┌────────────────────────────────────────────────────────────────────────────────────┐
│ 参考图 [选择] | 待比较图 [选择] | [交换] | [比较] [取消] | [复制摘要] [导出 JSON] │
├──────────────────────────────────────────────────────────────┬─────────────────────┤
│ 模式：并排 / 分割 / 叠加 / 闪烁 / RGB 差异 / 热力图         │ 指标摘要            │
│ 缩放：适应 / 100% / - / +   分割/透明度/闪烁/倍率参数        │ PSNR-Y / PSNR-RGB   │
├───────────────────────────────┬──────────────────────────────┤ 全局 SSIM-Y         │
│ 参考图或组合视口              │ 待比较图或组合视口           │ MAE/RMSE/最大差异   │
│ 同步缩放、平移、准线          │ 同步缩放、平移、准线         │ 变化像素/Alpha提示  │
├───────────────────────────────┴──────────────────────────────┼─────────────────────┤
│ R/G/B/Y/Cb/Cr 双直方图；bin 悬停与线性/log 显示             │ 像素对              │
│                                                              │ RGBA/Y/Δ/|Δ|        │
├──────────────────────────────────────────────────────────────┴─────────────────────┤
│ 状态、两图尺寸、显示代理尺寸、当前模式与倍率、进度、尺寸警告                      │
└────────────────────────────────────────────────────────────────────────────────────┘
```

窄窗口时允许指标和像素侧栏移动到底部，不能依赖固定宽度导致核心命令不可达。

### 13.2 交互状态

- 未选择两张图片时只开放相应“选择”命令；
- 路径齐全后开放“比较”，比较期间开放“取消”；
- 选择或交换路径后立即使旧摘要、差异和导出资格失效；
- 尺寸不同时仍显示基础预览和警告，但隐藏或禁用会制造伪对应的模式；
- Session 有效后开放模式、视口、直方图、悬停、复制和导出；
- 切换显示模式、直方图通道和侧栏不重新分析；
- 改变差异倍率/热力来源只重新投影代理；
- 导出只允许当前有效摘要，文件对话框取消不算错误；
- 错误消息使用中文并给出下一步，不向用户显示内部类型名和堆栈。

### 13.3 可访问性

- 所有图标按钮同时提供中文可访问名称和 tooltip；
- 参考图、待比较图、差异图和热力图有明确文本标签；
- 分割线、透明度、倍率和闪烁间隔可用键盘调整并显示数值；
- 指标、像素和直方图均提供文本等价信息；
- 颜色图例包含数值刻度，直方图曲线使用线型/标记和文字区分；
- 闪烁默认关闭，支持暂停，说明可能的视觉不适；
- Pointer 不可用时，允许通过 x/y 数值输入检查像素。

## 14. G0–G7 实施包

### G0：产品、数值与资源基线

目标：在生产代码之前冻结所有会影响结果解释、兼容性和资源的事实。

交付：

- 审阅并冻结本文；
- 创建 `docs/design/image-compare-lab/history/README.md` 和 G0 记录；
- 冻结参考/待比较、尺寸阻断、颜色、Alpha、坐标和变化符号；
- 冻结 PSNR-Y、PSNR-RGB、全局 SSIM-Y、误差、直方图和热力图公式；
- 冻结 1024 显示代理、全图统计和最大长期缓冲预算；
- 冻结 Document ID、快照 schema、摘要 schema 和非有限浮点表示；
- 记录 V2 独立设计项、不使用 AIFLOW、不增加 Windows CI 和不执行发布门禁。

门禁：本文中的公式、默认值、范围、错误语义和延期项无未决选择。

### G1：双图领域与质量基础

目标：在不依赖 UI、文件和 JSON 的前提下得到稳定、低内存、可取消的全图比较结果。

交付：

- `ImagePairValidator`、比较请求值对象和结构化尺寸错误；
- `FullReferenceQualityAnalyzer` 与详细指标值对象；
- 用在线统计替换当前两份全尺寸 `LumaPlane` 的质量计算路径；
- 为水印保留兼容 `ImageQualityMetrics` 映射；
- RGB/Alpha 误差、变化计数和源对象不可变；
- 当前实现与新实现的固定语料回归 Golden Vector。

门禁：指标公式、完全一致、定值、单像素、透明 RGB、Alpha-only、取消和尺寸错误测试全部通过；现有 68 项测试无回归。

### G2：差异、热力图、像素与直方图

目标：完成所有不涉及文件和 Document 的可视分析领域组件。

交付：

- 原图坐标的 `ImagePairPixelReport`；
- RGB 六档放大差异投影；
- MaxRGB/Y 两种固定量纲伪彩色热力图与图例；
- 六通道双 256-bin 直方图；
- 1024 同尺寸面积平均双代理规则；
- 投影饱和计数、颜色表、边界和取消测试。

门禁：所有投影尺寸、像素、Alpha、倍率、颜色表、直方图守恒和源对象不变门禁通过。

### G3：用例、Session 与统一摘要

目标：用窄应用用例完成从两个文件到稳定比较结果的工作流。

交付：

- `ImageComparisonRequest/Result/Session`；
- 准备、投影、像素检查和摘要导出四个用例；
- 版本化 `ImageComparisonSummary` 及 JSON schema 1；
- 非有限 PSNR 的合法 JSON 表示；
- 独立报告对话框、剪贴板和原子写入端口；
- 解码顺序、异常转换、取消和 Session Dispose。

门禁：正式 PNG/JPEG 编解码回读、尺寸不符、取消、JSON Golden File、原子导出和隐私字段测试通过。

### G4：Document 与持久化生命周期

目标：把已验证用例接入真实 scoped Document，并保证多实例和迟到结果安全。

交付：

- `ImageCompareLabDocument` 的选择、交换、比较、取消、投影、复制和导出命令；
- schema 1 快照、Dirty/Revision 和显式恢复后比较；
- Session/Bitmap 所有权、关闭取消、generation 和迟到结果拒绝；
- 参数失效矩阵：路径、交换、模式、倍率、视口分别影响哪些结果；
- 缺失文件、尺寸不符、非法快照、剪贴板失败和导出失败状态。

门禁：Scope 隔离、快照、取消、关闭、迟到结果、参数失效和 Dispose 测试通过。

### G5：View 与同步交互

目标：完成可解释、可访问且不在 View 中承载业务算法的比较界面。

交付：

- 编译绑定的 `ImageCompareLabView`；
- 并排、分割、叠加、闪烁、差异和热力图模式；
- 共享缩放、平移、适应窗口、100%、准线和 x/y 输入；
- 直方图轻量 Control 与 bin 提示；
- 分割、叠加、闪烁、倍率、颜色图例和可访问文本；
- Uniform 黑边、边界和两面板统一坐标映射。

门禁：Headless View、模式状态、坐标映射、计时器停止、键盘交互和编译绑定测试通过。

### G6：组合、Standalone 与复用入口

目标：把第四个 Document 接入唯一真实组合根，并证明其他实验可消费统一摘要而不依赖其 UI。

交付：

- 新增稳定 `DocumentTypeId` 和 Module 第四个 Persistable Document 注册；
- 所有无状态领域算法 singleton、Document scoped、Session 实例私有；
- Standalone 第四个真实预览页，不复制业务实现；
- 组合根和多 Scope 自动测试；
- 水印用例的质量结果通过兼容映射复用新分析器，或记录明确的后续迁移决定；
- 比较摘要的应用层消费示例测试，不让其他 Feature 引用 `ImageCompareLabDocument`。

门禁：贡献顺序、零 Tool/零 Workflow、DI Scope、Standalone 解析和现有三个 Document 回归通过。

### G7：本地集成与开发封板

目标：完成当前非发布阶段能够诚实证明的全部质量工作。

交付：

- `--locked-mode` restore、Debug/Release build 和 test 全部通过；
- 更新根 README、`docs/README.md`、公共图像领域边界和未来能力状态；
- 新增图像比较实验室用户指南和测试门禁专用文档；
- G0–G7 实施记录填写真实测试总数、数值证据、内存结构、偏差和风险；
- Standalone 手工检查四种基础比较、差异、热力图、直方图、像素悬停、尺寸警告和 JSON 导出；
- 明确记录 Windows CI、ZIP、真实 Host、目标用户设备性能和发布封板延期。

门禁：所有文档只陈述真实执行过的结果，不以 Standalone 替代 Host 验收，不提前勾选未完成项。

## 15. 预计代码与文档落点

### 15.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Application/
│  ├─ ImageComparison/
│  │  ├─ ImageComparisonContracts.cs        请求、Session、结果和用例接口
│  │  ├─ ImageComparisonUseCases.cs         准备、投影和像素工作流
│  │  └─ ComparisonSummaryExportUseCase.cs  组装 Report 并原子输出 JSON
│  └─ Ports/
│     └─ ImageLabPorts.cs                    新增报告选择与文本剪贴板窄端口
├─ Constants/
│  └─ PluginIds.cs                           新增稳定 DocumentTypeId
├─ Domain/
│  ├─ Imaging/
│  │  ├─ ImageQualityCalculator.cs           保留水印兼容入口，移除全尺寸亮度临时数组
│  │  └─ ImageDifferenceProjector.cs         保留 RGB 着色，明确倍率与取消语义
│  └─ Comparison/
│     ├─ ImagePairModels.cs                  双图、像素对、尺寸错误和值对象
│     ├─ FullReferenceQualityAnalyzer.cs     PSNR/全局 SSIM/误差流式累计
│     ├─ ImageHistogramAnalyzer.cs           六通道双直方图
│     ├─ ImageDifferenceProxyAnalyzer.cs     先差异、后聚合的有界基础差异场
│     ├─ ImagePairPixelInspector.cs          原图坐标像素报告
│     ├─ DifferenceHeatmapProjector.cs       固定量纲伪彩色投影
│     └─ ImageComparisonSummary.cs           不含路径的可复用领域摘要
├─ Features/
│  └─ ImageCompareLab/
│     ├─ ImageCompareLabDocument.cs
│     ├─ ImageCompareLabView.axaml
│     ├─ ImageCompareLabView.axaml.cs
│     ├─ ComparisonViewportControl.cs
│     └─ ComparisonHistogramControl.cs
├─ Infrastructure/
│  ├─ Persistence/
│  │  └─ ImageComparisonSummarySerializer.cs
│  └─ Ui/
│     └─ AvaloniaImageLabFileDialog.cs        实现窄报告/剪贴板适配
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs
```

这是职责落点，不要求为了文件数量机械拆分。短小且紧密的值对象可以合并，但不得把领域扫描、文件工作流、
Document 状态和 Avalonia 绘制重新塞进同一个类。

### 15.2 测试

建议在现有测试项目中按职责新增：

- `ImageComparisonQualityTests`；
- `ImageDifferenceAndHeatmapTests`；
- `ImageHistogramTests`；
- `ImageComparisonUseCaseTests`；
- `ImageCompareLabDocumentTests`；
- `ImageCompareLabViewTests`；
- 对现有 `CompositionAndPersistenceTests`、编解码和水印流水线测试做增量扩展。

测试文件可以根据规模合并，但失败输出必须能够区分数值、投影、用例、Document、UI 和组合层。

### 15.3 专用文档

```text
docs/
├─ design/
│  └─ image-compare-lab/
│     ├─ README.md
│     ├─ implementation.md
│     ├─ testing.md
│     ├─ guide.md
│     ├─ user-manual.md
│     ├─ mathematical-principles.md
│     └─ history/
│        ├─ README.md
│        └─ g0-... 至 g7-...
├─ README.md
└─ future-capabilities.md
```

实施时还必须同步：

- `README.md`：当前产品能力和第四个 Document；
- `docs/README.md`：文档索引和当前贡献数；
- `docs/design/shared/image-domain-boundaries.md`：公共比较领域和摘要边界；
- `docs/future-capabilities.md`：基础 Image Compare Lab 从候选改为 V1 实施状态，后续高级指标仍保留候选；
- `docs/design/shared/deployment-and-release.md`：只在将来发布阶段更新真实 Host/ZIP 验收，不在本次开发阶段伪造结果。

## 16. 自动测试与质量门禁

### 16.1 Domain 与数值

- null、空路径、非法坐标、非法倍率和尺寸不匹配安全失败；
- 参考/待比较符号固定为 `Candidate - Reference`；
- 完全一致图：两种 MSE/MAE/RMSE 为 0、两种 PSNR 为 `+∞`、全局 SSIM-Y 为 1；
- 单像素和 2×2 手算 Golden Vector；
- 纯黑/纯白、定值偏移、单通道变化和最大 255 差异；
- 当前全局 SSIM 实现与流式实现的冻结语料容差；
- RGB PSNR 使用 `3N`，Y PSNR 使用 `N`，不得混用；
- Alpha-only 变化不影响颜色指标，但 Alpha 统计准确；
- 完全透明像素的 RGB 仍参与颜色指标；
- 取消不返回半成品，源 `PixelImage` 不被修改；
- 静态分析或结构测试证明不再创建两份全尺寸亮度 `double[]`。

### 16.2 差异、热力图和直方图

- RGB 差异的六档倍率、裁切、Alpha=255 和尺寸保持；
- 构造“分别缩小后会抵消”的双色小图，证明基础差异场执行先差异、后聚合且结果非零；
- MaxRGB/Y 标量来源的手算像素；
- 固定色表长度、端点、关键点和确定性；
- 热力图不能按输入自动归一化；
- 代理最大边 1024、纵横比一致、两图代理尺寸一致；
- 代理坐标正确回映原图首/末像素；
- R/G/B/Y/Cb/Cr bin 公式和舍入规则；
- 每幅每通道 256 bin 总和等于像素数；
- 直方图切换和 log 显示不改变原始计数；
- 仅修改倍率不重新执行全图质量或直方图扫描。

### 16.3 用例、摘要与文件

- 解码顺序和任一步骤取消；
- 第二张解码失败时第一张不会泄漏到有效 Session；
- 尺寸不匹配返回结构化原因且没有指标；
- Session Dispose 后拒绝像素和投影访问；
- JSON schema 1 字段、顺序、UTF-8 和 Golden File；
- `+∞` PSNR 生成合法 JSON；
- 默认摘要不包含绝对路径、像素、Payload 或堆栈；
- 原子写入成功、替换、失败和取消不留临时文件；
- 复制摘要失败可恢复，不清除有效结果。

### 16.4 Document、UI 与组合

- Module 贡献顺序固定为四个 Persistable Document、零个普通 Document、零个 Tool；
- 不登记 Workflow Action、Workbench Command 或 AIFLOW；
- 不同 Scope 的双图、Session、视口、闪烁和取消互不影响；
- 快照只保存轻量配方，schema 1 可往返，恢复不自动比较；
- 非法或未知快照值安全回退；
- 路径或交换使旧结果失效，模式切换不重新分析；
- 关闭取消比较/投影、停止闪烁并拒绝迟到结果；
- Headless 环境加载第四个 View 和两个轻量 Control；
- 黑边、面板边界、最后像素、并排双面板和分割线坐标映射；
- 键盘可操作分割、透明度、倍率和像素坐标；
- 导出只在摘要属于当前路径和 generation 时开放；
- Standalone 复用真实 Module/DI，不复制比较实现。

### 16.5 回归与资源

- 当前 68 个测试必须全部继续通过；
- 水印三种 Profile、PNG/JPEG 回读、PSNR/SSIM 阈值、DCT-QIM 和协议 Golden Vector 不得变化；
- 频域分析器的通道、代理、FFT、重建、快照和 View 测试不得变化；
- 最大图像长期只保留两张全图，所有可视投影最大边 1024；
- 取消检查位于解码边界、行扫描、直方图和投影长循环；
- 不使用机器相关的严格毫秒断言作为单元测试门禁；
- 最大尺寸耗时和结构化内存证据在 G7 本地记录中据实填写，不写市场承诺；
- 不以单一覆盖率百分比替代关键分支和 Golden Vector；若后续引入覆盖率工具，必须单独记录依赖与稳定阈值。

### 16.6 本地回归命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

测试总数只能在实现完成后更新到测试文档。不得为了达到预期数量拆分无意义测试，不得删除回归断言，也不得通过
放宽数值容差掩盖算法变化。

当前阶段明确不增加 GitHub Actions/Azure Pipelines 等 Windows CI，不执行 ZIP、真实 Host、安装、卸载、布局恢复
或发布门禁。Release 只表示本地 `Release` 编译配置回归，不等于正式发布。

## 17. 人工验收场景

### 17.1 基础比较

1. 选择同一张 PNG 作为两边输入，确认两种 PSNR 为 `∞`、全局 SSIM-Y 为 1、差异全黑；
2. 选择 JPEG 重编码结果，确认指标有限且摘要明确比较的是解码像素；
3. 交换参考图和待比较图，确认有符号像素变化反号、绝对指标保持；
4. 修改任一路径后确认旧摘要、复制和导出立即失效；
5. 同时打开两个比较实例，确认路径、视口、模式、闪烁和取消完全隔离。

### 17.2 视觉模式与坐标

1. 在并排、分割、叠加和闪烁间切换，确认图像位置不跳动；
2. 缩放到 1600% 并平移，确认两边同一像素持续对齐；
3. 拖动分割线到 0%、50%、100%，确认裁剪边界正确；
4. 调整叠加透明度和闪烁间隔，确认状态数值、暂停与关闭停止正确；
5. 在四角、边界、黑边和透明区域悬停，核对 RGBA/Y/Δ 数值；
6. 使用键盘完成同等的分割、倍率和 x/y 像素检查。

### 17.3 差异、热力图与直方图

1. 对只改变 R、G、B、亮度和 Alpha 的小图分别检查差异显示；
2. 切换 1–32 倍，确认图例、状态和饱和提示同步；
3. 比较 MaxRGB 与 Y 热力图，确认颜色含义固定且不随图片自动归一；
4. 切换六通道及线性/log 直方图，核对已知颜色块的 bin；
5. 只改变 Alpha 时确认 RGB 差异全黑，但 Alpha 摘要和悬停明确提示变化。

### 17.4 尺寸错误与输出

1. 选择尺寸不同图片，确认两边尺寸和差值清晰显示，指标/差异/悬停对应被阻断；
2. 确认应用没有静默缩放、裁剪或对齐；
3. 导出普通比较与完全一致比较摘要，确认 JSON 合法且不包含绝对路径；
4. 取消保存、模拟写入失败并重试，确认内存结果仍在且无临时文件；
5. 恢复快照后确认不会自动读取旧文件，用户点击“比较”后才产生新摘要。

### 17.5 Standalone 边界

1. Standalone 显示第四个真实 Document，不是复制的演示 ViewModel；
2. 关闭 Standalone 页面时比较取消、闪烁停止、Session 和 Bitmap 可释放；
3. Standalone 只证明本地插件对象图与窗口交互，不宣称真实 Host、Dock、ZIP 或卸载已经验收。

## 18. 兼容、迁移与回滚

### 18.1 兼容规则

- 三个既有 Document ID、快照 schema 和水印 V1 线格式不变；
- 新增图像比较实验室 ID 首次发布后不得更改；
- `ImageQualityCalculator.Compare` 对现有水印调用继续返回兼容的 Y-PSNR 与全局 Y-SSIM；
- 重构质量计算只允许在冻结浮点容差内变化，现有测试阈值和协议行为不得放宽；
- RGB PSNR、Alpha 统计和直方图是新增字段，不改变既有 `ImageQualityMetrics` 的含义；
- V1 不新增 NuGet，因此中央版本和插件私有依赖清单原则上不变；
- 快照 schema 与摘要 JSON schema 相互独立，不能混用版本号。

### 18.2 功能回滚顺序

若某阶段无法达到门禁，按以下顺序回滚，不保留半成品入口：

1. 隐藏尚未稳定的 UI 入口并移除第四个 Module 贡献；
2. 移除 Document、View 和应用用例；
3. 只有质量分析重构已通过现有水印与独立数值测试时才可保留；
4. 报告端口、JSON serializer 和 DI 注册必须整体回滚或整体完成；
5. 纯领域直方图/热力图只有具备独立测试且无调用泄漏时才可保留；
6. 不回退三个既有 Document、水印协议、频域分析器或已有测试门禁；
7. 文档如实记录未完成阶段、实际回滚内容和原因。

### 18.3 无旧数据迁移

V1 只新增 Document 和摘要 schema，尚无旧 Image Compare Lab 快照或报告需要迁移。开发期若修改 schema，可以
清理开发快照；首次发布后任何 schema 变化必须增加版本并保留旧版本恢复或给出明确不可恢复说明。

## 19. 注释与实施纪律

- 所有新增注释使用中文；
- 复杂类的 XML `<remarks>` 说明设计目的、指标语义、所有权、线程、取消和资源边界；
- PSNR、全局 SSIM、在线协方差、YCbCr、热力图、坐标映射和非有限 JSON 必须有公式或设计说明；
- 注释重点解释“为什么这样设计、单位是什么、哪些输入不参与”，不逐行翻译循环和赋值；
- 不给显而易见的属性访问器堆砌无价值注释；
- 不通过继承树、反射、服务定位器、运行时算法发现或多层 Strategy/Factory 炫技；
- 接口只建在真实替换边界和应用用例边界，不为每个值对象创建接口；
- 不在 Document 或 View 中写全图像素循环，不在 Domain 中创建 Avalonia Bitmap、JSON 或文件；
- 不使用静态可变缓存，不让一个 Document 访问另一个 Document 的 Session；
- 不吞掉非取消异常，不把内部异常堆栈直接展示给用户；
- 不把“同尺寸”检查散落到每个 UI 命令，领域验证是唯一事实源；
- 每个 G 包先补失败测试，再实现能力，最后填写实际实施记录；
- 不使用 AIFLOW；
- 当前阶段不增加 Windows CI 和发布门禁。

## 20. V1 开发封板检查清单

以下复选框只能在对应证据真实完成后勾选。

### 产品与交互

- [x] 第四个贡献是 Persistable Document，不是 singleton Tool；
- [x] 参考/待比较、交换和尺寸阻断语义清晰；
- [x] 并排、分割、叠加和闪烁对比可用；
- [x] 同步缩放、平移、准线和像素悬停准确；
- [x] RGB 差异、两种热力图和六通道直方图可用；
- [x] 摘要复制和原子 JSON 导出可用；
- [x] UI 不输出未经验证的通用质量等级或阈值。

### SOLID 与生命周期

- [x] Domain、Application、Feature、Infrastructure 依赖方向正确；
- [x] Document 不直接执行像素扫描、图片解码或 JSON 写入；
- [x] 领域类职责窄，没有万能 CompareService 或算法注册中心；
- [x] 文件、剪贴板和应用用例接口满足接口隔离；
- [x] 多 Scope 状态完全隔离；
- [x] Session、投影和 Bitmap 所有权明确；
- [x] 取消、关闭、防抖、闪烁停止和迟到结果保护有测试；
- [x] 快照不包含大对象，恢复不自动比较。

### 数值与资源

- [x] PSNR-Y、PSNR-RGB、全局 SSIM-Y 和误差公式有 Golden Vector；
- [x] Alpha、透明 RGB、颜色公式和变化符号有门禁；
- [x] 质量计算不再分配两份全尺寸亮度数组；
- [x] 两张全图、两张显示代理、一份基础差异场和当前投影不突破结构预算；
- [x] 热力图固定量纲、颜色表和饱和提示正确；
- [x] 六通道直方图每个计数总和守恒；
- [x] 尺寸不同不会产生伪指标或隐式变换。

### 测试与文档

- [x] 现有 68 个测试全部保持通过；
- [x] 新增 Domain、投影、用例、Document、UI、组合和 JSON 测试；
- [x] Debug/Release 本地门禁通过；
- [x] 用户指南、测试门禁、文档索引、公共领域边界和未来能力已同步；
- [x] G0–G7 记录包含真实数据、偏差、风险和回滚；
- [x] 文档没有宣称已执行真实 Host、ZIP、Windows CI 或正式发布封板；
- [x] 局部 SSIM、MS-SSIM、Delta E、边缘/纹理和对齐仍处于独立后续设计，不混入 V1。
