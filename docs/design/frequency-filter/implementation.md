# ImageLabPlugin V1 Frequency Filter／频域滤波实施计划

> 计划状态：V1 本地开发实现完成；Debug/Release 自动门禁完成；发布门禁延期<br>
> 基线日期：2026-08-31<br>
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3<br>
> 起始自动基线：2026-08-31 实际复跑 locked restore；Debug/Release warn-as-error build 均 0 警告、0 错误；两配置 test 均 333/333 通过、0 失败、0 跳过<br>
> 核心路线：有界分析代理 + 共享二维 FFT/IFFT + 径向实数增益遮罩 + 原始 double 结果 + 副作用诊断 + 截断空间核对照<br>
> 首要规定：SOLID 优先；设计模式朴素使用；生产代码中文详细注释；先数值与资源门禁，后 Document 与 UI

> 实施结果（2026-08-31）：登记第十一个 Persistable Document；新增 29 个 runner 测试用例，Debug/Release 均为
> 362/362 通过、0 失败、0 跳过，warn-as-error 构建 0 警告、0 错误。实现未使用 AIFLOW，未新增 Windows CI，
> 未执行真实 Host、ZIP 或任何发布门禁。逐包证据见 `history/`，当前使用入口见 `guide.md` 与 `user-manual.md`。

本文定义 ImageLab 的第十项产品能力、计划中的第十一个 Persistable Document。它复用已经通过数值门禁的
`Fft1DTransform`、`Fft2DTransform`、`FrequencySpectrum`、六通道投影和空间卷积基础，但不直接把
`SpectrumInspectorDocument` 或 `ConvolutionPlaygroundDocument` 当作服务调用。

本文是实施时的唯一总计划。每个实施包完成后必须在对应 `history/gN-*.md` 中记录实际代码、测试、数值证据、
资源观察、偏差、遗留风险和回滚方式。任何“完成”结论都必须来自实际门禁，不能从本计划推断。

## 1. 决策摘要

### 1.1 产品形态

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Frequency Filter／频域滤波` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID | `myavalonia.plugin.image.lab.document.frequency-filter` |
| 显示分类 | `图像分析` |
| 输入 | 用户显式选择的一张 PNG/JPEG 图片 |
| 分析通道 | R、G、B、Y、Cb、Cr 六个单通道 |
| 滤波方向 | 低通、高通、带通、带阻 |
| 滤波家族 | Ideal、Butterworth、Gaussian |
| 实时处理 | 512/1024/2048 最大边分析代理；默认 1024 |
| 导出 | 与当前配方指纹一致的代理结果；原图能落入同一 FFT 预算时可显式执行原尺寸结果 |
| 空间对照 | 从遮罩 IFFT 得到冲激响应，截取 7/15/31 奇数核并以 Wrap 边界比较 |
| 模式使用 | 不需要算法 Strategy；使用不可变 Recipe/Result、窄用例和普通构造注入 |
| 外部依赖 | 不新增 NuGet、原生 FFT 或图表包 |
| 明确排除 | AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP、真实 Host 与发布门禁 |

### 1.2 用户闭环

```text
选择图片
    ↓
选择 R/G/B/Y/Cb/Cr 与 512/1024/2048 分析档位
    ↓
建立分析代理并缓存一次全局 FFT
    ↓
选择低通/高通/带通/带阻与 Ideal/Butterworth/Gaussian
    ↓
实时调整截止频率、Butterworth 阶数或 Gaussian 过渡参数
    ↓
联动观察原图、幅度谱、增益遮罩、IFFT 结果和差异/副作用
    ↓
查看过渡区、径向响应、冲激响应、Ringing/模糊/增强诊断
    ↓
生成 7/15/31 截断空间核，比较结果误差与本机实测耗时
    ↓
按需执行预算内原尺寸结果，并原子导出 PNG
```

### 1.3 固定实施顺序

1. G0 冻结滤波公式、坐标、增益、通道、显示和资源语义；
2. G1 建立不可变模型、校验和配方指纹；
3. G2 完成遮罩与径向响应数值核心；
4. G3 完成频谱乘法、IFFT 和通道重建；
5. G4 完成副作用分析与解释性投影；
6. G5 完成空间核派生和公平比较；
7. G6 完成 Application Session、取消和导出；
8. G7 接入 Persistable Document、快照和组合根；
9. G8 完成 UI、Standalone 和 Headless 交互证据；
10. G9 复跑 Debug/Release 全量本地门禁并同步全部文档。

不得先做界面再倒推公式，也不得为了演示效果把代理结果描述成原尺寸结果。

## 2. 当前项目事实与复用边界

### 2.1 已有能力

当前仓库已具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集与 `ImageLabPlugin.Standalone` 开发承载；
- 十个已登记 Persistable Document，当前不登记 Tool；
- `PixelImage`、`ImageSize`、64 MiB 编码输入上限和 16,000,000 像素上限；
- `ImageChannelConverter` 的 R/G/B/Y/Cb/Cr 抽取与单通道回写；
- `ImageAnalysisProxyProjector` 的有界抗混叠分析代理；
- `Fft1DTransform`、`Fft2DTransform` 与最多 `2048×2048` 的 `FrequencySpectrum`；
- `FrequencyCoordinates` 的中心化显示坐标和归一化径向频率；
- `SpectrumProjector` 的幅度/相位显示；
- `FrequencyBandMaskFactory` 的理想 0/1 环带能力；
- `SpatialConvolver`、`BorderIndexMapper` 和 3–31 奇数核卷积；
- `FullReferenceQualityAnalyzer`、差异投影、原子写入和 Avalonia 图片编解码；
- Document Scope、取消、generation 防迟到覆盖、轻量快照和 Headless View 测试惯例；
- 2026-08-31 实际复跑的 Debug/Release 333/333、0 跳过、0 警告基线。

### 2.2 应直接复用

- FFT/IFFT 实现、坐标转换、通道转换和分析代理不能复制第二份；
- 图片选择、解码、PNG 编码和原子写入继续依赖已有窄端口；
- 空间域比较继续复用 `SpatialConvolver` 的 raw double 结果和 `Wrap` 边界；
- PSNR/SSIM 等全参考指标继续复用 `FullReferenceQualityAnalyzer`；
- Document 生命周期继续沿用现有 generation + CancellationTokenSource 模式；
- Standalone 继续从真实 Module/DI 解析真实 Document 和 View。

### 2.3 不能错误复用

- 不能让新 Document 持有或调用 `SpectrumInspectorDocument`；Document 不是应用服务；
- 不能让新 Document 操作 `ConvolutionPlaygroundDocument` 的 UI 状态；
- 不能把只支持 byte 0/1 的 `FrequencyBandMaskFactory` 硬扩成包含全部滤波、重建、诊断和导出的万能类；
- 不能把 `KernelFrequencyResponseAnalyzer` 的固定 256² 核频响误当成输入图像的全局频谱；
- 不能为复用方便让 Domain 引用 Avalonia、文件系统、DI、Document 或 Host SDK；
- 不能改变既有 Spectrum Inspector 的 V1 参数、快照或导出语义。

### 2.4 计划中的共享重构

G2 前先评估将 `FrequencyBandMaskFactory` 保持原样，并新增通用的 `RadialFrequencyGrid` 或复用
`FrequencyCoordinates`。只有确有两个以上消费者时才提取共享坐标遍历；不为了“看起来抽象”建立频域框架。

新滤波能力需要 `double` 增益 `[0,1]`，现有 byte 遮罩仍服务 Spectrum Inspector。两者可以共用坐标，不能共用
可变数组或把旧 API 改成弱类型对象。

空间比较要求在 byte 舍入前取得 raw double。现有 `SpatialConvolver.Convolve` 会在返回 raw 的同时完成量化；G5 应先用
回归测试保护既有行为，再按单一职责最小提取 `ConvolveRaw`/`Quantize` 边界，使频域与空间计时都只覆盖对应数学核心。
这不是建立第二套卷积；Convolution Playground 继续通过原入口得到完全相同的 raw 与 byte 结果。

## 3. 产品范围

### 3.1 V1 必须完成

- Ideal、Butterworth、Gaussian 三类径向滤波器；
- 低通、高通、带通、带阻四种方向；
- 单截止或内/外截止的实时调整；
- Butterworth 阶数 1–12 实时调整；
- Gaussian 过渡宽度使用数学等价参数实时调整；
- 显示由 90% 到 10% 增益定义的实际过渡带；
- 原图、幅度谱、遮罩、IFFT 结果和差异联动；
- R/G/B/Y/Cb/Cr 六个单通道；
- 直接滤波信号、居中观察和叠加增强三种明确输出语义；
- raw double 最小/最大值、低/高越界、裁切、MAE、PSNR/SSIM 和梯度能量诊断；
- Ringing、模糊和边缘增强的图像、剖面与数值解释；
- 遮罩 IFFT 冲激响应、7/15/31 截断核与空间域近似结果；
- FFT 与空间近似的误差、参数、执行范围和本机实测耗时对照；
- 分析代理与预算内原尺寸执行明确区分；
- 可取消执行、迟到结果保护、多 Scope 隔离、快照与原子 PNG 导出；
- Debug/Release 全量自动门禁和专用文档集。

### 3.2 明确不实现

- 任意画笔、矩形、套索或自由频谱遮罩；
- Notch、周期噪声峰检测或自动去条纹；
- Chebyshev、Elliptic、Bessel、Raised Cosine 或用户脚本滤波器；
- 方向性、椭圆、扇区、Gabor 或可旋转滤波器；
- 复数相位遮罩、相位编辑或不满足实值共轭条件的变换；
- RGB 三通道并行实时 FFT；V1 一次只解释一个通道；
- 超过共享 `2048×2048` 补零预算的“伪原尺寸”处理；
- GPU、SIMD 专用分支、原生 FFT 库或跨 Document 全局缓存；
- 自动选择“最佳”滤波器、批处理、滤镜链或通用 DAG；
- 把空间截断核宣称为无限冲激响应的精确等价物；
- AIFLOW、Workflow Action、Workbench Command；
- Windows CI、ZIP、真实 Host、安装升级和发布门禁。

上述排除项分别属于 Frequency Mask Editor、Periodic Noise Removal、后续性能优化或发布阶段，不能在 V1 中顺手加入。

## 4. SOLID 架构

### 4.1 依赖方向

```text
Features/FrequencyFilter
  FrequencyFilterDocument       每实例状态、命令、Revision、取消和 Bitmap
  FrequencyFilterView           布局与绑定
  专用轻量 Control              径向响应、剖面和覆盖层绘制
                 │
                 ▼
Application/FrequencyFiltering
  PrepareSessionUseCase          解码、代理和缓存 FFT
  ApplyFilterUseCase             遮罩、IFFT、重建和诊断编排
  CompareSpatialUseCase          派生核与公平对照
  RenderFullResultUseCase        预算内原尺寸显式执行
  ExportImageUseCase             配方一致性与原子导出
                 │
                 ▼
Domain/FrequencyFiltering        Domain/Frequency + Domain/Imaging
  参数、增益遮罩、滤波执行、冲激响应、副作用分析
                 ▲
                 │
Infrastructure
  既有图片编解码、文件对话框和原子文件写入
```

依赖只允许从 Feature 指向 Application，再指向 Domain 抽象或已有 Infrastructure 端口。Domain 不知道
Avalonia、文件系统、JSON、DI、Document 或 Host；Application 不创建 `Bitmap`；View 不写公式和文件。

### 4.2 单一职责

| 类型 | 唯一职责 | 明确不负责 |
| --- | --- | --- |
| `FrequencyFilterRecipe` | 保存并验证稳定参数 | 生成遮罩、计时、UI 文本 |
| `RadialFilterResponse` | 计算一个半径处的实数增益 | 遍历图像、IFFT |
| `FrequencyFilterMaskFactory` | 从配方生成不可变增益遮罩 | 应用频谱、重建图像 |
| `FrequencyFilterEngine` | 逐频点乘增益并 IFFT | 解码、Bitmap、导出 |
| `FrequencySignalProjector` | 把 raw double 按明确模式映射为通道 | 生成遮罩 |
| `FrequencySideEffectAnalyzer` | 计算差异、越界、梯度和剖面诊断 | 修改结果 |
| `FrequencyImpulseResponseFactory` | 由遮罩生成实值冲激响应与截断核 | 执行空间卷积 |
| `FrequencySpatialComparator` | 在同一 padded 平面比较两条路径 | 决定 UI、保存文件 |
| `FrequencyFilterSession` | 拥有一次解码、代理、通道和缓存频谱 | 全局缓存、服务定位 |
| `FrequencyFilterDocument` | 管理当前实例状态和生命周期 | 数值循环和文件实现 |

禁止创建 `FrequencyFilterService` 一类同时负责参数、FFT、图片、Document、导出和解释的万能服务。

### 4.3 开闭原则与朴素设计

V1 的十二种组合是三种固定家族乘四种固定方向。实现采用：

- 一个经完整 `switch` 覆盖的纯响应计算器；
- 不可变 `Recipe` 与 `Result`；
- 普通 sealed 数值类；
- 窄应用用例接口；
- 构造注入和组合根显式登记。

本轮不使用 Strategy，因为三个公式没有运行时扩展、第三方注入或独立生命周期需求。将来如果确实增加多个可独立替换、
具有共同数值契约的滤波家族，再用测试驱动提取 `IFrequencyFilterResponse`。不得先建立反射目录、抽象工厂或插件内插件系统。

### 4.4 接口隔离

建议应用边界：

```csharp
internal interface IPrepareFrequencyFilterSessionUseCase
{
    Task<FrequencyFilterSession> ExecuteAsync(
        FrequencyFilterSessionRequest request,
        CancellationToken cancellationToken);
}

internal interface IApplyFrequencyFilterUseCase
{
    Task<FrequencyFilterResult> ExecuteAsync(
        FrequencyFilterSession session,
        FrequencyFilterRecipe recipe,
        CancellationToken cancellationToken);
}

internal interface ICompareFrequencySpatialUseCase
{
    Task<FrequencySpatialComparison> ExecuteAsync(
        FrequencyFilterSession session,
        FrequencyFilterRecipe recipe,
        int kernelSize,
        CancellationToken cancellationToken);
}

internal interface IRenderFullFrequencyFilterUseCase
{
    Task<FullFrequencyFilterResult> ExecuteAsync(
        FrequencyFilterSession session,
        FrequencyFilterRecipe recipe,
        CancellationToken cancellationToken);
}

internal interface IExportFrequencyFilterImageUseCase
{
    Task ExecuteAsync(
        FrequencyFilterExportRequest request,
        CancellationToken cancellationToken);
}
```

不把比较、完整尺寸处理和导出塞进 `IApplyFrequencyFilterUseCase`；不扩大 `IImageFileDialog` 为通用文件管理器。

### 4.5 SOLID 审查门禁

- SRP：任何类同时出现 FFT 循环、文件 IO 和 UI 状态即失败；
- OCP：增加新输出投影不应修改遮罩公式，增加新诊断不应修改 IFFT；
- LSP：用例实现必须遵守取消、不修改 Session 缓存、失败不返回半成品的契约；
- ISP：Document 只注入自己使用的五个用例和已有图片对话端口；
- DIP：Application 依赖图片编解码/写入抽象，Feature 不直接 `new` Infrastructure；
- 组合根测试必须证明算法为无状态 singleton，Document/Session 状态不跨 Scope 泄漏。

## 5. 数学协议

### 5.1 坐标与半径

继续复用 `FrequencyCoordinates`。内部频谱保持 FFT 自然顺序，界面以中心为 DC。归一化径向半径 `r` 固定为
`[0,1]`：中心为 0，频谱角点接近 1。所有遮罩、径向曲线、过渡区、频点提示和冲激响应必须使用同一坐标定义。

UI 同时显示：

- 归一化半径 `r`；
- 水平/垂直 cycles/pixel；
- 对当前代理尺寸换算后的 bin 坐标；
- 截止位置在径向曲线上的标记。

禁止在 XAML code-behind 或不同分析器里复制半径公式。

### 5.2 低通原型

令截止半径为 `c`，基础低通增益 `L(r)` 定义如下：

```text
Ideal:       L(r) = 1, r <= c
                  = 0, r >  c

Butterworth: L(r) = 1 / (1 + (r / c)^(2n))

Gaussian:    L(r) = exp(-ln(2) * (r / c)^2)
```

固定边界：

- `c` 必须位于 `(0,1]`；
- Butterworth `n` 为 1–12 的整数；
- `r=0` 时 Butterworth 直接返回 1，避免 `0/0`；
- Gaussian 用 `ln(2)` 使 `L(c)=0.5`；
- 所有结果必须有限并裁切到 `[0,1]`；
- Ideal 在 `r=c` 取 1，Golden 测试固定该边界；
- 计算高次幂前采用对数或受控分支，防止无穷值污染数组。

这里的 `H=0.5` 指遮罩振幅增益，不是功率的 -3 dB 定义。用户文档必须明确，不能混用电路滤波器术语。

### 5.3 四种滤波方向

基于低通原型：

```text
LowPass(r, c)       = L(r, c)
HighPass(r, c)      = 1 - L(r, c)
BandPass(r, a, b)   = HighPass(r, a) * LowPass(r, b), 0 < a < b <= 1
BandStop(r, a, b)   = 1 - BandPass(r, a, b)
```

这种定义保证同参数下高通与低通、带通与带阻逐点互补。平滑带通在内外过渡区可能达不到 1；界面必须显示真实径向
响应，不能画一个虚假的矩形通带。

### 5.4 截止、阶数与过渡带

V1 不提供三个彼此矛盾的独立参数。数学上：

- Ideal 没有有限过渡带，宽度固定为 0；阶数不适用；
- Butterworth 由截止 `c` 和阶数 `n` 决定陡峭程度；用户调整 `n` 时过渡带实时变化；
- Gaussian 由截止 `c` 决定平滑尺度；界面允许以“过渡宽度”编辑同一个尺度，并反算 `c`；阶数不适用；
- 带通/带阻有内、外两条边，各自显示自己的过渡区；两条过渡重叠时给出可见提示，不偷偷改参数。

过渡带统一定义为低通原型从 `H=0.9` 到 `H=0.1` 的半径区间：

```text
Butterworth: r(H) = c * ((1/H) - 1)^(1/(2n))
Gaussian:    r(H) = c * sqrt(-ln(H) / ln(2))
```

界面中的截止、阶数、过渡宽度和响应曲线必须双向联动，但只保存规范参数 `c/n`；派生过渡值不重复持久化。
这样既满足实时调节，也不会把非标准公式错误标成 Butterworth 或 Gaussian。

### 5.5 共轭、实值和增益

遮罩只依赖径向半径，是实数且中心对称，因此对实值输入自动保持共轭对称。应用规则：

```text
G(u,v) = H(u,v) * F(u,v),  H ∈ [0,1]
```

- 不修改相位；
- 不在缓存的 `FrequencySpectrum` 上原地写入；
- 每次执行先复制一份工作频谱，再逐点乘增益；
- IFFT 后统计最大虚部残差，门禁阈值内只取实部；
- 虚部超限视为算法错误，不静默丢弃；
- 全通等价配方允许短路，但必须保留与普通路径的等价测试。

### 5.6 通道、中性值与输出模式

输入平面使用既有六通道定义。Alpha 从始至终逐字节保留。

滤波引擎返回未裁切的 raw double 平面。Document 必须显式选择以下投影之一：

| 输出模式 | 公式 | 适用解释 |
| --- | --- | --- |
| `Direct` | `raw` 后舍入/裁切 | 低通、带阻的直接滤波结果 |
| `Centered` | `neutral + gain × raw` | 观察零均值高通/带通信号，不宣称是原信号 |
| `Additive` | `source + gain × raw` | 把高频分量叠加回源图，观察边缘增强 |

`neutral` 对显示固定为 128；通道回写仍遵守既有 Y/Cb/Cr 语义。`gain` 范围在 G0 冻结，建议 `[0,4]`、默认 1。
修改输出模式或增益只重复投影和诊断，不重复 FFT/IFFT。

导出文件名和状态必须包含“direct/centered/additive”语义，不能把 `Centered` 结果叫作“高通后的真实亮度”。

## 6. 遮罩、IFFT 与结果所有权

### 6.1 不可变模型

建议模型：

```text
FrequencyFilterRecipe
  Kind                 LowPass / HighPass / BandPass / BandStop
  Family               Ideal / Butterworth / Gaussian
  InnerCutoff           单截止时使用或作为带通内边界
  OuterCutoff           仅带通/带阻使用
  ButterworthOrder      仅 Butterworth 使用
  ProjectionMode        Direct / Centered / Additive
  ProjectionGain
  Channel

FrequencyFilterMask
  Width / Height
  Gains                 私有 double[]，对外只读且构造时复制
  RadialSamples         供曲线使用的固定 256 或 512 点只读数据
  RecipeFingerprint
```

无效家族参数必须被规范化或拒绝，不能进入配方指纹产生“视觉相同但指纹不同”的状态。例如 Gaussian 的 order 不参与
指纹，Ideal 的过渡值不进入快照。

### 6.2 结果组成

`FrequencyFilterResult` 至少包含：

- 当前配方指纹；
- 遮罩预览和径向响应；
- raw double 平面及最小/最大/均值；
- 最大 IFFT 虚部残差；
- 投影后的 `PixelImage`；
- 通道回写裁切统计；
- 差异、质量、梯度和副作用诊断；
- FFT 缓存命中、mask、multiply+IFFT、projection、diagnostics 的分阶段耗时；
- 代理/原尺寸标识与实际处理尺寸。

结果对象不得暴露可写数组。Session 释放或 generation 过期后，Document 不能继续导出旧结果。

### 6.3 缓存失效规则

| 参数变化 | 需要重建 Session/FFT | 需要重做 IFFT | 只需重做投影/诊断 |
| --- | --- | --- | --- |
| 源图、通道、代理档位 | 是 | 是 | 是 |
| 家族、方向、截止、阶数 | 否 | 是 | 是 |
| 输出模式、投影增益 | 否 | 否 | 是 |
| 显示缩放、选中图层 | 否 | 否 | 否 |
| 空间核尺寸 | 否 | 否 | 否，只重做空间对照 |

缓存规则必须由 Application/Document 状态机测试固定，不能依靠 UI 控件事件的偶然顺序。

## 7. 分析代理、原尺寸和资源预算

### 7.1 实时代理

- 最大边档位为 512、1024、2048，默认 1024；
- 不放大小图；大图用既有抗混叠代理投影；
- 代理宽高分别补到最小 2 的幂；
- 补零尺寸不得超过 `2048×2048` 和 `FrequencySpectrum.MaximumComplexValues`；
- UI 始终显示源图、代理、补零三组尺寸；
- 所有实时滑块只作用于代理缓存，不重复解码源图。

### 7.2 原尺寸执行

原尺寸不是默认路径。只有源图本身补零后仍满足：

```text
paddedWidth  <= 2048
paddedHeight <= 2048
paddedWidth * paddedHeight <= 4,194,304
```

才允许用户显式执行并导出原尺寸结果。超出时按钮显示具体原因，仍允许导出带清晰尺寸标识的代理结果。

V1 不通过缩小后再放大伪造原尺寸，也不分块执行全局 FFT，因为分块会改变频率和边界语义。

### 7.3 峰值预算

以 2048² 为最坏代理：

- 缓存频谱 `Complex[]` 约 64 MiB；
- IFFT 工作频谱约 64 MiB；
- `double[]` 增益遮罩约 32 MiB；
- raw 结果平面约 32 MiB；
- 两至三张 RGBA 投影各约 16 MiB；
- 其他曲线、统计和 Bitmap 有额外开销。

设计目标是单次活动处理控制在约 240 MiB 结构预算内，而不是承诺进程峰值。实现时应：

- mask 预览直接从增益生成，不长期缓存第二份灰度矩阵；
- 新请求先取消旧工作，再释放旧大结果；
- 不缓存每个滑块位置和每个空间核尺寸的完整图片；
- 空间比较完成后只保留当前选择的结果；
- 分配前使用 checked 计算样本与字节；
- 自动门禁检查数组上限、所有权和释放，不断言易受机器影响的进程工作集。

### 7.4 取消与防抖

- 滑块输入建议 120–180 ms 防抖；键盘提交可立即执行；
- 每次滤波、空间比较和原尺寸执行各有独立 generation；
- 新请求先推进 generation，再取消旧令牌；
- 遮罩按行、频谱乘法每若干样本、IFFT 每行/列、投影每若干像素观察取消；
- 取消不显示为错误，也不能提交半成品；
- Document 关闭时取消全部工作并释放 Session、结果和 Bitmap。

## 8. 副作用可视化

### 8.1 Ringing

Ringing 不能只靠一句说明。V1 至少提供：

- 结果与源图的有符号差异图；
- 当前横向/纵向剖面的源信号与结果曲线；
- raw 结果低于 0 或高于 255 的样本数、幅度和位置摘要；
- 理想遮罩冲激响应的旁瓣曲线；
- 在测试阶跃图上的 overshoot/undershoot Golden；
- Ideal 与平滑家族的并排参数说明。

“出现越界”是可测事实；“这是 Ringing”是基于滤波器与边缘附近振荡的解释。UI 文案必须区分二者。

### 8.2 模糊

低通模糊至少显示：

- 源图与结果的绝对差异；
- 复用固定中心差分得到的梯度能量比；
- PSNR/全局 SSIM（仅作为相对源图变化，不叫“质量提升”）；
- 选定剖面在高对比边缘上的扩散；
- 截止降低时梯度能量不增加的性质测试。

### 8.3 边缘增强

高通/带通的 `Additive` 模式至少显示：

- 叠加前后的梯度能量与裁切数；
- 正负高频分量的发散色图；
- raw、Centered 和 Additive 三种语义标签；
- 增益增加时过冲风险提示；
- Alpha 保留和颜色回写裁切统计。

梯度能量增加不等于主观清晰度提升，文档和状态栏不得作无依据的质量结论。

### 8.4 诊断类职责

`FrequencySideEffectAnalyzer` 只接收源平面、raw 平面和投影平面，返回数值与有限剖面数据。差异图由独立投影器生成。
它不更改像素、不决定滤波家族、不访问 UI，也不把启发式阈值写成领域真理。

## 9. 与空间域卷积比较

### 9.1 比较目的与限制

频域乘法与空间卷积的理论等价要求相同的离散网格、边界、完整冲激响应和数值精度。V1 为可运行实验使用
7×7、15×15 或 31×31 的截断核，因此空间结果通常是近似，不得标成“完全等价”。

界面必须同时报告：

- FFT padded 网格；
- 截断核尺寸；
- Wrap 边界；
- 核截断前后能量与系数和；
- 结果 MAE、最大绝对误差、PSNR/SSIM；
- 两条路径的算法执行耗时和计时范围；
- “理论等价 / 当前有限核近似”的解释。

### 9.2 冲激响应与核派生

步骤固定为：

1. 将频率增益作为零相位复数频谱；
2. 执行 IFFT 得到周期冲激响应；
3. 验证虚部残差；
4. 将空间原点循环移到图像中心；
5. 从中心截取 7/15/31 奇数窗口；
6. 在 DC 修正前记录截断保留的 L1/L2 能量比例；
7. 修正 DC 系数和：低通/带阻目标和为 1，高通/带通目标和为 0；
8. 转换为 `ConvolutionKernel` 并走既有 `SpatialConvolver`。

DC 修正只能把差值加到中心系数，不能对零和高通核做除法归一化。修正前后系数和都要显示并测试。

### 9.3 公平执行

- 两条路径使用相同的代理、通道、padded 平面和输出投影；
- 空间路径在 padded 平面上使用 `Wrap`，再裁回代理尺寸；
- 比较发生在 raw double 层，不能先各自 byte 裁切再掩盖误差；
- decode、代理生成、Bitmap 创建、UI 绘制和文件写入不计入算法耗时；
- 每次点击比较执行一次预热和至少三次有界测量，显示中位数；
- 自动测试只验证计时字段非负、范围正确和结果确定性，不设置“FFT 必须快 X 倍”的机器相关门禁；
- 31×31 空间计算超出受控操作预算时必须先可见阻断，不能冻结 UI。

### 9.4 性能结论边界

性能面板只能说“本机、当前代理、当前核、当前实现的观察值”。不得由一次运行推导跨机器结论，也不得把
FFT 对无限响应的优势与 7×7 小核的耗时混为一谈。`testing.md` 记录观察环境和样本，但性能不是发布承诺。

## 10. Application Session 与用例

### 10.1 Session

`FrequencyFilterSession` 建议持有：

- 已解码源 `PixelImage`；
- 当前分析代理；
- 选定通道的代理 double 平面；
- 补零尺寸和中性填充值；
- 不可变 `FrequencySpectrum`；
- 幅度谱投影和必要径向元数据；
- 源路径、源文件元数据和 Session 指纹；
- 可选、延迟创建的原尺寸通道信息。

Session 不持有 Document、Bitmap、ServiceProvider 或空间比较历史。它实现 `IDisposable` 只是释放大对象所有权并阻止释放后使用，
不假装能强制 GC 回收托管数组。

### 10.2 准备用例

`IPrepareFrequencyFilterSessionUseCase`：

- 通过 `IImageCodec` 解码一次；
- 校验源尺寸与资源上限；
- 创建指定档位代理；
- 抽取指定通道、建立 padded 平面并执行一次 FFT；
- 生成基础幅度谱和 Session；
- 失败或取消时不返回半成品。

### 10.3 滤波用例

`IApplyFrequencyFilterUseCase`：

- 校验 Session 与配方；
- 生成增益遮罩与径向响应；
- 复制频谱、乘遮罩并 IFFT；
- 裁取代理范围，验证虚部；
- 按输出模式投影并回写通道；
- 计算副作用诊断与阶段耗时；
- 返回与配方指纹绑定的不可变结果。

### 10.4 空间比较用例

`ICompareFrequencySpatialUseCase` 只在用户显式执行时派生核、走空间卷积并比较。它不重新解码、不修改当前频域结果，
也不因空间路径较慢而阻塞 UI Dispatcher。

### 10.5 原尺寸与导出

- 原尺寸用例先计算 padded 预算，不满足时返回结构化阻断；
- 原尺寸结果与代理结果使用不同标志和指纹；
- 修改任何数学参数后旧原尺寸结果立即 stale；
- 导出只接受当前 Session + 当前配方指纹匹配的结果；
- PNG 先编码，再通过现有原子写入发布；
- 取消、失败或文件冲突不能覆盖既有目标文件。

## 11. Document、快照与状态机

### 11.1 Document 注册

计划新增：

```csharp
public static readonly DocumentTypeId FrequencyFilterDocument =
    new("myavalonia.plugin.image.lab.document.frequency-filter");
```

并由唯一组合入口登记：

```text
AddPersistableDocument<FrequencyFilterDocument, FrequencyFilterView>
```

它是第十一个 Persistable Document、零普通 Document、零 Tool。注册数量测试必须从 10 更新为 11，并逐项验证稳定 ID，
不能只断言总数。

### 11.2 持久状态

快照 schema 从 `1` 开始，只保存：

- 源图路径；
- 通道与代理档位；
- 滤波方向和家族稳定枚举；
- 内/外截止与 Butterworth 阶数；
- 输出模式和显示增益；
- 空间核尺寸；
- 最后选择的归一化剖面位置与当前视图。

不保存：图片字节、复数频谱、增益数组、raw double、Bitmap、计时样本、空间核全数组、错误堆栈或取消对象。

恢复只恢复路径和参数，不自动读文件、不自动 FFT、不自动覆盖输出。未知 schema、非法枚举或参数必须回退安全默认值并显示
可恢复错误，不能让 Host 工作区恢复失败。

### 11.3 Dirty、stale 与 generation

- 路径、通道、档位、滤波和输出参数变化推进 Revision；
- 鼠标悬停、面板大小、进度和当前耗时不标 Dirty；
- 修改 Session 参数使所有结果 stale；
- 修改数学配方使频域、空间和原尺寸结果 stale；
- 只修改投影模式使投影/导出 stale，但允许复用 raw IFFT；
- 修改空间核尺寸只使空间比较 stale；
- 每个异步分支用 generation 拒绝迟到提交。

### 11.4 结构化错误

至少区分：路径不存在、解码失败、图片超限、代理预算超限、非法截止、频带倒置、过渡重叠、FFT 数值非有限、
IFFT 虚部超限、通道重建失败、空间操作预算超限、结果过期、导出失败和用户取消。

Document 负责把结构化错误翻译为详细中文状态；不能吞异常后继续显示或导出旧结果。

## 12. UI 信息架构

### 12.1 布局

沿用现有实验 Document 的分区习惯：

- 左侧“输入与参数”：图片、通道、代理档位、方向、家族、截止、阶数/过渡、输出模式和执行；
- 中部“频域联动”：原图、幅度谱、遮罩和 IFFT 结果，可切换同步缩放与像素探针；
- 右侧“解释与比较”：径向响应、差异/副作用、剖面、冲激响应、空间核、误差和耗时；
- 底部状态：源/代理/padded 尺寸、配方指纹摘要、当前阶段、进度、取消、stale 和错误。

### 12.2 控件状态

- Ideal 时禁用阶数并显示“过渡宽度 0”；
- Butterworth 时启用 1–12 阶并实时显示派生 90%–10% 过渡区；
- Gaussian 时显示截止/过渡的双向联动，不显示虚假的阶数；
- 低通/高通只显示单截止；带通/带阻显示内外截止；
- 内截止不小于外截止时执行按钮禁用并显示原因；
- `Centered`/`Additive` 才启用显示增益；
- 超出原尺寸预算时保留代理导出，并显示具体 padded 尺寸；
- 空间比较未执行或已 stale 时，不显示旧耗时为当前结果。

### 12.3 实时交互

- 参数变化先即时更新轻量径向曲线，再防抖执行 mask + IFFT；
- UI 不在 Dispatcher 上执行 CPU 密集循环；
- 快速拖动时旧请求被取消，状态栏显示最后提交参数；
- 视图切换和缩放不触发 IFFT；
- 频谱悬停显示半径、增益、原幅值和过滤后幅值；
- 剖面选择使用归一化坐标，代理尺寸变化后安全换算。

### 12.4 可访问性

- 颜色不是区分正差/负差、通过/阻带或 stale/当前的唯一方式；
- 曲线同时提供关键数值表格；
- 主要参数、执行、取消、导出和标签可键盘访问；
- 高对比主题下遮罩和曲线仍可辨识；
- Headless 测试实例化完整 View，检查关键绑定、条件启用和错误/空态。

## 13. 中文注释与设计说明规范

生产代码注释使用中文，重点解释“为什么、数学语义、所有权和边界”，具体要求：

- 每个领域模型、数值服务、用例、Document 和专用控件写详细 XML 摘要；
- 滤波公式旁说明 `c`、`n`、`H=0.5` 和振幅/功率区别；
- 带通乘积与带阻互补旁说明为何保证逐点互补；
- `r=0`、高次幂溢出、Gaussian 下溢和频带边界写清分支理由；
- 频谱工作副本注明所有权，说明为何不能修改 Session 缓存；
- IFFT 虚部检查注明共轭对称前提和失败语义；
- Direct/Centered/Additive 代码旁解释零均值信号与显示偏置，防止以后误删 128 或重复加回源图；
- 冲激响应 fftshift、截断和 DC 中心修正写明坐标及为何高通不能做普通 sum normalization；
- 空间比较注明 Wrap、padded 网格、raw double 与“近似等价”的限制；
- generation、防抖、取消和 stale 导出保护说明并发原因；
- 资源检查注明最坏数组大小和 checked 溢出原因；
- 复用 Spectrum/Convolution 的位置说明依赖方向，禁止未来反向引用 Feature。

不为显而易见的赋值、属性或循环逐行写“设置 X”“遍历 Y”式注释。详细注释应帮助维护者恢复设计思路，不能重复代码。
重要设计变化必须同步本文、数学文档或历史记录，不能只留在注释里。

## 14. 单元测试矩阵

### 14.1 模型与校验

- 三家族、四方向、六通道和三输出模式的稳定枚举 round-trip；
- 截止边界：0、最小正值、1、非有限、越界；
- 带通/带阻 `inner < outer` 与过渡重叠提示；
- Butterworth order 0/1/12/13；
- 不适用参数不进入配方指纹；
- 相同规范配方指纹稳定，不同数学参数指纹不同；
- 所有 checked 样本/字节预算在边界前后行为明确。

### 14.2 滤波响应 Golden

- Ideal 在 `r<c`、`r=c`、`r>c` 的精确 1/1/0；
- Butterworth 在 `r=0` 为 1、`r=c` 为 0.5，阶数增加时远离截止的响应更陡；
- Gaussian 在 `r=0` 为 1、`r=c` 为 0.5，固定点与独立公式一致；
- Low+High 在每个样本点误差不超过 `1e-12`；
- BandPass+BandStop 在每个样本点误差不超过 `1e-12`；
- 所有增益有限且位于 `[0,1]`；
- 低通径向响应单调不增，高通单调不减；
- 90%–10% 过渡边界反算正确；Ideal 宽度为 0；
- Butterworth 高阶不因溢出产生 NaN；Gaussian 极端半径不产生负值。

### 14.3 遮罩与频谱数值

- DC、轴端和角点增益与径向公式一致；
- 遮罩共轭对称，最大误差不超过 `1e-12`；
- 固定配方多次生成逐值一致；
- 取消在行边界可观察，不返回部分遮罩；
- 常量图低通保持常量，高通除数值误差外为零；
- 单冲激频谱乘法后 IFFT 等于对应冲激响应；
- 正弦输入只按目标半径增益缩放，空间相位保持；
- 棋盘格低通被抑制、高通保留；
- IFFT 虚部最大残差不超过 `1e-8`；
- 不修改 Session 原始 `FrequencySpectrum`；
- 全通短路与普通路径 raw 最大误差不超过 `1e-8`。

### 14.4 通道与输出投影

- R/G/B/Y/Cb/Cr 抽取与回写沿用既有 Golden；
- Alpha 每字节保持；
- Direct 的舍入、低/高裁切和统计；
- Centered 的 128 偏置只发生一次；
- Additive 只把 raw 高频加回一次，不重复滤波源图；
- 显示增益 0/1/4、负值、非有限和越界；
- Cb/Cr 中性值、色域裁切和计数；
- 参数变化只重做需要的阶段，缓存失效表逐项测试。

### 14.5 副作用测试

- 固定阶跃图的 Ideal 低通产生可复现 overshoot/undershoot；
- 相同截止下平滑家族的旁瓣/越界相对 Ideal 的诊断可解释，但不写脆弱的主观大小断言；
- 截止降低时固定测试图梯度能量不增加；
- Additive 增益增加时 raw 高频幅度按比例变化；
- 差异图正负颜色、零差、极值和放大系数；
- 剖面长度、归一化坐标映射和边界行/列；
- PSNR/SSIM 只对同尺寸结果执行，完全相同时走既有无穷/满分语义；
- raw 越界位置摘要有上限，不保存全部位置导致内存膨胀。

### 14.6 冲激响应与空间比较

- 遮罩 IFFT、中心搬移和共轭实值 Golden；
- 7/15/31 截取尺寸、中心和系数方向；
- 低通/带阻 DC 修正后核和接近 1；
- 高通/带通修正后核和接近 0；
- 不对零和核做除法；
- 保留能量比例位于 `[0,1]`；
- 把已知 3×3/5×5 有限核嵌入小型 FFT 网格后，频域乘法与同核 Wrap 空间卷积误差不超过数值容差；
- 参数化滤波遮罩派生的无限/全网格冲激响应只验证截断后的近似误差与收敛趋势，不伪造“完整奇数核”等价；
- 截断核结果报告非零近似误差，不伪造精确相等；
- 两条路径比较 raw double，byte 裁切不影响误差；
- 计时范围不包含解码、Bitmap 与文件 IO；字段非负且多次测量取中位数；
- 操作预算超限时在分配/执行前阻断。

### 14.7 Application、Session 和导出

- 准备用例只解码一次，代理/原图/padded 尺寸正确；
- Session 释放后拒绝使用，两个 Session 不共享可变缓冲；
- 滤波失败或取消不提交半成品；
- 原尺寸预算边界：刚好可用、单维超限、总样本超限；
- 新请求取消旧请求，迟到 generation 不覆盖当前结果；
- 输出模式变化复用 raw IFFT，数学参数变化重做 IFFT；
- 当前配方与结果指纹不符时导出被拒绝；
- PNG 导出 round-trip、尺寸、Alpha 和原子覆盖保护；
- 用户取消不写目标，不留下临时文件。

### 14.8 Document、持久化、组合根和 View

- 快照只含轻量参数，不含 RGBA、Complex、double mask、Bitmap、计时和错误堆栈；
- 恢复不自动解码或执行；未知 schema/非法参数可见回退；
- Dirty、stale 和 generation 规则逐项测试；
- 两个 DI Scope 的图片、参数、Session、取消和结果互不影响；
- Module 新增第十一个 Persistable Document，逐项稳定 ID 正确，仍为 0 Tool；
- 服务登记无重复冲突，算法 singleton 不持有 Document 状态；
- Standalone 复用真实 Module/DI/View，不复制业务实现；
- Headless 下完整 View 可创建，家族/方向条件控件正确启禁；
- 空态、忙碌、取消、错误、stale、代理与原尺寸标识可见；
- 生产源码扫描证明未新增 AIFLOW、通用工作流、Windows CI 或发布脚本。

### 14.9 架构与注释门禁

- Domain 项目命名空间扫描不得引用 Avalonia、Features、Infrastructure、Host SDK 或文件系统；
- Document 源码不得出现 FFT/滤波公式循环；
- View/code-behind 不得出现半径、Butterworth 或 Gaussian 数学；
- 不出现 Service Locator、运行时反射算法发现、万能 service 或事件总线；
- 新增核心类型和关键公式必须有中文 XML/行内设计注释；
- 评审清单逐项检查注释是否解释原因、边界和所有权，而不是只统计注释行数。

## 15. 本地开发门禁

### 15.1 每个实施包

每个 G 包最低要求：

1. 本包新增/修改测试通过，既有 333 项基线无回归；
2. Debug warn-as-error build 0 警告、0 错误；
3. 实际测试总数、失败数和跳过数如实记录，不预设“应新增多少项”；
4. 对应 `history/gN-*.md` 与受影响设计文档同步；
5. 不删除、跳过或放宽既有测试来换取通过；
6. 涉及数值 Golden 的代码必须先有失败测试，再完成实现。

### 15.2 G9 完整门禁

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

硬条件：

- restore 必须使用 lock file，不能静默升级包；
- Debug/Release 构建均 0 警告、0 错误；
- Debug/Release 新旧测试全部通过、0 失败、0 跳过；
- 测试总数必须大于 333，但只记录 runner 实际值，不在计划中捏造目标；
- `git diff --check` 通过；
- 没有新增 NuGet、AIFLOW、Workflow Action、Workbench Command、Windows CI 或发布配置；
- 文档状态与代码事实一致。

### 15.3 本轮明确不做的门禁

- 不创建 GitHub Actions/Azure DevOps 等 Windows CI；
- 不运行插件 Build 包的 ZIP/发布 Target；
- 不做真实 Host 安装、升级、卸载或 Dock 生命周期验收；
- 不把 Standalone Headless/窗口预览当成真实 Host 证据；
- 不修改发布版本、市场资料或发布清单；
- 不声明发布完成。

这些事项只在用户明确进入发布阶段时，按 `docs/design/shared/deployment-and-release.md` 单独执行。

## 16. 分阶段实施包

### G0：产品、数学与基线冻结

- 复跑并记录 333/333 Debug/Release 起始基线；
- 冻结半径、截止、阶数、过渡、带通乘积和互补公式；
- 冻结 Direct/Centered/Additive、中性值和裁切语义；
- 冻结代理、补零、原尺寸和内存/操作预算；
- 为响应、阶跃、冲激、正弦和空间等价准备独立 Golden；
- 建立 `mathematical-principles.md`、`testing.md` 和 `history/g0-*.md`；
- 不改 Module、Document 或 UI。

验收：所有产品与数值歧义能由文档和 Golden 回答，起始代码仍为 333/333。

### G1：领域模型与配方

- 建立稳定枚举、不可变 Recipe、Mask/Result 描述和配方指纹；
- 完成截止、频带、阶数、输出增益和资源校验；
- 冻结不适用参数的规范化；
- 测试所有边界、非有限数、指纹和数组所有权。

验收：Domain 不引用 UI/文件，非法状态不能进入算法。

### G2：滤波响应与遮罩

- 完成三家族低通原型和四方向组合；
- 完成 90%–10% 过渡计算和径向曲线；
- 完成 double 增益遮罩、预览与共轭检查；
- 复用统一坐标，不破坏旧 `FrequencyBandMaskFactory`；
- 通过单调、互补、有限值、边界和取消门禁。

验收：十二种组合均有纯数值证据，未接图片和 UI。

### G3：频域执行与通道重建

- 完成频谱工作副本、逐点乘法、IFFT、裁剪和虚部诊断；
- 完成 raw double 与 Direct/Centered/Additive 投影；
- 完成六通道回写、Alpha、舍入和裁切统计；
- 通过常量、冲激、正弦、棋盘格、全通和缓存不变测试。

验收：从缓存频谱到 `PixelImage` 的核心闭环不依赖 Avalonia 或文件系统。

### G4：副作用诊断

- 完成 signed/absolute 差异、raw 越界、梯度能量和剖面；
- 复用 PSNR/SSIM，不复制指标公式；
- 完成 Ringing 旁瓣、低通模糊和 Additive 增强解释数据；
- 增加固定阶跃和边缘 Golden；
- 限制位置摘要与曲线样本数。

验收：副作用既有可视数据也有数值证据，且不作主观质量承诺。

### G5：空间核与公平比较

- 完成遮罩 IFFT、中心搬移、7/15/31 截断和 DC 修正；
- 在相同 padded 平面、Wrap 边界、raw double 上调用空间卷积；
- 完成误差、核能量、操作预算和中位数耗时；
- 通过小网格完整核等价与有限核近似测试；
- 文案固定“近似”边界。

验收：理论条件、实现差异与性能计时范围可复核。

### G6：Application Session 与导出

- 建立 Session、五个窄用例和缓存失效规则；
- 接入解码、代理、原尺寸预算和原子 PNG；
- 完成取消、generation、stale、释放和配方指纹保护；
- 完成代理/原尺寸结果的明确类型与状态；
- 用 fake ports 测试失败、取消和原子行为。

验收：Application 只编排已验证 Domain，不创建 Bitmap 或操作 View。

### G7：Document、持久化与组合根

- 新增 `PluginIds.FrequencyFilterDocument`；
- 新增服务登记分区与第十一个 Persistable Document；
- 完成 Document 状态机、命令、快照、Dirty/stale 和关闭释放；
- 更新注册数量/稳定 ID/Scope 隔离测试；
- 不新增 Tool、AIFLOW、Workflow Action 或 Workbench Command。

验收：多实例状态隔离，恢复不自动执行，旧结果不可误导或导出。

### G8：UI、Standalone 与解释

- 完成输入、参数、四联视图、曲线、副作用、空间比较和状态区；
- CPU 工作全部移出 Dispatcher；
- 完成条件启禁、防抖、取消、错误、空态和可访问性；
- Standalone 增加第十一个真实 Document 页签/入口，不复制业务；
- 完成 Avalonia Headless View 和关键交互测试；
- 补齐 `guide.md` 和 `user-manual.md`。

验收：用户能完成全部闭环，并始终知道当前结果尺寸、模式和是否 stale。

### G9：质量加固与本地封板

- 复跑响应、FFT、通道、诊断、空间、资源、取消、持久化和既有能力全回归；
- 执行 locked restore 与 Debug/Release warn-as-error build/test；
- 记录实际总测试数、耗时、0 跳过和 0 警告；
- 完成 `testing.md`、全部 history、根索引和未来能力状态同步；
- 扫描确认无 AIFLOW、Windows CI 和发布配置变化；
- 真实窗口人工项若未执行，明确延期，不用 Headless 冒充。

验收：只可标记“本地开发自动门禁完成”，仍不得标记发布完成。

## 17. 建议目录与文件

```text
src/ImageLabPlugin.Plugin/
├─ Domain/FrequencyFiltering/
│  ├─ FrequencyFilterModels.cs
│  ├─ RadialFilterResponse.cs
│  ├─ FrequencyFilterMaskFactory.cs
│  ├─ FrequencyFilterEngine.cs
│  ├─ FrequencySignalProjector.cs
│  ├─ FrequencySideEffectAnalyzer.cs
│  ├─ FrequencyDifferenceProjector.cs
│  ├─ FrequencyImpulseResponseFactory.cs
│  └─ FrequencySpatialComparator.cs
├─ Application/FrequencyFiltering/
│  ├─ FrequencyFilterContracts.cs
│  ├─ FrequencyFilterSessionUseCases.cs
│  ├─ FrequencyFilterExecutionUseCases.cs
│  ├─ FrequencySpatialComparisonUseCase.cs
│  └─ FrequencyFilterExportUseCase.cs
└─ Features/FrequencyFilter/
   ├─ FrequencyFilterDocument.cs
   ├─ FrequencyFilterView.axaml
   ├─ FrequencyFilterView.axaml.cs
   ├─ FrequencyResponseControl.cs
   ├─ FrequencyProfileControl.cs
   └─ FrequencyFilterHelpCatalog.cs

tests/ImageLabPlugin.Tests/
├─ FrequencyFilterResponseTests.cs
├─ FrequencyFilterMaskTests.cs
├─ FrequencyFilterEngineTests.cs
├─ FrequencyFilterProjectionTests.cs
├─ FrequencyFilterSideEffectTests.cs
├─ FrequencySpatialComparisonTests.cs
├─ FrequencyFilterUseCaseTests.cs
├─ FrequencyFilterDocumentTests.cs
└─ FrequencyFilterViewTests.cs
```

这是职责建议，不要求机械创建空文件。小值对象可在同一上下文文件中；文件变大时按职责拆分。禁止用单个
`FrequencyFilterService.cs` 或 `FrequencyFilterDocument.cs` 承载全部算法与用例。

## 18. 文档同步清单

实施期间按现有能力惯例补齐 `docs/design/frequency-filter/`：

- `README.md`：能力入口、状态、阅读顺序和边界；
- `implementation.md`：本文，逐包更新真实状态；
- `mathematical-principles.md`：滤波公式、坐标、过渡、卷积定理和输出语义；
- `testing.md`：Golden 来源、容差、实际通过数、性能观察和未证明事项；
- `guide.md`：稳定 ID、参数、状态机、缓存、限制和导出；
- `user-manual.md`：面向新手的实验步骤和副作用解释；
- `history/README.md` 与 `g0...g9`：每包实际落点、证据、偏差、风险和回滚。

同时同步：

- 根 `README.md`；
- `docs/README.md`；
- `docs/design/README.md`；
- `docs/future-capabilities.md`；
- `docs/design/shared/project-and-window-responsibilities.md`；
- Spectrum Inspector 中关于参数化滤波器仍属后续能力的边界；
- Convolution Playground 中关于有限空间核比较的复用边界。

只有 G9 全部门禁通过后，才能把“计划中”改成“V1 已实现”。发布资料继续保持延期状态。

## 19. 风险与回滚

| 风险 | 预防与证据 | 回滚边界 |
| --- | --- | --- |
| 把过渡宽度做成非标准公式 | 使用标准响应和 90%–10% 派生定义 | 回退 G2 响应类，不影响 FFT 基础 |
| Ideal Ringing 被误判为实现错误 | 阶跃 Golden、冲激旁瓣和解释文档 | 只回退显示/文案，不改数值 |
| 高通结果因负值显示错误 | raw double + 三种显式投影 Golden | 回退投影层，不改滤波结果 |
| 大代理瞬时内存过高 | 结构预算、单当前结果、取消后释放 | 降低默认档位或禁用 2048，不扩大共享上限 |
| 快速滑块产生迟到覆盖 | generation + 独立取消测试 | 回退 Document/UI，不回退 Domain |
| 空间结果与频域不一致 | 同 padded/Wrap/raw 条件与完整核 Golden | 回退 G5，不影响频域主闭环 |
| 截断核被误称精确等价 | UI/文档/报告固定“近似”字段 | 禁用空间对照直至解释修正 |
| 破坏既有 Spectrum/Convolution | 333 基线全回归和共享 API 最小改动 | 按 G 包回退新消费者，保留既有类型 |
| Document 状态跨实例泄漏 | Scope 隔离、不可变 Session 测试 | 回退 G7 注册与 Feature |
| 发布范围被提前扩大 | 明确无 CI/ZIP/Host 门禁并扫描 diff | 回退发布相关意外文件 |

任何回滚都不得使用破坏用户工作区的 git 命令；按 G 包撤销新能力边界，并复跑既有全量门禁。

## 20. 完成定义

只有同时满足以下条件，Frequency Filter V1 才能标记为“本地开发完成”：

1. 用户能在独立多实例 Persistable Document 中完成十二种径向滤波组合实验；
2. 截止、Butterworth 阶数和实际过渡带实时联动，且数学定义准确；
3. 幅度谱、遮罩、IFFT 结果、差异和径向响应使用统一坐标并联动；
4. Direct/Centered/Additive 不混淆，Alpha、通道、中性值、舍入和裁切语义正确；
5. Ringing、模糊和边缘增强均有图像、剖面和数值证据，不作无依据的主观质量结论；
6. 空间域比较在相同 padded/Wrap/raw 条件下执行，并明确有限核只是近似；
7. 代理、预算内原尺寸和 stale 结果在类型、状态、UI 与导出中不会混淆；
8. Domain/Application/Infrastructure/Feature 依赖方向符合 SOLID，没有万能服务和不必要模式；
9. 重要数学、数组所有权、取消、资源和取舍均有详细中文注释；
10. 新增与既有测试在 Debug/Release 下全部通过、0 失败、0 跳过，构建 0 警告、0 错误；
11. 专用文档、实施历史、根索引和未来能力状态与实际代码完全一致；
12. 没有使用 AIFLOW，没有新增 Windows CI，也没有执行或宣称任何发布门禁。

上述完成定义只代表本地开发封板。真实 Host、ZIP、安装升级、Windows CI 和发布验收必须等用户明确进入发布阶段后再执行。
