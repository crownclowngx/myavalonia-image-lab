# ImageLabPlugin V1 Frequency Mask Editor／频谱遮罩编辑器实施计划

> 计划状态：V1 已完成本地开发实现与 G0–G9 自动门禁；已登记第十二个 Persistable Document，尚未执行发布门禁<br>
> 基线日期：2026-08-31<br>
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3<br>
> 起始证据：G0 实跑 locked restore 成功，Debug/Release warn-as-error build 均 0 警告、0 错误，两配置 test 均 362/362 通过、0 失败、0 跳过<br>
> 完成证据：G9 实跑 Debug/Release build 均 0 警告、0 错误，两配置 test 均 407/407 通过、0 失败、0 跳过；`git diff --check` 通过<br>
> 核心路线：有界分析代理 + 共享 FFT Session + 实数共轭对称增益遮罩 + 可重放编辑配方 + 防抖 IFFT + 空间域重建与解释性诊断<br>
> 首要规定：SOLID 优先；设计模式朴素使用；生产代码使用详细中文注释；数值、资源和生命周期门禁先于 UI

本文定义 ImageLab 的下一项能力、计划中的第十二个多实例 Persistable Document。它允许用户直接在中心化频谱上
绘制、擦除、创建圆环或矩形、锁定频带、反转遮罩并调节全局遮罩强度，同时观察空间域重建结果。

本文既保留 V1 决策基线，也记录已经完成的实施结果。每个实施包的实际代码、测试、数值证据、边界和回滚方式见
`history/gN-*.md`；测试数量和完成状态来自本地实跑，不由计划推断。

## 1. 决策摘要

### 1.1 产品形态

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Frequency Mask Editor／频谱遮罩编辑器` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID | `myavalonia.plugin.image.lab.document.frequency-mask-editor` |
| 显示分类 | `图像分析` |
| 输入 | 用户显式选择的一张 PNG/JPEG 图片 |
| 分析通道 | R、G、B、Y、Cb、Cr 六个单通道 |
| 实时处理 | 512/1024/2048 最大边分析代理；默认 1024 |
| 初始遮罩 | 全通，所有增益为 1 |
| 编辑工具 | 衰减画笔、恢复橡皮、矩形、圆环、频带锁定、全部反转、全部重置 |
| 实值安全 | 每一次写入都原子更新频点及其共轭点；用户不能关闭该约束 |
| 遮罩强度 | 全局 `s ∈ [0,1]`，有效增益 `H = 1 - s + sM` |
| 重建 | 只修改当前选定通道；Alpha 逐字节保持；显示虚部残差、裁切与质量变化 |
| 历史 | 有界操作记录 + 确定性重放，不保存每一步完整 2048² 遮罩 |
| 导出 | 当前配方一致的重建 PNG、遮罩预览 PNG 和版本化遮罩配方 JSON |
| 模式使用 | 不可变 Recipe/Operation/Result、普通 sealed 服务、窄用例和构造注入 |
| 外部依赖 | 不新增 NuGet、原生 FFT、图表或画布框架 |
| 明确排除 | AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP、真实 Host 与发布门禁 |

### 1.2 用户闭环

```text
选择图片、通道和分析档位
    ↓
建立分析代理并缓存一次只读全局 FFT
    ↓
在中心化频谱上选择画笔、橡皮、矩形或圆环
    ↓
所有修改自动落到共轭频点；可选径向频带锁定
    ↓
即时更新遮罩覆盖层；节流/防抖执行最后一次 IFFT
    ↓
联动查看原图、原频谱、有效遮罩、重建图与差异
    ↓
查看虚部残差、通过能量、裁切、PSNR/SSIM 和频点探针
    ↓
按需撤销、重做、反转、重置或调整全局遮罩强度
    ↓
导出与当前 Session、遮罩配方和强度指纹一致的结果
```

### 1.3 固定实施顺序

1. G0 冻结产品语义、数学、资源和起始门禁；
2. G1 提取可复用的实数增益遮罩应用核心，并保护 Frequency Filter 回归；
3. G2 建立不可变遮罩配方、编辑操作、校验、指纹和有界历史；
4. G3 完成画笔路径、橡皮、矩形、圆环、频带锁定、反转与共轭原子写入；
5. G4 完成有效遮罩、IFFT、通道重建和解释性诊断；
6. G5 完成 Application Session、用例、缓存、取消和导出；
7. G6 接入 Persistable Document、快照、撤销/重做和组合根；
8. G7 完成编辑画布、联动视图、Standalone 与 Headless 交互；
9. G8 同步专用文档、索引、用户说明、数学说明和测试证据；
10. G9 复跑 Debug/Release 全量本地门禁并完成本地开发封板。

不得先写一个在 UI 中“看起来能画”的像素层，再补共轭对称和可测试的坐标规则。遮罩数学、编辑操作和画布坐标必须先在
Domain 中证明，再接入 Pointer 事件。

## 2. 当前项目事实与复用边界

### 2.1 当前基线

仓库当前已经具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集与 `ImageLabPlugin.Standalone` 本地开发承载；
- 十一个多实例 Persistable Document，当前没有 Tool、Workflow Action 或 Workbench Command；
- `PixelImage`、`ImageSize`、图片大小与编码输入预算；
- `ImageChannelConverter` 的 R/G/B/Y/Cb/Cr 抽取和选定通道回写；
- `ImageAnalysisProxyProjector` 的 512/1024/2048 有界抗混叠分析代理；
- `Fft1DTransform`、`Fft2DTransform`、`FrequencySpectrum` 和最多 2048² 的共享 FFT 预算；
- `FrequencyCoordinates` 的自然索引、中心化显示坐标、cycles/pixel、径向半径和共轭索引；
- Spectrum Inspector 的幅度谱、频点信息、频带和实值重建门禁；
- Frequency Filter 的 `double` 增益、工作频谱、IFFT 虚部 `1E-8` 门禁、投影和副作用诊断；
- 差异图、`FullReferenceQualityAnalyzer`、正式 PNG 编解码与原子写入；
- Document Scope、generation、取消、轻量快照、Headless View 和 Standalone 复用惯例；
- 最近封板记录中的 362/362 双配置本地测试证据。G0 必须重新执行，不能把历史记录冒充新能力起点。

### 2.2 必须直接复用

- FFT/IFFT、频率坐标、共轭索引、通道转换和分析代理只能有一份事实源；
- 图片选择、解码、PNG 编码和原子写入继续走现有窄端口；
- 幅度谱、差异投影、PSNR/SSIM 和 Alpha 保持规则继续复用现有实现；
- 数值算法为无状态 singleton；Session、遮罩、历史、Bitmap、取消和结果属于各自 Document Scope；
- Standalone 从真实 Module 和 DI 解析真实 Document/View，不复制一套演示业务。

### 2.3 禁止的错误复用

- 新 Document 不持有或调用 `SpectrumInspectorDocument`、`FrequencyFilterDocument`；Document 不是服务；
- 不读取另一个 Document 的 Session、Bitmap、选择状态或取消源；
- 不把 `FrequencyBandMaskFactory` 扩成同时负责自由绘制、历史、IFFT、文件和 UI 的万能类；
- 不复制 `FrequencyFilterEngine` 的 FFT 工作副本和虚部检查循环；
- 不把 Avalonia `Point`、PointerEvent 或 Bitmap 放进 Domain；
- 不修改 Spectrum Inspector 和 Frequency Filter 的用户语义、稳定 ID 或快照 schema；
- 不为了未来插件化建立反射扫描、抽象工厂、事件总线或“滤镜节点”框架。

### 2.4 共享核心的最小重构

G1 计划从现有 Frequency Filter 中提取两个朴素公共类型，名称可在实现时按现有命名微调，但职责不得改变：

```text
Domain/Frequency
  FrequencyGainMask       不可变 width/height/double gains/fingerprint
  FrequencyMaskApplier    复制只读频谱、逐点乘实数增益、IFFT、裁取和虚部门禁
```

`FrequencyFilterMask` 继续保存径向样本和滤波器配方信息，但组合或委托给 `FrequencyGainMask`；
`FrequencyFilterEngine` 保留现有公开给仓库内部的入口，并委托给 `FrequencyMaskApplier`。先用现有 362 项回归保护，
再做提取；如果提取需要改变现有数值结果或公开语义，则停止并重新设计，不允许复制第二套 FFT 应用代码。

这里不创建 `IFrequencyMask`。两个本地消费者共享一个不可变值对象已经足够，接口只有出现真实替换边界时才增加。

## 3. V1 范围

### 3.1 必须完成

- 选择 R/G/B/Y/Cb/Cr 单通道和 512/1024/2048 代理档位；
- 原图、中心化幅度谱、遮罩覆盖层、有效遮罩、重建图和差异联动；
- 衰减画笔：把命中频点向选定目标增益 `[0,1]` 混合；
- 恢复橡皮：把命中频点向全通增益 1 混合；
- 可拖拽或参数输入的轴对齐矩形；
- 可拖拽或参数输入的任意中心圆环，内半径严格小于外半径；
- 每个点和其共轭点原子更新，自共轭点只更新一次；
- 可选 DC 中心径向频带锁定 `0 ≤ inner < outer ≤ 1`；
- 全部反转、全部重置、撤销、重做；
- 全局遮罩强度 `s`，且调整强度不破坏原始编辑遮罩；
- 画布即时更新，重建采用节流/防抖、取消和最后 generation 获胜；
- 显示当前点的显示/自然索引、频率、共轭点、原幅值、编辑增益和有效增益；
- 显示通过样本比例、平均增益、频谱能量保留比、最大虚部残差、裁切、PSNR/SSIM；
- 当前代理结果和预算内原尺寸结果明确区分；
- 重建 PNG、遮罩预览 PNG 和版本化遮罩配方 JSON 导入/导出；
- 多 Scope、快照、恢复、资源释放和完整本地测试门禁；
- 专用 README、实施计划、数学、测试、指南、用户说明、配方 schema 和历史记录。

### 3.2 明确不实现

- 关闭共轭约束的单边绘制、复数增益、负增益或相位编辑；
- 自动频谱峰检测、自动陷波、周期噪声识别或“智能去条纹”；
- 套索、魔棒、文本、图片粘贴、Bezier、羽化选择和图层混合系统；
- RGB 三通道同时独立编辑和三份实时 FFT；
- 遮罩动画、关键帧、宏录制、批处理或通用滤镜链；
- 跨 Document 全局遮罩仓库、素材管理器或云同步；
- 超过共享 2048² FFT 预算的伪原尺寸处理、分块 FFT 或缩放回填；
- GPU、原生 FFT、SIMD 专用分支和新 NuGet；
- 把重建结果描述成“自动修复”或“质量提升”；
- AIFLOW、Windows CI、ZIP、真实 Host、安装升级和发布门禁。

自动峰检测和专用 notch 语义属于后续 `Periodic Noise Removal`，不能借圆环工具提前混入本能力。

## 4. SOLID 架构

### 4.1 依赖方向

```text
Features/FrequencyMaskEditor
  FrequencyMaskEditorDocument        每实例状态、命令、历史、Revision、取消和 Bitmap
  FrequencyMaskEditorView            布局与编译绑定
  FrequencyMaskCanvasControl         只绘制并转发归一化指针意图
                    │
                    ▼
Application/FrequencyMaskEditing
  PrepareSessionUseCase              解码、代理、通道与只读 FFT Session
  RenderMaskUseCase                  配方重放、有效遮罩、IFFT、投影和诊断
  RenderFullResultUseCase            预算内原尺寸显式执行
  Import/ExportRecipeUseCase         schema、校验与原子文本写入
  ExportImageUseCase                 当前指纹一致的 PNG 导出
                    │
                    ▼
Domain/FrequencyMaskEditing          Domain/Frequency + Domain/Imaging + Domain/Comparison
  Recipe、Operation、Rasterizer、History、Diagnostics
                    ▲
                    │
Infrastructure
  既有图片编解码、文件对话框、JSON 边界和原子文件写入
```

Feature 只依赖 Application；Application 编排 Domain 和既有端口；Domain 不知道 Avalonia、文件系统、JSON、DI、
Document 或 Host SDK。序列化 DTO 属于 Infrastructure/Application 边界，不污染领域操作模型。

### 4.2 单一职责

| 类型 | 唯一职责 | 明确不负责 |
| --- | --- | --- |
| `FrequencyMaskRecipe` | 保存基线、全局强度和有序操作 | FFT、Bitmap、文件 |
| `FrequencyMaskOperation` | 表达一次稳定、可重放的编辑意图 | 直接持有画布或 Session |
| `FrequencyMaskRecipeValidator` | 校验数量、坐标、半径、有限值和预算 | 修复 UI 状态 |
| `FrequencyMaskRasterizer` | 把配方确定性重放为共轭对称增益网格 | IFFT、导出、历史 UI |
| `ConjugateMaskWriter` | 原子更新频点和共轭点 | 判断工具类型 |
| `MaskEditHistory` | 管理有界 undo/redo 操作序列 | 保存整张遮罩快照 |
| `FrequencyMaskDiagnostics` | 统计对称、增益、能量和编辑覆盖 | 修改遮罩 |
| `FrequencyMaskEditorSession` | 拥有一次解码、代理、通道和只读 FFT | 跨 Scope 缓存、Bitmap |
| `FrequencyMaskEditorDocument` | 管理实例状态、命令和生命周期 | 像素/FFT 循环、JSON/IO 实现 |
| `FrequencyMaskCanvasControl` | 绘制图层并把指针转成归一化意图 | 直接改 Domain 数组或执行 IFFT |

任何同时出现 Pointer 事件、频率数组循环和文件 IO 的类型都违反 SRP，不能合入。

### 4.3 开闭原则与朴素模式

V1 使用三种足够朴素的手段：

- 不可变值对象表达 Recipe、Operation 和 Result；
- 一个完整 `switch` 的 Rasterizer 处理固定四类几何操作；
- 有界操作记录提供 undo/redo，行为类似 Command，但不创建一类一个接口实现的对象层次。

V1 不使用 Strategy、Visitor、Mediator、Event Bus、通用画布框架或反射式工具目录。将来只有当新增工具确实由独立团队、
独立生命周期或第三方扩展提供时，才评估接口。当前优先保证坐标、顺序和数值语义一眼可读。

### 4.4 接口隔离

建议应用边界：

```csharp
internal interface IPrepareFrequencyMaskEditorSessionUseCase
{
    Task<FrequencyMaskEditorSession> ExecuteAsync(
        FrequencyMaskSessionRequest request,
        CancellationToken cancellationToken);
}

internal interface IRenderFrequencyMaskUseCase
{
    Task<FrequencyMaskRenderResult> ExecuteAsync(
        FrequencyMaskEditorSession session,
        FrequencyMaskRecipe recipe,
        CancellationToken cancellationToken);
}

internal interface IRenderFullFrequencyMaskUseCase { /* 只负责预算内原尺寸 */ }
internal interface IImportFrequencyMaskRecipeUseCase { /* 只负责读取、schema 与校验 */ }
internal interface IExportFrequencyMaskRecipeUseCase { /* 只负责版本化 JSON */ }
internal interface IExportFrequencyMaskImageUseCase { /* 只负责当前结果 PNG */ }
```

导入、配方导出和图片导出不能塞进一个“万能保存服务”。如果现有文件对话框接口无法表达 JSON 配方，则新增
`IFrequencyMaskRecipeFileDialog` 窄端口，不把 `IImageFileDialog` 扩成通用文件管理器。

### 4.5 SOLID 审查门禁

- SRP：Document 源码中不得出现 FFT、画笔 raster 或 JSON 文件循环；
- OCP：增加一种诊断不应修改编辑写入；增加一种导出不应修改遮罩数学；
- LSP：所有用例遵守取消、Session 不变、失败不提交半成品和结果指纹契约；
- ISP：Document 只注入实际使用的窄用例；
- DIP：Application 依赖编解码/写入抽象，Feature 不直接创建 Infrastructure；
- 组合根测试证明数值服务无状态、Document scoped、Session 不跨 Scope 泄漏。

## 5. 数学与编辑协议

### 5.1 坐标事实源

内部频谱始终保持 FFT 自然索引，UI 显示使用中心化坐标。所有转换必须调用 `FrequencyCoordinates`：

```text
display(x, y) → internal(u, v)
conjugate(u, v) = ((W-u) mod W, (H-v) mod H)
```

画布只向 Document 提交归一化显示坐标 `[0,1]²`；Application/Domain 按当前 padded 宽高换算到离散 bin。
黑边、缩放、DPI 和控件尺寸只属于 View 坐标映射器。Domain 不接收屏幕像素。

### 5.2 实值图像的共轭条件

对实值输入频谱：

```text
F(u,v) = conjugate(F((-u) mod W, (-v) mod H))
```

只应用实数增益遮罩 `H` 时，为保证 IFFT 仍为实值，必须满足：

```text
H(u,v) ∈ [0,1]
H(u,v) = H((-u) mod W, (-v) mod H)
```

因此共轭约束是领域不变量，不是 UI 勾选项。界面可提供“显示镜像光标/配对点”，但不能提供关闭对称写入的模式。
任何导入配方也必须通过 Rasterizer 重新生成并验证，不能直接信任外部 gain 数组。

### 5.3 自共轭点

满足 `2u mod W = 0` 且 `2v mod H = 0` 的点与自身共轭。对偶数 FFT 网格，它们可能是 DC 与三个 Nyquist 组合点。
`ConjugateMaskWriter` 必须比较线性索引：相同时只写一次，不重复应用 opacity；不相同时对两个位置写入完全相同的新值。

### 5.4 增益与强度

编辑遮罩记为 `M(u,v) ∈ [0,1]`：

- `M=1`：全通；
- `M=0`：完全阻断；
- 中间值：按比例衰减幅值；
- 不修改相位，不允许负增益或大于 1 的放大。

全局强度 `s ∈ [0,1]` 只在执行时生成有效遮罩：

```text
H(u,v) = 1 - s + s * M(u,v)
```

因此 `s=0` 必须与全通逐值相等，`s=1` 完全应用编辑遮罩。调整强度不修改操作历史，也不生成一条编辑操作。

### 5.5 单次写入混合

画笔、橡皮和几何工具统一使用：

```text
new = old + opacity * (targetGain - old)
```

- `targetGain ∈ [0,1]`；
- `opacity ∈ (0,1]`；
- 衰减画笔默认 `targetGain=0`；
- 恢复橡皮固定 `targetGain=1`；
- 每次 stamp 对同一 bin 最多混合一次，随后把结果原子写给共轭点；
- 所有运算后检查有限值并裁切到 `[0,1]`。

该公式比“每帧减固定值”更可复现，也使不同 Pointer 事件频率不会无意改变一次 stamp 的语义。

### 5.6 工具几何

所有几何使用归一化中心化显示坐标，重放时离散化：

- 画笔路径：圆形 stamp，半径相对于画布较短边；相邻采样点用固定间距插值，避免快速拖动留下空洞；
- 橡皮：复用同一圆形 stamp，只改变目标增益为 1；
- 矩形：两个角定义闭区间，轴对齐，边界包含规则由 Golden 固定；
- 圆环：中心、内半径、外半径，满足 `0 ≤ inner < outer`；边界包含规则固定；
- 共轭配对：对最终命中 bin 写入，不通过视觉上再画一份浮点几何猜测镜像位置。

矩形和圆环允许中心不在 DC，因此可以做手动成对区域实验；它们不提供自动峰检测或 notch 结论。

### 5.7 频带锁定

频带锁定是编辑约束，不是第二张遮罩：只有满足 `inner ≤ FrequencyCoordinates.Radius ≤ outer` 的 bin 才能被
画笔、橡皮、矩形或圆环修改。配对点半径理论上相同；实现仍应对两点分别验证并用测试保护边界。

“全部反转”和“全部重置”作用于整张遮罩，不受频带锁定影响，按钮文案必须明确为“反转全部”和“重置为全通”。
V1 不提供“只反转锁定频带”，避免相似按钮产生不可见语义差异。

### 5.8 反转与重置

反转定义为 `M' = 1 - M`。共轭相等的两个值经过相同变换后仍相等。重置清空操作序列并恢复全通基线。
重置属于破坏性编辑，必须可撤销；只有明确执行“新建空白配方”或加载另一份配方时才清空 redo 栈。

## 6. 操作配方、撤销和持久化

### 6.1 为什么不保存逐步位图

2048² 的一个 `double[]` 约 32 MiB。若每次落笔都复制整张遮罩，十步历史就可能超过 320 MiB，且还未计算 FFT、
raw 和 Bitmap。因此 V1 保存小型操作描述，当前遮罩增量更新，undo/redo 时确定性重放操作序列。

### 6.2 操作模型

建议使用带稳定 `Kind` 的普通不可变记录：

```text
BrushStroke       归一化点序列、半径、目标增益、opacity、当时的频带锁定
EraseStroke       归一化点序列、半径、opacity、当时的频带锁定
RectangleFill     两角、目标增益、opacity、当时的频带锁定
RingFill          中心、内外半径、目标增益、opacity、当时的频带锁定
InvertAll         无几何参数
ResetAllPass      作为可撤销操作记录，不依赖 UI 当前值
```

频带锁定参数必须固化进每条操作，不能在重放旧历史时读取当前 UI 的 lock 状态。全局强度不进入操作序列，只进入 Recipe。

### 6.3 有界历史

G0 建议冻结并由测试验证以下上限，若实际交互验证需要调整，必须先更新本文和资源测算：

- 最多 128 条可持久化操作；
- 单条画笔最多 4096 个归一化采样点；
- 全配方最多 32768 个采样点；
- 序列化 JSON 上限 1 MiB；
- 连续采样只有移动距离达到画笔半径的一定比例才新增点；
- 达到上限前可见提示并拒绝开始会超限的新操作，不丢弃旧操作、不静默截断路径。

这些是防止恶意或意外快照膨胀的产品限制，不是性能优化细节。V1 不实现有损路径简化；以后若引入，必须有几何误差门禁。

### 6.4 快照与外部配方

Document snapshot schema 1 保存：

- 源路径、通道、代理档位；
- 当前工具参数、频带锁定、全局强度；
- 有界操作配方和当前 undo 游标；
- 当前选中视图、缩放和探针的轻量值。

不保存图片字节、Complex、增益网格、raw 平面、Bitmap、计时、错误堆栈或取消对象。恢复只恢复参数和操作，不自动读图、
不自动 FFT；用户重新载入后按当前 padded 网格重放。归一化操作可用于不同尺寸图片，但 UI 必须提示“遮罩按归一化频率重放”，
不能宣称离散 bin 完全相同。

### 6.5 配方 JSON

配方 JSON 使用独立 schema 1，至少包含：

- schema、产品稳定 ID、创建版本和坐标协议；
- 全通基线、全局强度、操作序列；
- 每条操作稳定 kind、有限参数和频带锁定快照；
- 可选的原始 padded 尺寸，仅作复现提示，不作为执行信任来源；
- 配方规范化后的 SHA-256 指纹。

导入按严格 DTO → 校验 → Domain 转换执行。未知 schema、未知 kind、非有限数、超限点数、越界坐标和指纹不符必须结构化失败，
不能部分导入。不得从 JSON 读取任意类型名或使用反射反序列化。

## 7. 遮罩应用、重建与诊断

### 7.1 执行顺序

```text
重放/增量得到编辑遮罩 M
    ↓
按全局强度生成有效遮罩 H
    ↓
验证有限值、范围和逐点共轭误差
    ↓
复制 Session 的只读 FrequencySpectrum
    ↓
逐点执行 F' = H * F
    ↓
IFFT、有限值检查、最大虚部残差门禁
    ↓
裁回代理尺寸、回写选定通道、保持 Alpha
    ↓
生成预览、差异、能量、质量和裁切诊断
```

缓存频谱绝不原地修改。虚部残差超过 `1E-8` 视为算法错误，拒绝提交结果和导出，不能静默只取实部。

### 7.2 全通短路

当有效遮罩逐值为 1 时允许直接返回源代理，但必须同时保留普通 IFFT 路径的 Golden 等价测试。全局强度 `s=0`、
初始配方和“重置为全通”都应命中相同语义，不因来源不同产生不同指纹或结果。

### 7.3 通道语义

- 一次只编辑一个通道；
- R/G/B 回写只改变所选颜色通道；
- Y/Cb/Cr 使用既有转换与裁切规则，不重新定义中性值；
- Alpha 逐字节保持；
- 全零遮罩表示所选频率信号被完全阻断，不等同于“自动变为中性颜色”；
- 所有裁切数和颜色重建裁切必须可见，不把 byte 裁切后的图当作 raw 信号。

### 7.4 诊断

结果至少提供：

- 遮罩最小、最大、平均增益；
- 非全通 bin 数与比例；
- 共轭对称最大误差；
- 原频谱总能量、有效频谱能量和保留比例；
- 最大 IFFT 虚部残差；
- raw 结果最小/最大值、低于 0/高于 255 样本数；
- 通道回写裁切数；
- 与代理源图的 MAE、PSNR、全局 SSIM 和差异图；
- mask raster、multiply+IFFT、projection、diagnostics 的分阶段耗时；
- 当前结果属于代理还是预算内原尺寸。

能量保留比例只描述当前频谱经过当前幅值遮罩后的能量变化，不叫“信息保留率”或“质量”。PSNR/SSIM 只描述相对源图差异，
不自动给出“更好”结论。

## 8. Session、缓存与资源预算

### 8.1 Session 所有权

`FrequencyMaskEditorSession` 持有：

- 已解码源 `PixelImage`；
- 当前分析代理和选定通道 double 平面；
- 一份只读 `FrequencySpectrum`；
- 原始幅度谱预览；
- 源/代理/padded 尺寸、档位、源路径和 Session 指纹；
- 预算内原尺寸能力说明。

Session 不持有 Document、Bitmap、ServiceProvider、操作历史或跨配方结果缓存。释放后拒绝使用，两个 Scope 不共享可变数组。

### 8.2 缓存失效

| 变化 | 重建 Session/FFT | 重放遮罩 | 重做 IFFT | 只更新显示 |
| --- | --- | --- | --- | --- |
| 源图、通道、代理档位 | 是 | 是 | 是 | 是 |
| 新增/撤销/重做编辑操作 | 否 | 增量或重放 | 是 | 是 |
| 全局强度 | 否 | 否 | 是 | 是 |
| 显示遮罩透明度、缩放、探针 | 否 | 否 | 否 | 是 |
| 工具、画笔半径、目标增益、lock 参数 | 否 | 否，直到提交操作 | 否 | 是 |
| 导入配方 | 否 | 是 | 是 | 是 |

画笔拖动中的临时操作只属于当前 gesture；Pointer release 后才进入 undo 历史。拖动取消或窗口关闭时丢弃未提交 gesture，
不污染持久配方。

### 8.3 实时策略

- Pointer 移动即时更新轻量遮罩预览；
- IFFT 采用 120–180 ms 节流/防抖，且同一时刻最多一个重建在运行；
- 新请求先推进 generation，再取消旧令牌；
- Pointer release 强制安排最终重建；
- mask raster 按行、乘法按样本块、FFT 按行/列、投影和诊断按像素块检查取消；
- 取消不作为错误，旧 generation 不能覆盖新遮罩或新图片结果；
- UI Dispatcher 不执行大数组循环。

“实时”指交互期间持续获得最终一致的预览，不承诺每一个 Pointer 事件都完成一次 2048² IFFT。

### 8.4 结构资源预算

2048² 最坏代理需要大致：

- Session 频谱 `Complex[]` 约 64 MiB；
- IFFT 工作副本约 64 MiB；
- 编辑/有效 `double[]` 遮罩各约 32 MiB；
- raw 结果约 32 MiB；
- 若干 RGBA 预览各约 16 MiB；
- 操作记录、诊断与 Avalonia Bitmap 另有开销。

实现目标是避免同时长期持有编辑遮罩和有效遮罩的多份冗余副本：强度可以在乘法时即时计算或复用单个工作数组。
结构预算建议控制单个活动 Document 在约 260 MiB 内，不写成进程峰值或市场性能承诺。自动测试检查数组和历史上限，
不使用易受 GC、机器和后台负载影响的严格工作集断言。

### 8.5 原尺寸

只有原图补零后仍满足共享限制时才允许显式原尺寸执行：单维不超过 2048，总复数样本不超过 4,194,304。
超出预算时保留代理导出并显示原因；不分块全局 FFT、不先缩小后放大、不把代理结果伪装为原尺寸。

## 9. Application 用例

### 9.1 Prepare Session

`IPrepareFrequencyMaskEditorSessionUseCase`：

- 通过 `IImageCodec` 解码一次；
- 校验图片与 FFT 预算；
- 生成指定档位代理并抽取选定通道；
- 建立 padded 平面、只读 FFT 和幅度谱；
- 返回独占 Session；失败或取消不返回半成品。

### 9.2 Render Mask

`IRenderFrequencyMaskUseCase`：

- 校验 Session 和 Recipe；
- 确定性重放或接受与同一 Recipe 指纹绑定的增量遮罩；
- 生成有效强度、验证增益和共轭不变量；
- 通过共享 `FrequencyMaskApplier` 执行 IFFT；
- 回写通道、生成预览、差异与诊断；
- 返回 Session 指纹 + Recipe 指纹 + 结果指纹绑定的不可变结果。

### 9.3 导入和导出

- 配方导入只产生通过校验的 Domain Recipe，不触发隐式图片读取；
- 配方导出先生成规范 JSON，再原子写入；
- 图片导出只接受当前 Session、Recipe、强度和代理/原尺寸标志完全匹配的结果；
- 遮罩预览 PNG 必须标记为显示图，不宣称可无损还原 double 增益；
- 重建 PNG 使用正式编码器回读验证尺寸、像素和 Alpha；
- 取消、失败和 stale 不能覆盖既有目标文件。

## 10. Document 状态机

### 10.1 注册与组合

计划增加稳定 ID：

```csharp
public static readonly DocumentTypeId FrequencyMaskEditorDocument =
    new("myavalonia.plugin.image.lab.document.frequency-mask-editor");
```

由唯一 Module 登记 `AddPersistableDocument<FrequencyMaskEditorDocument, FrequencyMaskEditorView>`。完成后应为十二个
Persistable Document、零 Tool。组合测试逐项验证顺序和稳定 ID，不能只断言数量。

### 10.2 状态分层

Document 状态分为：

- 持久输入：路径、通道、档位、Recipe、工具参数、视图参数；
- 临时编辑：当前 gesture、镜像光标、框选预览；
- 派生大对象：Session、编辑遮罩、结果和 Bitmap；
- 生命周期：busy、generation、CancellationTokenSource、stale、error；
- 历史：undo 列表、redo 列表和当前 Recipe 指纹。

路径/通道/档位/配方变化推进 Revision；hover、进度、耗时、busy、临时 gesture 和面板大小不标 Dirty。

### 10.3 撤销/重做

- 一次完整 stroke/shape/反转/重置是一条历史；
- 新编辑清空 redo；
- undo/redo 通过 Recipe 游标和确定性重放恢复，不保留完整遮罩快照；
- 当前 gesture 未提交时不可与 undo 并发；
- undo/redo 会使结果 stale、取消旧重建并安排最后一次重建；
- 导入新配方建立新的历史根；旧配方不藏在无限 redo 中。

### 10.4 结构化错误

至少区分：路径不存在、解码失败、图片/FFT 超限、配方 schema 不支持、操作/点数超限、非有限参数、非法几何、
共轭验证失败、FFT 非有限、IFFT 虚部超限、重建裁切、结果过期、导入/导出失败和用户取消。

Document 把错误转换为详细中文状态。除 `OperationCanceledException` 外不吞异常；详细堆栈只进入测试/开发诊断，不直接展示给用户。

## 11. UI 信息架构

### 11.1 布局

- 左侧“输入与工具”：图片、通道、档位、画笔/橡皮/矩形/圆环、半径、目标增益、opacity、频带锁定；
- 中部“频域编辑”：原始幅度谱为底图，遮罩以可调透明度叠加，显示当前/共轭双光标和形状预览；
- 中部或下方“空间结果”：原图、重建图、差异图同步查看；
- 右侧“数值与历史”：遮罩统计、能量、虚部、质量、频点探针、undo/redo 列表摘要；
- 底部状态：源/代理/padded 尺寸、Session/Recipe 摘要、当前阶段、busy、取消、stale 和错误。

### 11.2 画布职责

`FrequencyMaskCanvasControl` 可以：

- 按给定 Bitmap/只读数据绘制频谱、遮罩和几何预览；
- 把 Pointer 坐标扣除 letterbox 后转换为归一化显示坐标；
- 捕获/释放 Pointer，并发送开始、移动、结束、取消 gesture 意图；
- 绘制主光标和共轭光标。

它不可以直接取得增益数组、写 bin、计算共轭、执行 IFFT、读文件或管理取消源。code-behind 只转发 View 事件，
复杂坐标换算进入可单测的映射器。

### 11.3 可见规则

- 没有 Session 时禁用编辑和导出，保留配方导入；
- 对称安全始终显示为启用锁定状态；
- 画笔显示目标增益和 opacity；橡皮显示“恢复到 1”；
- lock 开启时在频谱上显示内外径向边界；
- 圆环参数非法时不提交操作并显示具体原因；
- busy 时允许继续轻量画布输入，但只保留最后待执行重建；资源不足时可禁用下一 gesture；
- 结果 stale 时旧图可淡化保留供比较，但导出按钮禁用并明确原因；
- 代理和原尺寸结果必须有长期可见标签。

### 11.4 可访问性

- 黑/白或颜色不是通过/阻断、当前/stale、主点/共轭点的唯一线索；
- 所有工具支持键盘选择，主要数值支持直接输入；
- 遮罩统计同时提供文本，不只依赖灰度预览；
- 高对比主题和 125%/150% DPI 下光标、边界和焦点可见；
- Headless 测试覆盖空态、加载、工具切换、非法参数、busy、stale 和错误可见性。

## 12. 中文注释与设计说明规范

新增生产代码一律使用中文注释，重点解释设计原因、数学语义、所有权和失败边界：

- `FrequencyGainMask` 说明增益范围、不可变所有权和为何只接受实数；
- 共轭写入说明自然索引公式、自共轭 DC/Nyquist 及“只混合一次”的原因；
- 归一化坐标离散化说明边界包含、矩形/圆环和不同 padded 尺寸重放语义；
- 画笔插值说明为何不能依赖 Pointer 事件密度；
- opacity 混合说明同一 stamp 去重，避免 UI 帧率改变结果；
- 全局强度公式说明为什么不修改编辑历史；
- IFFT 虚部门禁说明共轭不变量和超限为何必须失败；
- Session 频谱工作副本说明不能原地写缓存；
- 操作历史说明为何不用完整遮罩快照及资源上限；
- generation、防抖和 gesture 提交说明迟到结果与关闭边界；
- JSON DTO 校验说明为何拒绝类型反射、部分导入和超限数组；
- 原尺寸与代理说明为何不能缩小后再放大或分块冒充全局 FFT。

不为简单属性、赋值和显而易见的循环堆砌“设置 X”“遍历 Y”式注释。关键设计变化必须同步专用文档，不能只留在代码里。

## 13. 单元测试矩阵

### 13.1 模型、校验和指纹

- 稳定工具/操作枚举 round-trip；
- target gain、opacity、strength 的 0/1 边界、越界和 NaN/Infinity；
- 矩形零面积、坐标越界、圆环 `inner >= outer`；
- lock 边界 `0 ≤ inner < outer ≤ 1`；
- 操作数、单 stroke 点数、总点数和 JSON 字节预算前后；
- 不可变集合和数组防御性复制；
- 相同规范配方指纹相同，顺序、几何或强度变化时指纹变化；
- 显示透明度、hover 和选择工具不进入数学指纹。

### 13.2 坐标与共轭 Golden

- 中心、四角、轴线、最后像素和非方形网格的 display/internal 往返；
- 共轭两次返回原点；
- DC 和各 Nyquist 自共轭点只应用一次 opacity；
- 普通频点两侧获得逐位相同 double；
- 奇偶尺寸通用函数行为明确，正式 FFT 的 2 的幂网格全部覆盖；
- UI letterbox、边缘、DPI 和面板外 Pointer 映射；
- 主光标和共轭光标显示位置与 Domain 索引一致。

### 13.3 Rasterizer

- 空配方全为 1；
- 单点/固定半径 brush 命中集合 Golden；
- 稀疏路径插值无空洞，重复事件不会额外改变一次 stamp；
- eraser 向 1 恢复；
- 矩形边界包含规则和共轭矩形；
- DC 中心及偏心圆环命中集合；
- lock 内可写、边界可写、lock 外保持原值；
- 反转两次恢复原值，重置恢复全通；
- undo/redo 重放逐值一致；
- 相同配方多次重放逐值一致且源 Recipe 不变；
- 每次操作后所有增益有限、位于 `[0,1]`、共轭最大误差为 0 或约定容差内；
- 取消不返回部分网格。

### 13.4 强度、FFT 与重建

- `s=0` 有效遮罩逐值全通；`s=1` 等于编辑遮罩；中间值符合独立公式；
- strength 变化不修改 Recipe 操作历史；
- 全通结果与代理逐字节一致，普通 IFFT 路径 raw 误差不超过既有容差；
- 全零遮罩只留下数值零平面；
- DC-only、单频正弦、棋盘格和冲激 Golden；
- 对称随机操作序列 IFFT 最大虚部不超过 `1E-8`；
- 人为构造非对称 gain 必须在 IFFT 前被拒绝；
- Session 原始 `FrequencySpectrum` 前后逐值不变；
- R/G/B/Y/Cb/Cr 重建和 Alpha 保持；
- raw 越界、颜色裁切、能量比例、PSNR/SSIM 与差异图确定性；
- 代理和预算内原尺寸结果标志、尺寸与指纹正确。

### 13.5 历史、Application 和导出

- stroke 只有 Pointer release 后进入历史；取消 gesture 不进入历史；
- 新编辑清空 redo，undo/redo 边界无异常；
- 反转和重置可撤销；
- 达到历史/点数上限时先阻断，不丢旧操作；
- Session 只解码一次、两个 Session 不共享可变状态、Dispose 后拒绝使用；
- 快速连续请求只有最后 generation 提交；取消不显示错误；
- 工具参数变化不误触 IFFT，强度变化只重做必要阶段；
- schema 1 JSON round-trip 和规范化指纹；
- 未知 schema/kind、超限、非有限和指纹篡改拒绝完整导入；
- stale Session/Recipe/strength 的结果不能导出；
- 重建 PNG 与遮罩预览 PNG 正式回读，验证尺寸、像素、Alpha 和显示语义；
- 原子写入失败或取消不覆盖目标、不遗留临时文件。

### 13.6 Document、组合根和 View

- snapshot 只含轻量路径、参数和有界操作，不含图片、Complex、gain/raw 数组、Bitmap 和异常堆栈；
- 恢复不自动读图/FFT，非法 snapshot 安全回退并给出中文提示；
- Dirty、stale、busy、generation 和关闭规则逐项验证；
- 两个 DI Scope 的配方、历史、Session、取消、结果和 Bitmap 完全隔离；
- Module 完成后按固定顺序登记十二个 Persistable Document、零 Tool，并逐项验证稳定 ID；
- 算法 singleton 不持有 Document 状态，Document 是 scoped；
- Standalone 复用真实 Module/DI/View；
- Headless View 可创建，编译绑定通过，关键工具和状态启禁正确；
- 源码/项目扫描证明没有新增 AIFLOW、Workflow、Windows CI、发布脚本或新 NuGet。

### 13.7 架构与注释门禁

- Domain 命名空间不得引用 Avalonia、Features、Infrastructure、Host SDK、JSON 或文件系统；
- Document 不出现 FFT、共轭、画笔填充或 JSON 解析循环；
- View/code-behind 不出现频率数学和直接 gain 数组写入；
- 不出现 Service Locator、反射工具发现、万能 service、Event Bus 或不必要接口层；
- 新增核心类型、数学分支、所有权和并发边界具有中文 XML/行内说明；
- 评审检查注释是否解释“为什么”和限制，不以注释行数作为质量指标。

## 14. 本地开发门禁

### 14.1 每个实施包

每个 G 包至少满足：

1. 先增加会失败的相关测试，再实现生产代码；
2. 本包测试与全部既有测试通过，0 失败、0 跳过；
3. Debug warn-as-error build 0 警告、0 错误；
4. 实际测试数量如实记录，不预设或凑数；
5. 对应 `history/gN-*.md` 和受影响文档同步；
6. 不删除、跳过、合并掉或放宽既有测试换取通过；
7. 涉及共享 Frequency Filter 核心的改动，必须先后比较重构前后的 Golden 与完整回归。

### 14.2 G9 完整本地门禁

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

硬条件：

- locked restore 成功且不静默升级包；
- Debug/Release 构建均 0 警告、0 错误；
- 两配置新旧测试全部通过、0 失败、0 跳过；
- 实际测试总数必须大于 G0 实跑基线，但不得为数量拆分无意义测试；
- 共轭、虚部、全通、undo/redo、资源、取消、stale 导出和 Scope 隔离门禁全部有自动证据；
- `git diff --check` 通过；
- 没有新增 NuGet、AIFLOW、Workflow Action、Workbench Command、Windows CI 或发布配置；
- 文档状态、测试数量和代码事实一致。

### 14.3 当前明确不做

- 不创建或修改 GitHub Actions、Azure DevOps 等 Windows CI；
- 不运行插件 ZIP/发布 Target；
- 不执行真实 Host 安装、升级、卸载、Dock 或布局恢复验收；
- 不把 Standalone/Headless 结果冒充真实 Host 证据；
- 不修改发布版本、市场资料和发布清单；
- 不声明产品已发布。

这些事项只在用户明确进入发布阶段时，按 `docs/design/shared/deployment-and-release.md` 单独执行。

## 15. 分阶段实施包

### G0：产品、数学与基线冻结

- 实跑 locked restore、Debug/Release build/test，记录实际起始数量；
- 冻结 gain、strength、opacity、共轭、自共轭、边界包含和 lock 语义；
- 冻结操作数、点数、JSON、FFT 和内存预算；
- 准备 DC、Nyquist、正弦、棋盘格、冲激和随机对称操作 Golden；
- 建立专用 README、数学、测试和 `history/g0-*.md`；
- 不登记 Document，不做 UI。

验收：公式、默认值、错误语义和延期项无未决选择，原代码基线无变化。

### G1：共享实数遮罩应用核心

- 先为 Frequency Filter 当前输出补足重构保护；
- 提取 `FrequencyGainMask` 和 `FrequencyMaskApplier`；
- 让 Frequency Filter 通过委托复用新核心；
- 保持现有滤波配方、结果、指纹和数值完全兼容；
- 增加 gain 所有权、尺寸、有限值、共轭验证和取消测试。

验收：旧 362 基线及 G0 新测试全部通过；无第二份 FFT 应用循环。

### G2：Recipe、Operation 与历史

- 建立不可变操作模型、Recipe、规范化、校验和指纹；
- 建立有界 undo/redo 模型；
- 固定 snapshot/JSON DTO 边界，但暂不做文件 IO；
- 覆盖非有限、越界、预算和防御性复制测试。

验收：非法状态无法进入 Rasterizer，历史不保存完整遮罩。

### G3：共轭安全 Rasterizer

- 实现归一化坐标离散化和 `ConjugateMaskWriter`；
- 实现画笔路径插值、橡皮、矩形、圆环；
- 实现频带锁定、反转和重置；
- 在长循环加入取消和 checked 预算；
- 完成命中集合、边界、自共轭、重放与随机序列测试。

验收：任意合法操作序列输出有限、范围正确、确定性且共轭安全。

### G4：有效遮罩、IFFT 与诊断

- 实现全局 strength 混合和全通短路；
- 通过共享 Applier 执行 IFFT 和虚部门禁；
- 回写六通道并保持 Alpha；
- 生成遮罩预览、差异、能量、裁切与质量诊断；
- 完成全通、全阻、DC、Nyquist、正弦、棋盘格和随机对称 Golden。

验收：虚部不超过 `1E-8`，全通逐字节一致，缓存频谱不变。

### G5：Session、用例和导出

- 实现 Prepare/Render/FullSize 用例和独占 Session；
- 实现 Recipe JSON 严格导入/原子导出；
- 实现重建/遮罩预览 PNG 导出与正式回读；
- 实现 Session/Recipe/strength 指纹和 stale 拒绝；
- 完成取消、迟到、Dispose、资源边界和原子失败测试。

验收：用例不依赖 Avalonia View，失败或取消不提交半成品。

### G6：Document、持久化与组合根

- 增加稳定 ID、Document、快照 schema 1 和 scoped 服务；
- 接入 gesture 提交、undo/redo、Dirty/Revision、stale 和 generation；
- Module 更新为十二个 Persistable Document、零 Tool；
- 更新 Standalone 真实 Scope；
- 完成双 Scope、恢复、关闭和迟到结果测试。

验收：Document 没有数值/文件实现，多实例完全隔离。

### G7：编辑画布与联动 UI

- 实现编译绑定 View 和轻量 `FrequencyMaskCanvasControl`；
- 完成 letterbox/DPI 坐标、Pointer capture、主/共轭光标和几何预览；
- 接入输入、工具、频带、强度、历史、诊断和导出状态；
- 实现 120–180 ms 重建调度和 release 最终提交；
- 完成 Headless、坐标、状态启禁和可访问性测试。

验收：UI 线程无大数组循环；快速绘制最终结果对应最后 Recipe。

### G8：文档与人工验收

- 完成 `README.md`、`guide.md`、`user-manual.md`、`mathematical-principles.md`、`testing.md`、`recipe-schema.md`；
- 填写 G0–G8 实际历史记录；
- 同步根 README、`docs/README.md`、`docs/design/README.md`、未来能力和公共图像领域边界；
- 人工检查 512/1024/2048、六通道、四工具、lock、反转、历史、导入/导出和错误态；
- 明确记录未执行真实 Host、ZIP、Windows CI 和发布门禁。

验收：普通用户、开发者和维护者均有单一入口，文档不超出证据。

### G9：本地开发封板

- 完整执行第 14.2 节双配置命令；
- 记录实际测试数量、警告、失败、跳过和环境；
- 复核 SOLID、中文注释、资源、取消和安全边界；
- 检查无 AIFLOW、Windows CI、发布文件和无关改动；
- 将状态从“待实施”改为真实结果，失败项不得勾选完成。

验收：全部本地门禁通过，或如实保持未完成并记录阻断；不得用计划替代证据。

## 16. 预计代码与文档落点

### 16.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Application/
│  └─ FrequencyMaskEditing/
│     ├─ FrequencyMaskEditorContracts.cs
│     └─ FrequencyMaskEditorUseCases.cs
├─ Constants/
│  └─ PluginIds.cs
├─ Domain/
│  ├─ Frequency/
│  │  ├─ FrequencyGainMask.cs
│  │  └─ FrequencyMaskApplier.cs
│  └─ FrequencyMaskEditing/
│     ├─ FrequencyMaskModels.cs
│     ├─ FrequencyMaskRasterizer.cs
│     ├─ ConjugateMaskWriter.cs
│     ├─ MaskEditHistory.cs
│     └─ FrequencyMaskDiagnostics.cs
├─ Features/
│  └─ FrequencyMaskEditor/
│     ├─ FrequencyMaskEditorDocument.cs
│     ├─ FrequencyMaskEditorView.axaml
│     ├─ FrequencyMaskEditorView.axaml.cs
│     ├─ FrequencyMaskCanvasControl.cs
│     └─ FrequencyCanvasCoordinateMapper.cs
├─ Infrastructure/
│  └─ Persistence/
│     └─ FrequencyMaskRecipeSerializer.cs
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs
```

这是职责落点，不要求机械拆文件。短小且紧密的值对象可以合并，但不得重新把 Domain、用例、Document、View 和文件 IO
塞进一个类。

### 16.2 测试

建议按职责新增：

- `FrequencyMaskRecipeTests.cs`；
- `FrequencyMaskRasterizerTests.cs`；
- `FrequencyMaskReconstructionTests.cs`；
- `FrequencyMaskUseCaseTests.cs`；
- `FrequencyMaskEditorDocumentTests.cs`；
- `FrequencyMaskEditorViewTests.cs`；
- 对 `FrequencyFilterDomainTests`、`CompositionAndPersistenceTests` 和正式 PNG 回读测试做增量扩展。

### 16.3 专用文档

```text
docs/design/frequency-mask-editor/
├─ README.md
├─ implementation.md
├─ testing.md
├─ guide.md
├─ user-manual.md
├─ mathematical-principles.md
├─ recipe-schema.md
└─ history/
   ├─ README.md
   └─ g0-... 至 g9-...
```

上述专用文档均已由对应实施包创建，并只记录已有代码与本地实跑证据。

## 17. 人工验收场景

### 17.1 基本绘制

1. 载入图片并分别选择六通道，确认原谱和重建正确切换；
2. 在普通频点落笔，确认主点和共轭点同时变化；
3. 在 DC/Nyquist 落笔，确认 opacity 只应用一次；
4. 快速画长线，确认无明显断点，最终结果对应完整 stroke；
5. 用橡皮恢复局部，确认增益回到 1；
6. 创建偏心矩形和圆环，确认共轭区域和数值提示一致。

### 17.2 约束与历史

1. 开启频带锁定并跨边界绘制，确认 lock 外完全不变；
2. 调整 strength 从 0 到 1，确认历史不增加且结果连续变化；
3. 连续 undo/redo 画笔、圆环、反转和重置，确认逐步一致；
4. 达到操作预算前确认有可见提示且旧工作不丢失；
5. 保存并恢复工作区，确认不自动读图，重新加载后按归一化频率重放；
6. 导出再导入 Recipe，确认指纹和遮罩一致。

### 17.3 数值与导出

1. 全通时重建与代理逐字节一致；
2. 对称遮罩重建时虚部残差低于门禁；
3. 观察 Y/Cb/Cr 重建的裁切统计和 Alpha 保持；
4. 快速连续编辑并取消，确认迟到结果不会覆盖新图；
5. 导出重建 PNG、遮罩预览 PNG，回读尺寸和标签正确；
6. 修改 Recipe 后确认旧结果立即 stale 且不能导出；
7. 超出原尺寸预算时确认只有代理导出，且没有伪造原尺寸。

### 17.4 Standalone 边界

Standalone 可以证明真实 Module/DI/View、编译绑定、主要交互、取消、导出和插件内部 Scope 可工作；它不能证明
真实 Host Catalog、Dock、布局恢复、AssemblyLoadContext、正式 ZIP、Windows CI 或目标设备性能。

## 18. 风险与回滚

| 风险 | 控制 |
| --- | --- |
| 单边写入导致复数残差 | 共轭写入唯一入口、导入只重放 Recipe、执行前全网格验证、IFFT `1E-8` 硬门禁 |
| Pointer 事件频率改变结果 | 固定路径插值、stamp 内去重、gesture 结束后形成单操作 Golden |
| 2048² 历史内存爆炸 | 操作记录重放、点数/JSON 上限、不保存 mask snapshot |
| 快速绘制导致 UI 卡顿 | 即时轻量预览、节流 IFFT、单活动任务、取消和最后 generation 获胜 |
| 共享核心重构破坏 Frequency Filter | G1 前补 Golden、保留适配入口、完整 362 基线回归、失败整体回滚 |
| 归一化 Recipe 跨尺寸产生误解 | 保存原 padded 尺寸作提示，文档明确归一化重放而非相同 bin |
| stale 结果被导出 | Session + Recipe + strength + size 指纹四重一致性检查 |
| 工具持续膨胀为通用编辑器 | V1 固定四类几何，不引入图层、套索、脚本、自动检测和通用 Pipeline |

回滚顺序：

1. 隐藏未稳定入口并移除第十二个 Module 贡献；
2. 移除 Document、View、Application 用例和专用序列化；
3. 仅当 G1 共享核心已经通过 Frequency Filter 全回归且职责更清晰时才保留；否则整体回滚 G1；
4. 不修改或回退现有十一个 Document 的稳定 ID、快照和用户结果；
5. 文档如实标记未完成阶段、失败证据和回滚原因。

## 19. V1 完成检查清单

以下项目已由 G0–G9 代码、测试、文档和本地门禁证据完成：

### 产品与数值

- [x] 第十二个贡献是多实例 Persistable Document，仍为零 Tool；
- [x] 画笔、橡皮、矩形、圆环、lock、反转、重置、undo/redo 全部可用；
- [x] 所有合法编辑自动共轭安全，自共轭点只应用一次；
- [x] strength 公式、全通和 `1E-8` 虚部门禁通过；
- [x] 六通道、Alpha、能量、裁切和质量诊断正确；
- [x] 代理与预算内原尺寸语义、尺寸和导出清晰。

### 架构与生命周期

- [x] Domain/Application/Feature/Infrastructure 依赖方向正确；
- [x] Frequency Filter 与编辑器复用同一个遮罩应用核心且无回归；
- [x] Document、View/code-behind 中没有数值与文件实现；
- [x] 多 Scope、Session、历史、取消、关闭和迟到结果完全隔离；
- [x] 快照和 Recipe 有界，不保存大数组，恢复不自动执行；
- [x] 没有不必要 Strategy、Factory、Mediator、反射或事件总线。

### 测试、注释与文档

- [x] Debug/Release locked restore、warn-as-error build、全量 test 通过且零跳过；
- [x] 数值、工具、历史、用例、Document、View、导入/导出和架构门禁齐全；
- [x] 核心代码具备详细中文设计注释，无无价值注释堆砌；
- [x] 专用文档、根索引、未来能力和公共边界同步；
- [x] 文档只写实际测试数量和真实证据；
- [x] 未使用 AIFLOW，未新增 Windows CI，未执行或宣称发布门禁。
