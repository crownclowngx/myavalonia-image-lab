# ImageLabPlugin V1 卷积核实验台实施计划

> 计划状态：开发实现与本地自动门禁完成；未执行发布阶段门禁<br>
> 基线日期：2026-08-30<br>
> 产品名称：Convolution Playground／卷积核实验台<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 起始自动基线：2026-08-30 实际复跑 locked restore；Debug/Release build 均零警告、零错误；两配置 test 均 241/241 通过、零跳过；实施封板时必须再次完整复跑<br>
> 核心路线：不可变卷积核 + 真二维离散卷积 + 明确定义的边界采样/归一化/偏置 + 单核与双核算子 + 分析代理实时预览 + 完整尺寸显式执行 + 核频率响应与像素贡献解释<br>
> 实施原则：SOLID 是首要规定；设计模式只用于真实变化点且保持朴素；生产代码使用详细中文注释解释数学、所有权与取舍；不使用 AIFLOW；不新增 Windows CI；不执行发布门禁
> 完成证据：2026-08-30 locked restore；Debug/Release warn-as-error build 均零警告、零错误；两配置 test 均 303/303 通过、零跳过；详见 `testing.md` 与 `history/g9-local-sealing.md`<br>

| 实施包 | 当前状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 完成 | 冻结产品范围、卷积约定、资源预算、交互和数值基线 | [实施记录](history/g0-product-and-numeric-baseline.md) |
| G1 | 完成 | 建立不可变核、校验、文本解析、归一化和预设工厂 | [实施记录](history/g1-kernel-domain-and-catalog.md) |
| G2 | 完成 | 完成边界采样、单通道真卷积和通道重建核心 | [实施记录](history/g2-spatial-convolution-core.md) |
| G3 | 完成 | 完成平滑、锐化、双核梯度、Laplacian 和浮雕语义 | [实施记录](history/g3-preset-and-composite-operators.md) |
| G4 | 完成 | 完成核频率响应、差异投影、像素探针和贡献解释 | [实施记录](history/g4-response-and-explanation.md) |
| G5 | 完成 | 建立窄用例、Session、代理/完整尺寸执行和 PNG 导出 | [实施记录](history/g5-application-session-and-export.md) |
| G6 | 完成 | 完成第九个 Persistable Document、快照和多 Scope 隔离 | [实施记录](history/g6-document-lifecycle.md) |
| G7 | 完成 | 完成联动 UI、自定义核编辑、无障碍和 Headless View | [实施记录](history/g7-ui-and-interaction.md) |
| G8 | 完成 | 完成数值回归、资源、取消、架构和兼容性门禁 | [实施记录](history/g8-quality-hardening.md) |
| G9 | 完成 | 完成本地双配置门禁、专用文档和开发阶段封板 | [实施记录](history/g9-local-sealing.md) |

本文定义 ImageLab 在 LSB 隐写与统计实验之后的下一项能力和第九个 Persistable Document。它不是“滤镜商店”，
也不以一键美化为目标；用户必须能够看到核矩阵、锚点、卷积约定、边界策略、有效除数、偏置、原始响应范围、
裁切数量、频率响应和某一输出像素的逐项来源。

本文是实施阶段的唯一总计划。每个 G 包均已有对应历史记录，写明实际修改、测试证据、偏差、遗留风险和回滚方式；
上表“完成”来自实际实现和 G9 双配置门禁，不表示真实 Host、ZIP、Windows CI 或发布验收已经完成。

## 1. V1 用户闭环与固定实施顺序

### 1.1 用户闭环

```text
显式选择一张 PNG 或 JPEG 图片
    ↓
解码 RGBA8888，并建立最大边 512/1024/2048 的抗混叠分析代理
    ↓
选择均值、高斯、运动模糊、锐化、反锐化、高提升、边缘或浮雕预设
    ↓
或输入 3×3 至 31×31 的奇数方形自定义核
    ↓
选择边界策略、归一化、显式除数、偏置和通道处理方式
    ↓
通过短防抖在分析代理上执行可取消卷积
    ↓
联动查看原图、结果、绝对/有符号差异和核的二维频率响应
    ↓
点击同一坐标，查看输入/输出像素、原始累加值、除数、偏置、裁切和核贡献
    ↓
按需显式执行完整分辨率卷积；成功后原子导出 PNG
```

### 1.2 固定实施顺序

1. G0 先冻结“真卷积还是相关”、核坐标、边界映射、归一化、舍入、裁切、通道和频率响应语义；
2. G1 用值对象和手算 Golden 固定核事实，不写 UI，也不先接入现有模糊算子；
3. G2 先让通用空间域执行器通过小矩阵、边界、通道、取消和输入不变测试；
4. G3 再增加固定目录和双核梯度组合，预设只负责生成核，不复制执行循环；
5. G4 证明频率响应与空间核一致，并完成差异、探针和逐项解释；
6. G5 用窄应用用例管理代理预览、完整尺寸结果、导出和 Session 所有权；
7. G6/G7 最后接入 Document、快照、编辑器、联动 UI、Standalone 和 Headless View；
8. G8 补齐数值、资源、架构、并发和既有能力兼容门禁，不以人工观感代替测试；
9. G9 重新执行本地 locked restore、Debug/Release warn-as-error build/test 并同步全部专用文档；不执行发布门禁。

### 1.3 V1 决策摘要

| 主题 | V1 决策 |
| --- | --- |
| 产品形态 | 第九个多实例 Persistable Document，不登记 singleton Tool |
| 输入 | 单张 PNG/JPEG；沿用 64 MiB 编码输入和 16,000,000 像素上限 |
| 实时预览 | 抗混叠分析代理，最大边 512/1024/2048，默认 1024；界面始终显示实际尺寸 |
| 完整结果 | 用户显式执行；与实时代理结果分别持有、分别标识；仅完整结果可作为默认导出对象 |
| 核尺寸 | V1 只接受 3 至 31 的奇数方形核；锚点固定为中心；不支持偶数、非方形或可移动锚点 |
| 运算约定 | 真二维离散卷积，不把相关运算静默称为卷积 |
| 核预设 | 均值、高斯、运动模糊、锐化、反锐化、高提升、Sobel、Prewitt、Scharr、Laplacian、浮雕 |
| 边界 | 常量、复制、Reflect-101、Wrap；常量值显式可见 |
| 归一化 | 不归一化、除以核和、除以绝对值和、显式除数；非法或近零除数在执行前阻断 |
| 偏置 | 有限 double，默认 0；导数/浮雕预设可建议 128，但不隐式修改用户选择 |
| 通道 | RGB 独立处理，或只替换 R/G/B/Y/Cb/Cr 中一个通道；Alpha 始终逐字节保留 |
| 梯度 | X、Y 是单核线性结果；Magnitude 是双核结果 `sqrt(Gx²+Gy²)`，明确标记为非线性组合 |
| 频率响应 | 显示应用归一化后的核响应；偏置、边界和输出裁切不伪装成线性传递函数的一部分 |
| 导出 | 显式完整尺寸计算成功后原子导出 PNG；不把分析代理静默导出为原尺寸结果 |
| 集成 | 零 AIFLOW、零 Workflow Action、零 Workbench Command、零通用滤镜 DAG |

## 2. 当前工程基线与复用边界

### 2.1 已有事实

当前仓库已经具备：

- 一个真实 `ImageLabPlugin.Plugin` 插件程序集、复用真实 Module/DI 的 Standalone 和单一 xUnit/Headless 测试项目；
- 八个 Persistable Document、每实例 DI Scope、`IDocumentLifetime`、轻量快照、generation、取消和 Bitmap 释放惯例；
- 自有未预乘 RGBA8888 `PixelImage`、`ImageSize`、16,000,000 像素上限和 64 MiB 编码输入上限；
- `ImageAnalysisProxyProjector` 的 512/1024/2048 抗混叠分析代理；
- `ImageChannelConverter` 的 R/G/B/Y/Cb/Cr 抽取、单通道重建和裁切计数；
- `FullReferenceQualityAnalyzer`、差异图、热力图、像素探针和有界预览的既有设计经验；
- `Fft1DTransform`、`Fft2DTransform`、`SpectrumProjector` 和中心化频谱显示基础；
- `Domain.Robustness` 内部已有可分离 Gaussian Blur 与 Unsharp Mask，但其固定边界、通道和扰动语义不足以直接承担实验台；
- Avalonia PNG/JPEG 编解码、图片文件对话框、原子写入和 Headless View 测试方式；
- 2026-08-30 实际复跑 Debug：241/241、零失败、零跳过，构建零警告、零错误；
- 当前没有 AIFLOW，Windows CI、ZIP、真实 Host 和发布验收均不在开发阶段门禁内。

### 2.2 复用规则

- 直接复用 `PixelImage`、`ImageSize`、`IImageCodec`、`IImageFileDialog`、`IAtomicFileWriter` 和图片预览基础；
- 直接复用 `ImageAnalysisProxyProjector`，但界面和导出必须区分“分析代理”与“完整尺寸”；
- 复用 `ImageChannelConverter` 的六通道公式和 Alpha 保留事实，不另写第二套 YCbCr 公式；
- 频率响应优先复用已验证的二维 FFT 数值核心；新增代码负责把以中心为锚点的核正确搬移到 FFT 原点；
- 可把 Robustness 中 Gaussian 核生成和 Unsharp 公式提炼到协议中立的卷积领域，随后让 Robustness 适配共享核心；
- 提炼前先补现有 Gaussian/Unsharp 输出回归，提炼后逐像素保持原扰动的 clamp-to-edge 与 Alpha 语义；
- Robustness 的 `IImagePerturbationOperator` 是实验攻击步骤，不是通用卷积接口；卷积台不得依赖扰动配方、trial 或攻击枚举；
- 既有 `ImageDifferenceProjector` 只表达 RGB 绝对差异；有符号通道响应应新增窄投影器，不扭曲原类型语义；
- 不为追求复用率修改水印、LSB、指纹或比较实验的稳定协议和阈值。

### 2.3 需要新增的能力

- 有尺寸、中心锚点、有限系数和不可变所有权的 `ConvolutionKernel`；
- 自定义矩阵文本解析、结构化错误和预设参数校验；
- 真卷积的通用执行器、四种边界映射、四种归一化和显式偏置；
- 单通道、RGB 三通道和双核梯度的结果模型；
- 均值、高斯、运动、锐化、反锐化、高提升、三类梯度、Laplacian、浮雕目录；
- 归一化后核的二维幅值/相位响应、DC 增益和中心横纵截面；
- 绝对差异、有符号差异、像素探针和卷积贡献解释；
- 分析代理 Session、完整尺寸执行、PNG 导出、Document、View、组合根和专用测试/文档。

## 3. 产品范围与明确非目标

### 3.1 V1 必须完成

- 均值核：3×3 至 31×31 的奇数尺寸；
- 高斯核：由奇数尺寸和 `sigma` 生成，系数对称且默认按和归一化；
- 运动模糊：奇数尺寸、角度和长度的确定性离散核，显示实际非零权重；
- 锐化：轻度、标准、强度可调的中心增强核；
- 反锐化掩模：显式 `sigma` 与 `amount`，核为 `(1 + amount)δ - amount·Gσ`；
- 高提升：显式 `A`，核为 `Aδ - Gσ`，并显示其 DC 增益 `A - 1`；
- Sobel、Prewitt、Scharr：X、Y 和 Magnitude，方向与符号定义可查；
- Laplacian：四邻域和八邻域两种离散形式；
- 浮雕：有限方向预设、强度和建议偏置，不能只给不可解释名称；
- 3×3 至 31×31 奇数方形自定义核，支持网格编辑和矩阵文本粘贴；
- 常量、复制、Reflect-101、Wrap 边界；不允许未命名的默认策略；
- 不归一化、核和、绝对值和和显式除数；偏置、舍入、裁切计数和原始响应范围可见；
- RGB 独立卷积，或只修改 R/G/B/Y/Cb/Cr 一个通道；Alpha 始终保持；
- 原图、结果、绝对差异、有符号差异、频率响应联动；
- 同坐标像素探针、核覆盖范围和累加贡献说明；
- 可取消、防迟到覆盖、多实例隔离、轻量快照、完整尺寸 PNG 原子导出；
- Debug/Release 本地自动门禁、专用文档和有限人工验收记录。

### 3.2 明确不实现

- 美颜、磨皮、调色、贴纸、风格化滤镜包或第三方滤镜市场；
- 任意多步滤镜链、节点图、宏、脚本、插件式算子发现或通用图像工作流；
- AIFLOW、Workflow Action、Workbench Command 或 DAG 执行器；
- 卷积神经网络、训练、推理、特征图、反向传播或 GPU shader；
- 任意尺寸无限核、偶数核、非方形核、可移动锚点、稀疏核文件格式；
- 二维自定义核的自动低秩分解、自动优化器或“智能推荐”；
- 双边滤波、中值滤波、形态学、Guided Filter；它们不是线性卷积，不能塞入核目录；
- Canny、LoG/DoG 零交叉、Hough、边缘追踪或阈值分割；
- FFT overlap-add/save 大核卷积、GPU/SIMD 性能承诺或原尺寸“实时”承诺；
- HDR、16 位、浮点、RAW、ICC 色彩管理或预乘 Alpha 卷积；
- JPEG 结果导出；有损编码会混淆卷积结果和编码误差；
- Windows CI、ZIP、真实 Host 安装/升级/卸载、发布清单或发布完成声明。

### 3.3 教学与解释边界

- “锐化”表示由核增强局部变化，不表示恢复已经丢失的真实细节；
- “边缘”是离散梯度或二阶差分响应，不是物体识别结果；
- “频率响应”描述线性核本身；边界扩展、偏置、逐通道裁切和梯度 Magnitude 会破坏严格线性关系；
- 分析代理的结果用于交互观察，不代表完整尺寸逐像素结果；两者必须显示各自尺寸；
- PSNR/SSIM 或差异较小只表示数值接近，不表示视觉更好或算法更正确；
- 高提升、Laplacian、Sobel 等零和/负系数核会产生负响应；字节图只是经过除数、偏置和裁切后的显示/重建。

## 4. Document 形态、身份与状态所有权

### 4.1 贡献形态

“卷积核实验台”固定为第九个 Persistable Document：

| 字段 | 固定值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.convolution-playground` |
| 显示名称 | `卷积核实验台` |
| 描述 | `编辑空间卷积核，并联动观察边界、差异、像素贡献和频率响应` |
| 分类 | `图像分析` |
| Host 注册 | `AddPersistableDocument<ConvolutionPlaygroundDocument, ConvolutionPlaygroundView>` |
| 实例基数 | 多实例，每个实例独立图片、核、参数、缓存、结果和取消令牌 |

选择 Document 而不是 Tool 的原因：图片路径、核矩阵、选中像素和完整尺寸结果属于可保存的实验上下文；用户也可能
同时比较同一图片的不同边界或核。singleton Tool 无法正确拥有这些实例状态，也无法在关闭单个实验时释放大图。

### 4.2 持久状态

- 源图片路径；
- 预设稳定 ID 或 `custom`，不能序列化中文显示名作为协议；
- 自定义核尺寸与有限系数；
- 预设参数：尺寸、sigma、运动角度/长度、amount、A、方向和强度；
- 卷积约定版本，V1 固定为 `true-convolution-v1`；
- 边界策略、常量边界值、归一化模式、显式除数、偏置；
- 通道模式、梯度输出模式、分析代理档位、差异视图和频率响应视图；
- 最后选中的归一化图像坐标。

### 4.3 运行时派生状态

- 解码后的原图和分析代理；
- 已解析、已归一化的有效核或双核；
- 代理卷积的 raw double 平面、重建图片、差异图和统计；
- 频率响应复数数据、幅值/相位投影和横纵截面；
- 显式生成的完整尺寸结果；
- Avalonia `Bitmap`、当前进度、错误、取消源和 generation。

运行时对象不得写入快照。参数改变后，旧的完整尺寸结果必须立刻标记为 stale，禁止带着新参数标签导出旧图片。

### 4.4 快照与恢复

- schema 从 `1` 开始，枚举写稳定英文 ID，系数写 JSON number；
- 单个快照最多保存 31×31 个有限系数，不保存像素、raw 响应、Bitmap、FFT 或 PNG；
- 恢复时验证尺寸、系数数量、有限性、参数范围和 divisor；未知预设回退到 `custom` 或安全的 3×3 identity；
- 恢复后只显示路径和参数，不自动读取文件、不自动卷积、不自动执行完整尺寸任务；
- 无效旧快照显示结构化可恢复错误，不能让 Host 的整个工作区恢复失败；
- 关闭时取消代理/完整尺寸工作并释放 Session、完整结果和全部 Bitmap。

## 5. 数学与数值协议

### 5.1 核坐标与真卷积

V1 的核尺寸 `K` 为 `[3,31]` 内奇数，半径 `r=(K-1)/2`。矩阵第 `row`、`column` 个系数的数学坐标为：

```text
ky = row - r
kx = column - r
```

对输入平面 `f` 和有效核 `h`，未归一化累加值固定为：

```text
acc(x, y) = Σ[ky=-r..r] Σ[kx=-r..r] h(ky, kx) · f(x-kx, y-ky)
```

这是真卷积。不得实现 `f(x+kx,y+ky)` 后仍称为卷积。3×3 非对称 impulse Golden 必须能发现核是否被错误翻转。
Sobel、浮雕和运动模糊等非对称预设的展示矩阵按上述冲激响应语义提供，文档同时给出其预期正方向。

### 5.2 系数、除数、偏置、舍入与裁切

- 每个系数必须是有限 double，绝对值不超过 `1024`；禁止 NaN、Infinity 和空单元格进入执行器；
- 有效除数 `d` 由归一化策略生成，必须有限且 `abs(d) >= 1e-12`；
- raw 值为 `acc/d`，偏置后值为 `v=raw+bias`；
- 字节结果统一使用 `Math.Round(v, MidpointRounding.AwayFromZero)`，再裁切到 `[0,255]`；
- 结果必须报告 raw 最小/最大值、偏置后最小/最大值、低端/高端裁切样本数；
- 计算使用 double，不在每个乘加步骤量化；只在最终重建字节时舍入；
- 累加顺序固定为行优先，Golden 允许有明确 double 容差，不要求不同 CPU 的最后一位完全相同。

### 5.3 归一化

| 模式 | 有效除数 | 规则 |
| --- | --- | --- |
| None | `1` | 保留核原始增益 |
| KernelSum | `Σh` | 近零时阻断，不偷偷改用 1 |
| AbsoluteSum | `Σabs(h)` | 近零时阻断 |
| Explicit | 用户输入 | 近零、NaN、Infinity 或越界时阻断 |

频率响应使用 `h/d`。偏置不是核系数，不进入 `H(u,v)`；输出裁切也不是线性系统的一部分。预设可以给出推荐模式，
但 UI 必须显示最终有效除数，用户切换模式后不得由后台再次隐式归一化。

### 5.4 边界映射

对长度 `n` 的一维索引 `i`：

- `Constant`：越界直接返回显式常量，不映射索引；默认常量为 0；
- `Replicate`：`clamp(i,0,n-1)`；
- `Reflect101`：以边界像素之外为对称轴，`... c b | a b c ... | c b ...`；`n=1` 时固定映射到 0；
- `Wrap`：`((i % n) + n) % n`。

二维边界分别映射 X/Y。必须用 1×N、N×1、2×2 和核大于图像的 Golden 覆盖多次反射/环绕，不能只测试一次越界。

### 5.5 通道处理

| 模式 | 输入平面 | 重建规则 |
| --- | --- | --- |
| RGB | R/G/B 分别使用同一核 | 三个结果写回，Alpha 原样保留 |
| R/G/B | 对应字节 | 只替换所选字节，其余 RGB/Alpha 不变 |
| Y/Cb/Cr | 既有全范围 YCbCr 分量 | 保留另外两分量后转回 RGB，统计发生 RGB 裁切的像素 |

Alpha 不进入 V1 卷积。常量边界值按当前输入平面的数值解释；Cb/Cr 的中性建议值是 128，但仍显示用户实际值。
RGB 模式的统计分别保留三通道 raw 范围和裁切数，不能只返回一个无法解释的总数。

### 5.6 梯度 Magnitude

Sobel、Prewitt、Scharr 的 X/Y 模式各执行一个线性核。Magnitude 模式先得到除数后的 `Gx`、`Gy`：

```text
magnitude = sqrt(Gx * Gx + Gy * Gy)
```

偏置只在 Magnitude 形成后应用。Magnitude 不是单一线性卷积，不显示虚构的“等价核”；频率面板改为显示：

```text
combined(u,v) = sqrt(|Hx(u,v)|² + |Hy(u,v)|²)
```

并明确标记这是双核幅频摘要，不含空间域像素的非线性相位组合。

## 6. 核目录与自定义输入

### 6.1 预设目录

预设只生成不可变定义，不执行图片处理：

- Identity：用于基线、调试和 Golden，不作为宣传滤镜；
- Mean：`K×K` 全 1，推荐 `KernelSum`；
- Gaussian：按 `exp(-(x²+y²)/(2σ²))` 生成，推荐 `KernelSum`；
- Motion：根据长度和角度对中心线段进行确定性面积/双线性栅格化，权重非负，推荐 `KernelSum`；
- Sharpen：中心增强、邻域抑制，强度参数直接反映到系数；
- Unsharp Mask：先生成 Gaussian，再组合 `(1+a)δ-aG`，理论核和为 1；
- High Boost：`Aδ-G`，显示 `A` 与理论 DC 增益；
- Sobel/Prewitt/Scharr：成对 X/Y 核与方向定义；
- Laplacian 4/8：零和二阶差分，推荐 bias 128 仅用于有符号显示；
- Emboss：固定若干方向，通过旋转/显式矩阵生成，默认建议 bias 128。

每个预设包含稳定 ID、中文名、参数范围、生成公式、推荐归一化/偏置、线性/双核类型和解释文字。显示文字不是持久协议。

### 6.2 参数范围

- 核尺寸：奇数 `3..31`；
- Gaussian/Unsharp/High Boost sigma：`0.1..5.0`，并验证尺寸能覆盖所选 sigma；不自动静默扩大尺寸；
- Motion 长度：`1..K`，角度归一化到 `[0,180)`；
- Sharpen/Unsharp amount：`0..5`；0 必须精确退化为 identity；
- High Boost `A`：`1..6`；
- Emboss 强度：`0..5`；
- 偏置建议可以由预设给出，但用户已修改时不覆盖。

### 6.3 自定义矩阵编辑

- 3×3/5×5 使用可键盘访问的滚动网格；更大核仍使用同一虚拟化/滚动模型，不一次创建无界控件；
- 支持按行粘贴，行用换行分隔，列接受空格、Tab、逗号或分号；数字协议使用 `.` 小数点；
- 所有行列数必须一致、必须为允许的奇数方形，不能补零猜测用户意图；
- 解析返回带行、列和原因的错误，不通过异常文本驱动 UI；
- 编辑期间保留“最后一次有效核”和当前无效草稿；无效草稿不触发计算，也不清空上一次有效结果；
- 提供“重置为当前预设”“转为自定义”“旋转 90°”“水平/垂直翻转”；这些是显式矩阵操作，不引入命令框架；
- 核和、绝对值和、最小/最大系数、非零数、对称性和可分离提示实时显示；V1 的自定义核即使可分离也走通用正确路径。

## 7. SOLID 架构与朴素设计

### 7.1 依赖方向

```text
Features/ConvolutionPlayground
  ConvolutionPlaygroundDocument    实例状态、命令、Revision、generation、生命周期
  ConvolutionPlaygroundView        布局、绑定、编辑和坐标转发
  KernelGrid / Response Controls   小型绘制与交互，不执行算法
                    │
                    ▼
Application/Convolution
  IPrepareConvolutionSessionUseCase
  IRenderConvolutionPreviewUseCase
  IInspectConvolutionPixelUseCase
  IRenderKernelResponseUseCase
  IRenderFullConvolutionUseCase
  IExportConvolutionImageUseCase
                    │
                    ▼
Domain/Convolution + Domain/Imaging + Domain/Frequency
  核、解析、目录、边界、卷积、双核组合、响应、差异、探针
                    ▲
                    │
Infrastructure
  Avalonia 图片编解码、文件对话框、Bitmap 适配、原子 PNG 写入
```

Domain 不引用 Avalonia、文件、JSON、DI、Document 或 View。Application 不返回 Avalonia `Bitmap`。View 不解析矩阵、
生成核或读取文件。Document 不 `new` 算法实现、不访问 ServiceProvider，也不拥有可写领域缓冲。

### 7.2 SOLID 具体落实

**SRP：**

- `ConvolutionKernel` 只保证核尺寸、锚点、系数和不可变性；
- `ConvolutionKernelParser` 只把文本变为核或结构化错误；
- `ConvolutionPresetFactory` 只从明确参数生成核定义；
- `ConvolutionNormalizer` 只解析有效除数；
- `BorderIndexMapper` 只定义边界索引；
- `SpatialConvolver` 只执行单平面真卷积并响应取消；
- `GradientCombiner` 只组合 Gx/Gy；
- `KernelFrequencyResponseAnalyzer` 只分析有效核；
- `ConvolutionPixelInspector` 只生成某坐标的贡献 DTO；
- 应用用例负责流程，Session 负责资源所有权，Document 负责 UI 状态，View 负责展示。

**OCP：**预设目录通过显式、可审查的工厂分支扩展；新增预设通常只增加定义和测试，不修改空间卷积循环。V1 不使用反射扫描
或动态插件目录。单核与双核由有穷判别联合/枚举表达，避免为每个名字创建空壳 Strategy。

**LSP：**如果均值/Gaussian 使用可分离快速路径，同一请求必须可替换为通用二维执行器，并在容差、边界、舍入前 raw 结果、
取消和输入不变性上满足同一契约；优化失败时可以安全回退到通用路径。

**ISP：**Document 只依赖六个窄用例；导出用例单独依赖 `IImageCodec` 和 `IAtomicFileWriter`。不得创建同时包含选图、核管理、
卷积、FFT、导出和报告的 `IImageService`。

**DIP：**异步流程依赖应用接口，文件和编码依赖既有端口；纯数学类可以直接作为无状态具体依赖注入，不为满足形式而给每个
三行帮助类建立接口。

### 7.3 允许的朴素模式

- **Value Object：**不可变 `ConvolutionKernel`、`ConvolutionRecipe`、`BorderDefinition`；
- **Factory：**`ConvolutionPresetFactory` 集中生成可审查预设；
- **Strategy：**仅当通用二维与可分离执行确实需要可替换时使用一个窄执行契约；
- **Session：**集中拥有源图、代理、有效核和派生结果，并显式释放；
- **Application Use Case：**协调解码、计算和导出。

明确禁止：Mediator、事件总线、Repository、Unit of Work、抽象工厂套抽象工厂、Service Locator、反射发现、通用规则引擎、
通用滤镜流水线和 AIFLOW。设计模式数量不作为质量指标。

## 8. 建议领域与应用契约

### 8.1 领域模型草案

```csharp
internal sealed class ConvolutionKernel
{
    public int Size { get; }
    public int Radius { get; }
    public ReadOnlyMemory<double> Coefficients { get; }
    public double this[int row, int column] { get; }
}

internal enum BorderMode { Constant, Replicate, Reflect101, Wrap }
internal enum KernelNormalizationMode { None, KernelSum, AbsoluteSum, Explicit }
internal enum ConvolutionChannelMode { Rgb, Red, Green, Blue, Luma, ChromaBlue, ChromaRed }
internal enum GradientOutputMode { X, Y, Magnitude }

internal sealed record ConvolutionRecipe(
    ConvolutionOperatorDefinition Operator,
    BorderDefinition Border,
    KernelNormalizationDefinition Normalization,
    double Bias,
    ConvolutionChannelMode Channel);
```

实际代码可调整名称，但必须保留不可变所有权、稳定 ID、单核/双核差异和结构化校验。不得向外暴露可写 `double[]`。

### 8.2 应用用例草案

```csharp
internal interface IPrepareConvolutionSessionUseCase
{
    Task<ConvolutionSession> ExecuteAsync(
        string sourcePath,
        int analysisMaximumEdge,
        CancellationToken cancellationToken);
}

internal interface IRenderConvolutionPreviewUseCase
{
    Task<ConvolutionPreviewResult> ExecuteAsync(
        ConvolutionSession session,
        ConvolutionRecipe recipe,
        CancellationToken cancellationToken);
}

internal interface IInspectConvolutionPixelUseCase
{
    ConvolutionPixelReport Execute(
        ConvolutionSession session,
        ConvolutionPreviewResult preview,
        ImagePoint proxyPoint);
}

internal interface IRenderKernelResponseUseCase
{
    KernelResponseResult Execute(
        ConvolutionRecipe recipe,
        KernelResponseView view,
        CancellationToken cancellationToken);
}

internal interface IRenderFullConvolutionUseCase
{
    Task<FullConvolutionResult> ExecuteAsync(
        ConvolutionSession session,
        ConvolutionRecipe recipe,
        CancellationToken cancellationToken);
}

internal interface IExportConvolutionImageUseCase
{
    Task ExecuteAsync(
        FullConvolutionResult result,
        string outputPath,
        CancellationToken cancellationToken);
}
```

`ConvolutionSession` 拥有完整源图和代理；`ConvolutionPreviewResult` 拥有代理 raw 响应和投影；完整结果独立持有并绑定
recipe fingerprint。导出时再次验证 fingerprint 与当前参数一致，避免竞态导出过期结果。

## 9. 空间执行、优化和资源预算

### 9.1 正确路径

- 通用二维实现是规范事实，复杂度 `O(W·H·K²·C)`；
- 输入始终只读，输出与 raw 缓冲新分配；失败或取消不得部分替换 Session 中的有效结果；
- 每行或固定像素块检查取消；31×31 时不得等整张图片结束后才响应；
- 边界采样和核坐标由共享小函数提供，单核、双核和探针不得各写一套映射；
- 完整尺寸任务不在 UI 线程运行；同一 Document 同时最多一个代理任务和一个完整尺寸任务；新任务取消旧任务；
- 不使用不确定并行归约，以免教学探针与实际执行出现难以解释的尾数差异。

### 9.2 有限优化

- Mean/Gaussian 可使用显式标记的可分离一维核，先水平后垂直；
- 可分离路径保留 double 中间值，只在第二遍完成后统一偏置、舍入和裁切；
- 两遍都使用同一边界定义；Golden 必须证明与通用二维路径在 raw 容差内一致；
- 自定义核、运动核和导数核 V1 默认走通用二维路径，不做运行时 SVD 或自动分解；
- 不增加第三方数学、GPU 或原生依赖。

### 9.3 资源预算

- 实时代理最大 `2048×2048`，默认 `1024`；
- 单通道 raw double 平面在 2048² 时约 32 MiB；RGB 不同时长期保留三份贡献表；
- 代理 Session 长期持有：源 RGBA、代理 RGBA、最终代理 RGBA、必要 raw 平面和显示投影；目标结构化预算不超过约 160 MiB；
- 完整尺寸最多 16,000,000 像素，单个 double 平面约 122 MiB；实现必须逐通道处理并及时释放中间缓冲；
- 完整尺寸 RGB 与 31×31 可能耗时显著，UI 在开始前显示估计乘加规模 `W·H·K²·C` 和取消入口；
- 自动门禁验证数组长度和所有权上限，不对 GC 私有字节写脆弱断言；
- G8 在 1024²/3×3、1024²/31×31 和完整尺寸代表样本上记录本机数据，但不把机器相关毫秒数写成发布承诺。

## 10. 频率响应、差异与可解释重建

### 10.1 核频率响应

固定把有效核 `h/d` 的中心锚点搬移到 `256×256` 周期网格的 `(0,0)`，执行二维 FFT。显示坐标使用 fftshift，中心为 DC。

必须输出：

- 2D 对数幅值和相位视图；
- DC 增益、最大幅值及其归一化坐标；
- 中心水平/垂直幅值截面；
- 单核的核和与 `H(0,0)` 一致性；
- 双核 Magnitude 的组合幅频摘要；
- “偏置、边界、裁切和 Magnitude 非线性不包含在响应中”的固定说明。

Identity 的幅值应全为 1；归一化均值/Gaussian 的 DC 应为 1；Laplacian 和一阶导数的 DC 应近似 0。使用直接 DFT 的
小核参考实现作测试 oracle，避免仅以同一 FFT 实现自证。

### 10.2 差异

- 绝对差异：复用/适配现有有界 RGB 差异语义，支持 1×/4×/16× 显式放大；
- 有符号差异：按所选分析通道显示，零映射为中性灰，正负使用可区分且带数值图例的双色；
- 差异统计：每通道 MAE、RMSE、最大绝对差、变化像素数、低/高裁切数；
- 可选显示既有 PSNR/全局 SSIM，但明确它们不适用于评价边缘核“好坏”；
- 所有差异都基于同尺寸代理或同尺寸完整结果，禁止隐式缩放后比较。

### 10.3 像素探针与贡献解释

点击原图、结果或差异中的任一视图，统一选择代理坐标，并显示：

- 源 RGBA、结果 RGBA、每通道差值；
- 当前处理平面的源值、raw 累加值、有效除数、除后值、偏置后值、舍入值和最终字节；
- 是否发生边界采样、低端裁切、高端裁切或 YCbCr 回写裁切；
- 每个非零核项的 `(kx,ky)`、原始采样坐标、映射后坐标/常量、样本值、系数和乘积；
- 所有乘积之和与执行器 raw 结果的容差校验；
- 双核模式分别显示 Gx/Gy 和最终 Magnitude，不把两套贡献混成一列。

大核贡献表可滚动并按绝对贡献排序/核行顺序切换，但计算 DTO 必须保留完整非零项；UI 优化不能改变数值事实。

## 11. Document 异步、失效与失败边界

### 11.1 generation 与防抖

- 图片、有效核、边界、归一化、偏置、通道或代理档位改变都会推进 generation；
- 文本输入通过 200 ms 左右短防抖，只对最后一次完整有效核启动任务；
- 新 generation 取消旧代理、响应和完整尺寸任务；旧任务即使迟到也不得替换 Bitmap、统计或导出对象；
- 选中像素只刷新探针，不重新卷积；切换幅值/相位显示只重新投影，不重新执行空间卷积；
- 瞬时指针悬停不标记 Dirty；持久参数和点击选中坐标才推进 Document Revision。

### 11.2 命令状态

- 没有有效图片或有效 recipe 时禁用预览/完整尺寸/导出；
- 正在代理计算时允许取消和继续编辑，新有效参数替换排队目标；
- 完整尺寸计算中允许代理继续响应新参数，但新参数会取消并使旧完整结果失效；
- 只有当前 recipe fingerprint 对应的完整结果允许导出；
- 导出使用临时文件和原子替换，取消/失败不得留下看似成功的目标文件。

### 11.3 可恢复错误

结构化区分：图片解码失败、核语法错误、核参数越界、除数近零、资源预算拒绝、计算取消、计算失败、编码失败和写入失败。
参数错误保留最后有效结果和草稿；换图失败不应销毁仍可观察的旧 Session，除非用户显式清空。

## 12. 界面与交互设计

### 12.1 建议布局

```text
┌ 选择图片 | 代理档位 | 执行完整尺寸 | 导出 PNG | 取消 ┐
├──────────────┬─────────────────────────────┤
│ 核与参数面板   │ 2×2 联动视图                   │
│ - 预设/自定义  │ 原图        结果               │
│ - 核矩阵编辑   │ 差异        频率响应            │
│ - 边界/常量    │                             │
│ - 归一化/除数  │ 同步缩放、坐标十字与尺寸标签     │
│ - 偏置/通道    │                             │
├──────────────┴─────────────────────────────┤
│ 核摘要 | raw/裁切统计 | 频响截面 | 像素贡献表    │
├────────────────────────────────────────────┤
│ 状态、代理/完整尺寸标识、进度、错误和解释提示   │
└────────────────────────────────────────────┘
```

窄窗口改为分区 Tab/可滚动布局，不强制四图缩到不可读。布局变化只影响展示，不改变 Session 或计算。

### 12.2 交互规则

- 预设变化先显示实际矩阵，再计算，不能只显示名称；
- 用户编辑预设矩阵时显式“转为自定义”，避免目录参数与矩阵事实分叉；
- 所有数值输入显示范围、单位和验证原因；错误不使用只有红色边框的提示；
- 原图、结果、差异共享缩放和平移；频响共享选点但使用独立频率坐标；
- 图像面板标题始终显示 `代理 1024×…` 或 `完整尺寸 W×H`；
- 边缘核默认可以建议“有符号差异/偏置 128”，但不在用户不知情时改值；
- 颜色不是唯一信息载体；正负差异同时使用符号、数值、图例和不同明度；
- 主要命令可键盘到达，自定义单元格有行列名称，图像和曲线提供可读摘要；
- Help 入口解释卷积/相关、边界、DC、负响应、裁切和代理/完整尺寸区别。

## 13. 中文注释与设计说明规定

新生产代码必须使用中文 XML 注释或块注释详细说明“为什么”和稳定语义，至少覆盖：

- 核矩阵的行列到 `(kx,ky)` 映射，以及为什么执行器读取 `f(x-kx,y-ky)`；
- 四种边界的精确序列，尤其 Reflect-101 与重复边界镜像的区别；
- 归一化失败为何阻断，而不是静默回退；
- Gaussian、Unsharp、High Boost、Sobel/Prewitt/Scharr、Laplacian 的公式、方向和 DC 特征；
- Motion 核离散化导致的实际权重与理想连续线段差异；
- 双核 Magnitude 为什么不是单一线性卷积；
- 核中心搬移到 FFT 原点、fftshift 和频率坐标；
- 偏置、裁切、边界为什么不属于理想核频率响应；
- raw double、重建字节、代理、完整尺寸结果和 Bitmap 的所有权与释放顺序；
- generation、取消和 recipe fingerprint 如何阻止迟到/过期结果；
- 为什么 Alpha 不参与，YCbCr 回写为什么可能裁切；
- 可分离优化与通用二维路径的等价条件和回退方式。

以下注释不合格：逐字翻译代码、只写“执行卷积”、为每个显然属性写重复说明、宣称“高性能”却没有边界和证据。
复杂类型的 `<remarks>` 应包含设计思路；关键公式旁应给变量含义和测试落点。

## 14. 单元测试、集成测试与质量门禁

### 14.1 核值对象、解析和目录

- 尺寸 2/4/32、非方形、系数数量不匹配、NaN/Infinity、绝对值越界全部拒绝；
- 构造后修改原数组不能改变核；对外不能取得可写缓冲；
- 3×3、5×5、多分隔符粘贴成功；空格、缺列、多列、坏数字报告准确行列；
- Identity、Mean、Gaussian、Motion、Sharpen、Unsharp、High Boost、三类梯度、Laplacian、Emboss 系数 Golden；
- Gaussian 对称、非负、归一化和 sigma/尺寸边界；Motion 角度规范化与重复生成确定；
- `amount=0` 的 Unsharp 精确 identity；High Boost 核和与 `A-1` 一致；导数/Laplacian 核和为 0。

### 14.2 真卷积与边界 Golden

- 单 impulse 输入输出能看出非对称核方向，防止实现成相关；
- 手算 2×3、3×3 灰度矩阵覆盖中心、四边和四角；
- Constant/Replicate/Reflect101/Wrap 分别覆盖 `n=1`、`n=2`、负多周期和正多周期索引；
- 核比图片宽/高时仍按定义工作；
- None/KernelSum/AbsoluteSum/Explicit 的有效除数和近零阻断；
- 偏置、AwayFromZero 舍入、低/高裁切和 raw 范围 Golden；
- 输入 `PixelImage` 与输入平面逐字节/逐 double 不变；取消在大循环中抛出，不返回半成品。

### 14.3 通道与算子

- RGB 三通道分别与单通道 oracle 一致，Alpha 逐字节不变；
- R/G/B 模式未选通道不变；Y/Cb/Cr 使用既有转换并报告裁切；
- 常量图在归一化低通的内部保持常量；导数/Laplacian 内部为零；
- 横/纵 ramp 和阶跃验证 Sobel/Prewitt/Scharr 方向、符号和相对尺度；
- Magnitude 与手算 `sqrt(Gx²+Gy²)` 一致，且不应用两次偏置；
- Unsharp 与 `f+a(f-Gf)`、High Boost 与 `Af-Gf` 的独立 oracle 一致；
- 可分离 Mean/Gaussian 与通用二维 raw 结果在固定容差内一致，并覆盖全部边界模式。

### 14.4 频率响应与解释

- 小核直接 DFT 与 FFT 响应逐点对照；
- Identity 全幅值 1、相位 0；归一化 Mean/Gaussian 的 DC 为 1；导数/Laplacian DC 为 0；
- 核旋转/平移约定的相位 Golden，防止错误 fftshift；
- 双核组合幅值与独立 Hx/Hy oracle 一致；
- 改 bias 不改变频响；改 divisor 按比例改变频响；
- 探针贡献之和等于执行器 raw 累加，边界映射坐标和常量标志正确；
- 有符号差异零点、正负、图例范围和绝对差异放大 Golden。

### 14.5 用例、Session、Document 与 View

- 解码一次建立原图/代理，参数预览不重复读文件；
- 新 recipe 取消旧任务，迟到结果不能覆盖；无效草稿保留最后有效结果；
- 完整结果绑定 fingerprint，参数改变后导出禁用；
- PNG 编码后回读与内存完整结果逐字节一致；写入失败不留下成功状态；
- 快照不含像素/Bitmap/raw，未知枚举和坏核安全恢复；恢复不自动运行；
- 两个 Document Scope 的图片、核、取消、完整结果和 Bitmap 完全隔离；
- Dispose 取消工作并释放每个 Bitmap 一次；
- 第九个贡献 ID 唯一、DI 可解析、Standalone 使用真实 Module；
- Headless View 加载成功，主要控件绑定、错误文本、尺寸标签、键盘顺序和窄窗口布局可验证；
- 扫描生产代码/登记清单，确保没有 AIFLOW、Workflow Action、Workbench Command、反射算子发现或通用 DAG。

### 14.6 资源与回归

- 31×31、2048 代理、16 MP 完整图的缓冲长度在分配前受控，乘法使用 checked/long；
- 频响网格固定 256×256，不随完整图尺寸增长；
- 快速连续编辑、换图、关闭和取消没有旧 Bitmap 回闪；
- 提炼 Gaussian/Unsharp 前后既有 Robustness Golden 逐像素不变；
- 既有 241 项全部继续通过，不降低阈值、不改为 skip、不把旧测试计作新增能力证据；
- 新能力测试数在 G9 记录实际 runner 数，不预先捏造目标数字；零失败、零跳过是硬门禁。

### 14.7 本地开发门禁命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

每个 G 包至少执行受影响测试与 Debug build；G9 执行上述完整顺序并记录实际输出。不得新增 Windows CI workflow，
不得把本地 Release 配置构建称为“发布门禁”或“已发布”。

## 15. G0–G9 交付与验收

### G0：产品与数值基线

交付：本文决策冻结、手算 3×3 Golden、卷积/边界/归一化/通道/频响协议、资源预算和 UI 草图。

验收：争议语义均有唯一答案；当前 241 基线实际可复现；没有生产代码、占位贡献或完成声明。

### G1：核领域与目录

交付：不可变核、recipe、解析器、校验器、normalizer、预设工厂和目录说明。

验收：所有核和解析 Golden 通过；Domain 无 Avalonia/文件/DI；工厂无反射；中文注释说明公式与方向。

### G2：空间卷积核心

交付：边界映射、通用单平面执行器、通道协调、raw/裁切统计和可取消计算。

验收：真卷积、四边界、四归一化、偏置、舍入、六通道、RGB 和 Alpha 门禁通过；输入不变。

### G3：预设与组合算子

交付：平滑、运动、锐化、Unsharp、High Boost、三类双核梯度、Laplacian、Emboss 和有限可分离优化。

验收：独立公式 oracle 通过；Magnitude 明确非线性；优化可由通用路径替换；Robustness 回归不变。

### G4：响应、差异与解释

交付：256² 核频响、幅值/相位/截面、双核摘要、绝对/有符号差异、像素贡献 DTO。

验收：直接 DFT oracle、DC/相位 Golden 和贡献求和通过；偏置/裁切局限在 UI 与文档可见。

### G5：应用、Session 与导出

交付：六个窄用例、Session、代理预览、完整尺寸显式执行、recipe fingerprint、PNG 原子导出。

验收：应用层无 Bitmap；取消/失败不提交半成品；过期结果不能导出；编码后逐字节回读通过。

### G6：Persistable Document

交付：稳定 ID、Module/DI 登记、schema 1、命令状态、generation、多 Scope、Bitmap 替换和关闭释放。

验收：第九个贡献唯一；快照轻量；恢复不自动运行；两个实例隔离；迟到结果门禁完整。

### G7：联动 UI 与无障碍

交付：核参数、自定义矩阵、四视图、频响截面、贡献表、尺寸/状态说明、帮助目录和 Headless View。

验收：键盘、高对比、窄窗口和无颜色辨识可完成主要流程；View 无算法/文件/生命周期逻辑。

### G8：质量强化

交付：全数值回归、边界极值、资源结构、取消风暴、生命周期、架构扫描、性能记录和兼容测试。

验收：既有测试零回退；无 flaky 时间断言；31×31 和 16 MP 风险有数据、有取消、有清晰限制。

### G9：本地封板与文档

交付：执行 14.7 全门禁；补齐专用文档和 G0–G9 历史；同步公共入口、未来能力和共享边界；记录有限人工验收。

验收：实际测试数、零跳过、命令输出、未执行事项和回滚方式可追踪；无 AIFLOW、Windows CI 或发布完成声明。

## 16. 预计代码、测试与文档落点

### 16.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Domain/Convolution/
│  ├─ ConvolutionKernel.cs
│  ├─ ConvolutionRecipe.cs
│  ├─ ConvolutionKernelParser.cs
│  ├─ ConvolutionPresetFactory.cs
│  ├─ ConvolutionNormalizer.cs
│  ├─ BorderIndexMapper.cs
│  ├─ SpatialConvolver.cs
│  ├─ GradientCombiner.cs
│  ├─ KernelFrequencyResponseAnalyzer.cs
│  ├─ ConvolutionDifferenceProjector.cs
│  └─ ConvolutionPixelInspector.cs
├─ Application/Convolution/
│  ├─ ConvolutionContracts.cs
│  ├─ PrepareConvolutionSessionUseCase.cs
│  ├─ RenderConvolutionPreviewUseCase.cs
│  ├─ InspectConvolutionPixelUseCase.cs
│  ├─ RenderKernelResponseUseCase.cs
│  ├─ RenderFullConvolutionUseCase.cs
│  └─ ExportConvolutionImageUseCase.cs
└─ Features/ConvolutionPlayground/
   ├─ ConvolutionPlaygroundDocument.cs
   ├─ ConvolutionPlaygroundView.axaml
   ├─ ConvolutionPlaygroundView.axaml.cs
   ├─ KernelGridControl.cs
   ├─ KernelResponseControl.cs
   ├─ ConvolutionDifferenceControl.cs
   └─ ConvolutionHelpCatalog.cs
```

文件可按实际职责小幅合并或拆分。不得把解析、卷积、FFT、导出和 Document 堆进单一巨型类，也不得制造大量只转发一次的接口。

### 16.2 测试

```text
tests/ImageLabPlugin.Tests/
├─ ConvolutionKernelAndParserTests.cs
├─ ConvolutionPresetTests.cs
├─ SpatialConvolverTests.cs
├─ ConvolutionBorderAndChannelTests.cs
├─ ConvolutionOperatorTests.cs
├─ KernelFrequencyResponseTests.cs
├─ ConvolutionDifferenceAndInspectorTests.cs
├─ ConvolutionUseCaseTests.cs
├─ ConvolutionPlaygroundDocumentTests.cs
└─ ConvolutionPlaygroundViewTests.cs
```

可依据测试规模合并文件，但 14 节每项门禁必须能追溯到明确用例。

### 16.3 专用文档

实施过程中按现有能力目录惯例，在 `docs/design/convolution-playground/` 同步：

- `README.md`：能力入口、阅读顺序、状态和“不是美图滤镜集合”的边界；
- `user-manual.md`：面向新手解释核、边界、归一化、偏置、负响应、代理和完整尺寸；
- `guide.md`：准确描述所有参数、状态、矩阵文本、通道、导出、失败和限制；
- `kernel-catalog.md`：每个预设的稳定 ID、矩阵/公式、参数范围、推荐设置、方向和 DC 特征；
- `mathematical-principles.md`：真卷积、边界、可分离性、梯度、Laplacian、Unsharp/High Boost、DFT 响应；
- `testing.md`：Golden 来源、容差、命令、实际测试数、已证明和未证明事项；
- `implementation.md`：本文，持续反映计划和实际状态；
- `history/README.md` 与 G0–G9：实际实施证据，不替代当前指南。

还需同步仓库 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md`、
`docs/design/shared/image-domain-boundaries.md`、必要的项目/窗口职责文档，以及 Robustness 文档中共享 Gaussian/Unsharp 的实现位置。
文档同步跟随每个 G 包，不能等 G9 一次补写；规划阶段公共入口曾只标注“规划中”，完成后已按实际证据更新。

## 17. 有限人工验收清单

1. 用 3×3 非对称自定义核和 impulse 图片确认显示矩阵、真卷积方向及探针贡献；
2. 在小尺寸棋盘图上逐一切换 Constant/Replicate/Reflect101/Wrap，核对四角结果；
3. 切换 None/KernelSum/AbsoluteSum/Explicit，确认有效除数、近零阻断、偏置和裁切计数；
4. 用常量图检查 Mean/Gaussian 保持、Sobel/Prewitt/Scharr/Laplacian 内部零响应；
5. 用横纵阶跃检查三类梯度的 X/Y 符号、Magnitude 和频响方向；
6. 比较 Sharpen、Unsharp、High Boost 的实际矩阵、DC 增益和 raw 范围，确认没有“恢复真实细节”措辞；
7. 检查 Motion 不同角度的离散权重、核和、空间结果和频响主方向；
8. 切换 RGB、R/G/B、Y/Cb/Cr，核对未选通道、Alpha 和 YCbCr 裁切说明；
9. 在原图/结果/差异中点击同一位置，逐项复算贡献、除数、偏置、舍入和最终字节；
10. 输入不规则 5×5、NaN、空单元格、偶数核和 33×33，确认保留上次有效结果并显示具体错误；
11. 快速编辑核、切换通道、换图、取消和关闭，旧结果不得闪回，Bitmap 不得继续更新；
12. 执行代理和完整尺寸，检查尺寸标签、耗时提示、stale 状态和 PNG 编码后回读；
13. 保存/恢复 Document，确认不保存像素/结果且不自动读取或运行；同时打开两个实例检查隔离；
14. 使用键盘、高对比、窄窗口和无颜色辨识完成主要流程并读懂等价数值说明。

人工清单在 Standalone 中只证明开发期交互，不证明真实 Host、Windows 平台矩阵、ZIP、安装或发布行为。未执行项必须在 G9 如实延期。

## 18. 回滚与兼容策略

1. 先从 Module 隐藏第九个贡献，同时保留稳定 ID 的安全快照识别；
2. 再移除 Feature View/Document 和专用应用用例登记；
3. 再移除响应、解释和完整尺寸协调；
4. 最后移除独立 Convolution 领域；
5. 若 Gaussian/Unsharp 已被 Robustness 复用，不得删除共享原语，必须恢复到通过既有逐像素 Golden 的实现；
6. 不修改或回滚 Frequency、Comparison、Robustness、水印、指纹、位平面和 LSB 的稳定行为；
7. 已导出的 PNG 是用户文件，回滚不得删除、移动或覆盖；
8. schema 变化使用显式版本分支和测试，不通过吞异常/返回默认图来伪装兼容。

## 19. 完成定义

只有同时满足以下条件，才可把计划状态改为“开发实现与本地自动门禁完成”：

- G0–G9 均有实际历史记录，所有状态基于证据而不是预先勾选；
- 预设和 3×3 至 31×31 自定义核形成编辑、代理预览、解释、完整尺寸执行和 PNG 导出闭环；
- 真卷积、四边界、四归一化、偏置、舍入、裁切、六单通道、RGB、Alpha 语义均被 Golden 固定；
- 单核、双核 Magnitude、频率响应、差异和像素贡献的数学说明与实现一致；
- SOLID 分层、朴素 Factory/Value Object/Session、窄接口、取消、generation 和 fingerprint 有自动测试；
- 新生产代码中文注释覆盖算法、坐标、数值风险、资源所有权、优化等价和设计取舍；
- Debug/Release locked 本地门禁零失败、零跳过、零警告，实际新增与总测试数如实记录且总数大于 241；
- 专用文档、公共入口、未来能力状态和共享边界同步；
- 生产代码和贡献清单中没有 AIFLOW、Workflow Action、Workbench Command、通用 DAG 或滤镜市场；
- 没有新增 Windows CI，也没有声称完成真实 Host、ZIP、安装/卸载或发布验收。

## 20. 发布阶段明确延期

以下内容不属于本轮开发完成条件，正式准备发布时再按 `docs/design/shared/deployment-and-release.md` 执行：

- Windows CI 与目标平台矩阵；
- 正式 ZIP、manifest、哈希、依赖闭包和可复现打包；
- 真实 Host Catalog/Dock、多实例恢复、安装、升级、卸载和回滚；
- 不同 Windows 版本、DPI、主题、GPU 和权限环境；
- 大规模自然图片数据集上的视觉评价和参数推荐校准；
- 16 MP、31×31、多实例长时间内存、取消和资源泄漏压力；
- GPU/SIMD/FFT 大核加速、性能 SLA 和发布级兼容承诺；
- 发布说明、截图、许可证复核和对外支持策略。

本地 Release 配置只表示第二编译配置回归，不等于发布。开发阶段完成后，产品仍应准确描述为“可解释的空间卷积实验台”，
而不是通用图片编辑器、专业修复工具或实时 GPU 滤镜引擎。
