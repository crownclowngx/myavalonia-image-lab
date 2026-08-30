# ImageLabPlugin V1 Periodic Noise Removal／周期噪声与陷波器实施计划

> 计划状态：V1 已完成本地开发实现与 Debug/Release 自动门禁；本文同时保留实施冻结与完成证据，不代表发布完成<br>
> 基线日期：2026-08-31<br>
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3<br>
> 起始证据：`dotnet restore ImageLabPlugin.slnx --locked-mode` 成功；Debug/Release warn-as-error build 均为 0 警告、0 错误，两配置 test 均为 408/408 通过、0 失败、0 跳过<br>
> 完成证据：locked restore 与 `git diff --check` 通过；Debug/Release build 均为 0 警告、0 错误，两配置 test 均为 442/442 通过、0 失败、0 跳过<br>
> 核心路线：复用有界 FFT Session + 确定性候选峰检测 + 必须人工确认的陷波草案 + 共轭安全 Notch Mask + IFFT 重建 + 频谱、图像、差异和损失联动<br>
> 首要规定：SOLID 优先；设计模式朴素使用；新增生产代码使用详细中文注释；数值、安全、资源和生命周期门禁先于 UI<br>

本文记录 ImageLab 的第十三个多实例 Persistable Document。它用于定位可能由扫描、采集或传输过程
引入的周期性条纹、扫描线和周期纹理候选，并通过中心共轭的陷波遮罩观察抑制结果。

V1 不把频谱峰直接宣称为“噪声”，也不允许自动检测静默修改或导出图片。自动流程只生成可撤销、可复核的草案；用户必须
同时看到候选依据、中心对称位置、处理前后频谱、空间结果、差异和不可逆损失，再显式采用草案。

## 1. 决策摘要

### 1.1 产品形态

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Periodic Noise Removal／周期噪声与陷波器` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID | `myavalonia.plugin.image.lab.document.periodic-noise-removal` |
| 显示分类 | `图像分析` |
| 输入 | 用户显式选择的一张 PNG/JPEG 图片 |
| 分析通道 | R、G、B、Y、Cb、Cr 六个单通道 |
| 分析尺寸 | 512/1024/2048 最大边分析代理；默认 1024 |
| 检测输出 | 有界候选频率对、分数、局部突出度、风险原因和来源，不输出“已确认噪声” |
| 手动选择 | 在中心化频谱单击一个频点，领域层自动加入或移除它与共轭点 |
| 自动生成 | 由保守规则生成未确认陷波草案；不会静默进入已采用配方 |
| 陷波参数 | 半径、衰减强度、Ideal/Butterworth/Gaussian 过渡；Butterworth 阶数 |
| 实值安全 | 每个陷波中心必须和共轭中心成对；生成的增益遮罩必须满足共轭对称 |
| 结果 | 当前通道重建、处理后频谱、符号/绝对差异、PSNR/SSIM 和损失诊断 |
| 导出 | 只允许导出与当前 Session、已采用配方和结果指纹一致的 PNG；草案结果禁止导出 |
| 模式使用 | 不可变值对象、普通 sealed 服务、完整 switch、窄用例和构造注入 |
| 外部依赖 | 不新增 NuGet、原生 FFT、机器学习、图表或画布框架 |
| 明确排除 | AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP、真实 Host 与发布门禁 |

### 1.2 用户闭环

```text
选择图片、通道和分析档位
    ↓
建立有界分析代理并缓存只读 FFT
    ↓
运行确定性候选检测，或在频谱上手动选择频点
    ↓
同时显示候选点、中心共轭点、分数、风险和局部频谱依据
    ↓
生成“未确认陷波草案”，调节半径、强度、过渡方式和阶数
    ↓
联动预览原/结果频谱、原/结果图像、差异和损失诊断
    ↓
人工排除真实纹理候选，显式单击“采用草案”
    ↓
按需执行预算内原尺寸结果，并导出当前指纹一致的 PNG/配方
```

### 1.3 固定实施顺序

1. G0 冻结产品语义、检测指标、Notch 数学、误判边界和起始门禁；
2. G1 建立候选、陷波、配方、草案和损失诊断等不可变领域模型；
3. G2 完成确定性径向背景估计、局部峰检测、共轭归并和风险分级；
4. G3 完成三类陷波响应、共轭安全遮罩、组合和数值 Golden；
5. G4 完成 IFFT、处理后频谱、通道回写、差异和不可逆损失诊断；
6. G5 完成 Application Session、检测/预览/采用/原尺寸/导出窄用例；
7. G6 接入 Persistable Document、快照、取消、缓存失效和组合根；
8. G7 完成频谱交互、候选表、四视图比较、Standalone 和 Headless；
9. G8 同步专用文档、总索引、未来能力表和人工验收记录；
10. G9 复跑 Debug/Release 全量本地门禁并完成本地开发封板。

不得先在 UI 中用亮度阈值“找亮点”，再补频率坐标、共轭归并和误判说明。检测协议、Notch 响应、草案/已采用状态
和损失指标必须先在 Domain/Application 中通过测试，UI 只展示并提交意图。

## 2. 当前项目事实与复用边界

### 2.1 当前基线

仓库已经具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集与 `ImageLabPlugin.Standalone` 本地开发承载；
- 起始时已有十二个 Persistable Document，当前没有 Tool、Workflow Action 或 Workbench Command；
- `PixelImage`、`ImageSize`、六通道转换、Alpha 保持和正式 PNG/JPEG 编解码；
- `ImageAnalysisProxyProjector` 的 512/1024/2048 有界分析代理；
- `Fft1DTransform`、`Fft2DTransform`、`FrequencySpectrumBuilder` 和共享 2048² FFT 预算；
- `FrequencyCoordinates` 的自然索引、中心化坐标、cycles/pixel、归一化半径和共轭索引；
- `FrequencyGainMask` 对 `[0,1]`、有限值、尺寸和 `1E-12` 共轭对称的构造门禁；
- `FrequencyMaskApplier` 的只读频谱复制、实数增益、IFFT 和 `1E-8` 最大虚部残差门禁；
- Spectrum Inspector 的幅度谱和频点解释，Frequency Mask Editor 的手动共轭安全编辑基础；
- 差异投影、PSNR/SSIM、原子文件写入、Document Scope、generation、取消和轻量快照惯例；
- 2026-08-31 实跑的 408/408 Debug/Release 双配置本地基线。每个实施包仍须复跑，不得把本文证据冒充完成证据。

### 2.2 必须直接复用

- FFT/IFFT、频率坐标、共轭索引、通道转换、代理投影和质量指标保持单一事实源；
- 陷波输出必须构造 `FrequencyGainMask`，并交给现有 `FrequencyMaskApplier`，不复制频谱乘法和 IFFT；
- 图片选择、解码、PNG 编码、有限文本读取和原子写入继续走现有窄端口；
- 原/结果差异继续复用 `FrequencyDifferenceProjector` 与 `FullReferenceQualityAnalyzer`；
- 数值服务保持无状态 singleton；Session、候选、草案、结果、Bitmap 和取消源属于 Document Scope；
- Standalone 从真实 Module/DI 解析真实 Document 和 View，不复制一套演示算法。

### 2.3 允许的小型共享改进

实现中如需生成处理后的频谱预览，应在 `Domain/Frequency` 增加一个只负责“频谱乘实数增益并投影”的窄服务，或给现有
频谱投影器增加不破坏旧调用方的明确入口。该改动必须先由 Frequency Filter 与 Frequency Mask Editor 回归保护。

不允许为了本能力把 `FrequencyMaskApplier` 扩展成同时负责峰检测、配方、图片回写、诊断和文件导出的万能服务。

### 2.4 禁止的错误复用

- 新 Document 不持有或调用 `SpectrumInspectorDocument`、`FrequencyFilterDocument`、`FrequencyMaskEditorDocument`；
- 不读取其他 Document 的 Session、Bitmap、遮罩、历史、选中状态或取消源；
- 不把 Frequency Mask Editor 的自由绘制 `Recipe` 强行兼作 Notch 配方；二者可共享增益核心，不能混淆产品语义；
- 不在 Feature/Document 中实现 FFT 循环、候选评分、遮罩光栅化或 JSON 文件循环；
- 不把 Avalonia `Point`、Pointer 事件、Brush 或 Bitmap 放进 Domain/Application 合同；
- 不修改既有稳定 ID、快照 schema 或用户语义；
- 不建立反射发现、算法插件目录、Mediator、Event Bus、通用 Pipeline 或“智能算子”框架。

## 3. V1 范围

### 3.1 必须完成

- R/G/B/Y/Cb/Cr 单通道和 512/1024/2048 分析档位；
- 原始频谱上的手动频点选择，并明确显示其共轭位置；
- 基于对数功率、径向稳健背景、局部最大值和非极大值抑制的确定性候选检测；
- 候选分数、局部突出度、归一化频率、周期/方向提示、风险等级和风险原因；
- 最多 64 个候选频率对、最多 32 个已选择陷波对、默认最多 12 个自动建议；
- 保守自动建议生成未确认草案，手动加入/移除也先进入草案；
- Ideal、Butterworth、Gaussian 三种衰减过渡；
- 半径、衰减强度和 Butterworth 阶数调节；
- 所有陷波中心共轭成对，遮罩增益有限且位于 `[0,1]`；
- 原始/处理后频谱、原图/结果图、符号差异/绝对差异同步比较；
- 修改 bin 比例、频谱能量移除率、候选峰抑制率、最大虚部残差、raw 越界、PSNR 和 SSIM；
- “未确认草案”“已采用”“结果过期”“仅代理”“预算内原尺寸”清晰状态；
- 结果 PNG、遮罩 PNG、候选摘要 JSON 和版本化 Notch 配方 JSON 导出；
- 多 Scope、取消、generation、快照恢复、资源释放和完整本地自动门禁；
- 专用 README、指南、新手说明、数学说明、配方 schema、测试证据和分阶段历史。

### 3.2 明确不实现

- 把候选峰自动断言为扫描仪、相机、显示器或某种设备产生的确定噪声；
- 载入后自动应用、自动覆盖原文件或在未确认草案上开放结果导出；
- 基于 AI/ML 的纹理分类、训练数据、在线推理、AIFLOW 或“智能修复”文案；
- 空间变化的周期噪声、弯曲条纹、摩尔纹分离、盲源分离或时频分析；
- 相位编辑、负增益、频谱放大、复数遮罩或关闭共轭约束；
- RGB 三通道同时独立检测和三份并行实时 FFT；
- 通用自由绘制；需要任意遮罩时使用现有 Frequency Mask Editor；
- 批处理、文件夹扫描、滤镜链、宏、Workflow Action 或 Workbench Command；
- 超过共享 2048² 预算的分块伪原尺寸结果、GPU、原生 FFT 或新 NuGet；
- Windows CI、ZIP、真实 Host、安装升级和发布门禁。

## 4. 防误判与不可逆损失规则

### 4.1 候选不是结论

领域模型和 UI 统一使用“候选频率峰”“建议陷波”和“风险”，禁止使用“已检测到噪声”“已自动修复”等确定性表述。
周期性真实纹理、规则网格、织物、建筑立面和文字栅格同样可能产生离散频谱峰，仅靠单张图无法可靠区分来源。

### 4.2 两阶段确认

Document 同时维护：

- `AcceptedRecipe`：最近一次用户明确采用的配方；
- `DraftRecipe`：自动建议、手动选点或参数变化形成的预览草案；
- `DraftResult`：允许比较但必须带“未确认”标记；
- `AcceptedResult`：只有它可以进入导出用例。

“运行自动检测”只更新候选；“生成建议遮罩”只更新草案；“采用草案”才替换已采用配方。任何草案变化都会让旧草案结果
过期，但不得隐式改变已采用结果。该状态机是防误操作边界，不用事件总线实现。

### 4.3 保守自动建议

候选只有同时满足下列条件才可进入默认自动建议：

- 位于 DC 排除半径之外；
- 稳健分数与局部突出度均超过冻结阈值；
- 是固定邻域内的严格局部最大值；
- 通过共轭一致性检查；
- 未命中“宽脊线”“邻域过密”“接近 DC/Nyquist”“局部不尖锐”等高风险规则；
- 与更高分候选保持非极大值抑制距离；
- 数量不超过默认 12 对。

风险候选仍可显示并由用户手动选择，但不得默认勾选。具体默认阈值在 G0 由合成 Golden 冻结，不把 UI 默认值散落在
Document 和 AXAML 中。

### 4.4 不可逆损失可见性

- Session 内保留原图，因此重置配方可恢复预览；这不等于已导出的滤波图片可逆。
- 结果区必须显示“陷波会永久丢弃被抑制频率中的真实纹理，导出的处理图无法从自身恢复这些信息”。
- 导出前始终显示频谱能量移除率、修改 bin 数、PSNR/SSIM、最大绝对差异和风险数量。
- 当能量移除率、修改范围或质量下降超过 G0 冻结的提示阈值时显示警告，但不伪造通用“安全阈值”。
- 不提供覆盖源文件入口；只允许选择新 PNG 路径，并沿用原子写入。

## 5. SOLID 架构

### 5.1 依赖方向

```text
Features/PeriodicNoiseRemoval
  PeriodicNoiseRemovalDocument      每实例状态、命令、Revision、取消和 Bitmap
  PeriodicNoiseRemovalView          布局、编译绑定和可见状态
  PeriodicSpectrumControl           绘制频谱、候选、共轭点并提交归一化点击
                    │
                    ▼
Application/PeriodicNoiseRemoval
  PrepareSessionUseCase             解码、代理、通道、只读 FFT 和原始预览
  DetectCandidatesUseCase           编排检测并返回有界不可变候选集
  PreviewDraftUseCase               草案遮罩、IFFT、频谱/图像/差异/损失
  RenderFullResultUseCase           预算内原尺寸显式执行
  Import/ExportRecipeUseCase        schema、校验和原子文本写入
  ExportArtifactUseCase             只导出当前已采用指纹一致的结果
                    │
                    ▼
Domain/PeriodicNoiseRemoval         Domain/Frequency + Domain/Imaging + Domain/Comparison
  Candidate、Detector、Recipe、NotchResponse、MaskFactory、Diagnostics
                    ▲
                    │
Infrastructure
  既有图像编解码、文件对话框、严格 JSON、有限读取和原子文件写入
```

Feature 只依赖 Application 合同；Application 编排 Domain 和既有端口；Domain 不知道 Avalonia、文件系统、JSON、DI、
Document 或 Host SDK。

### 5.2 单一职责

| 类型 | 唯一职责 | 明确不负责 |
| --- | --- | --- |
| `PeriodicPeakDetector` | 从只读频谱产生有界候选对和解释字段 | IFFT、图片回写、UI 选择 |
| `RadialSpectrumBaseline` | 估计每个径向桶的稳健中位数和 MAD | 判定噪声、生成遮罩 |
| `PeriodicPeakRiskAssessor` | 根据固定事实生成风险原因 | 自动修改配方 |
| `PeriodicNoiseRecipe` | 保存通道、过渡参数和有限陷波中心 | FFT、Bitmap、文件 |
| `NotchResponse` | 计算一个距离处的增益 | 遍历频谱、选择候选 |
| `NotchMaskFactory` | 把配方光栅化为共轭安全 `FrequencyGainMask` | IFFT、质量结论 |
| `PeriodicNoiseLossAnalyzer` | 统计频谱移除、候选抑制和空间损失 | 修改结果、宣称主观质量 |
| `PeriodicNoiseSession` | 独占一次解码、代理、通道、频谱和原始预览 | 跨 Scope 缓存、持有 Avalonia Bitmap |
| `PeriodicNoiseRemovalDocument` | 管理实例状态、草案/采用、命令和生命周期 | 候选扫描、遮罩循环、JSON/IO |
| `PeriodicSpectrumControl` | 绘制与坐标映射，提交归一化意图 | 直接修改 Recipe 或执行 FFT |

任何同时出现 Pointer 事件、频率数组循环和文件 IO 的类型均违反 SRP，不能合入。

### 5.3 开闭原则与朴素模式

V1 只使用：

- 不可变值对象表达检测设置、候选、陷波、配方和结果；
- `NotchResponse` 内一个完整 `switch` 处理三种固定过渡；
- 构造注入和窄用例隔离状态与副作用；
- 普通列表和显式状态转换管理草案/采用，不创建通用状态机框架。

三种过渡没有独立部署、第三方扩展或生命周期，不为它们建立 Strategy 接口、抽象工厂或反射注册。只有出现真实的第二个
可替换实现边界时才新增接口，不能为“将来也许”制造层次。

### 5.4 接口隔离

建议应用边界：

```csharp
internal interface IPreparePeriodicNoiseSessionUseCase { /* 只负责一次解码和 FFT Session */ }
internal interface IDetectPeriodicNoiseCandidatesUseCase { /* 只负责候选检测 */ }
internal interface IPreviewPeriodicNoiseDraftUseCase { /* 只负责草案预览和诊断 */ }
internal interface IRenderFullPeriodicNoiseResultUseCase { /* 只负责预算内原尺寸 */ }
internal interface IImportPeriodicNoiseRecipeUseCase { /* 只负责有限读取、schema 和校验 */ }
internal interface IExportPeriodicNoiseRecipeUseCase { /* 只负责规范 JSON */ }
internal interface IExportPeriodicNoiseArtifactUseCase { /* 只负责已采用结果的单项导出 */ }
```

不建立 `IPeriodicNoiseService` 万能接口。导入、配方导出、图片导出和候选摘要导出保持独立合同。

### 5.5 SOLID 自动审查门禁

- SRP：Document 源码不得包含 FFT、直方图、局部最大值、Notch 公式或 JSON 循环；
- OCP：增加一种诊断不修改遮罩生成；增加导出项不修改峰检测；
- LSP：用例统一遵守取消、Session 不变、失败不提交半成品和结果指纹契约；
- ISP：Document 只注入实际使用的窄用例；
- DIP：Application 依赖图片/文本/原子写入端口，Feature 不直接创建 Infrastructure；
- 组合根测试证明算法 singleton、Document scoped、Session 不跨 Scope 泄漏；
- 依赖扫描证明 Domain 不引用 Avalonia、IO、JSON、DI、SDK 或 Feature。

## 6. 领域模型与频率语义

### 6.1 坐标事实源

内部始终保存 FFT 自然索引；显示和配方使用 `FrequencyCoordinates` 导出的中心化频率：

```text
fx = kx / width, fy = ky / height, fx/fy ∈ [-0.5, 0.5)
conjugate(u,v) = ((W-u) mod W, (H-v) mod H)
```

配方保存 `fx/fy`，不保存某个代理网格的画布像素或裸 bin。这样同一陷波中心可按 cycles/pixel 映射到预算内原尺寸 FFT。
映射时必须使用固定的舍入和边界规则，并重新成对，不能用浮点取负猜测共轭显示位置。

### 6.2 建议模型

```text
PeriodicNoiseDetectionSettings
  DcExclusionRadius、RobustScoreThreshold、ProminenceThreshold、SuppressionRadius、MaximumCandidates

PeriodicFrequencyCandidate
  CanonicalFrequency、ConjugateFrequency、RobustScore、Prominence、LocalCompactness、RiskLevel、RiskReasons

PeriodicNotch
  CanonicalFrequency、Origin(Manual/Automatic)、Enabled

PeriodicNoiseRecipe
  Channel、Transition、Radius、Strength、ButterworthOrder、Notches、SchemaVersion、Fingerprint、MathematicalFingerprint

PeriodicNoiseRenderResult
  SessionFingerprint、RecipeFingerprint、IsDraft、IsFullSize、Mask、Reconstruction、FilteredSpectrum、Difference、Diagnostics
```

所有集合在构造时复制并限制数量；所有 double 验证有限值；影响数学结果的字段进入数学指纹。候选来源只用于解释，可以进入
序列化配方指纹，但不能改变同一中心和参数下的数学指纹与数值遮罩。

### 6.3 共轭对规范化

- 每个候选先用自然索引计算共轭点；
- 一对频点只保留一个 canonical 记录，canonical 规则使用两个自然线性索引的较小者；
- 自共轭 DC/Nyquist 点只保留一次并标记高风险；DC 默认禁止加入，Nyquist 只允许手动加入；
- 去重和非极大值抑制按频率环面距离执行，避免频谱边缘的相邻点被误认为很远；
- UI 始终绘制 canonical 与 conjugate，重合时明确显示“自共轭”。

## 7. 确定性候选检测

### 7.1 对数功率

对每个频点计算：

```text
L(u,v) = log(1 + |F(u,v)|²)
```

使用 `double`，拒绝 NaN/Infinity。DC 排除半径内的点不参与候选，但仍参与原始频谱显示。检测不修改 Session 频谱。

### 7.2 径向稳健背景

自然图像能量通常随半径下降，不能用一个全局亮度阈值找峰。V1 按归一化半径分桶，并使用固定数量直方图近似每个桶的
中位数与 MAD：

```text
score = (L - medianRadius) / max(1.4826 * MADRadius, epsilon)
```

桶数、直方图量化数、`epsilon` 和空桶回退规则在 G0 固定。实现采用少量线性扫描和有界 `int[]` 直方图，不对每个径向桶
保留或排序全部像素，避免 2048² 上出现不可控小对象和排序成本。

### 7.3 局部峰与有界排序

只有固定 3×3 邻域内的严格局部最大值、分数和突出度均达标的点进入临时候选。平台相等时使用自然线性索引作为稳定
tie-break。随后按分数、突出度、canonical 索引稳定排序，执行环面距离非极大值抑制，并截断为最多 64 对。

不创建全尺寸候选对象数组；扫描时只保存通过阈值的轻量结构，并在达到结构预算时使用有界选择。每行或固定样本间隔检查
取消，取消后不提交部分候选。

### 7.4 风险事实

V1 至少提供以下风险原因：

- `NearDc`：接近低频主体结构；
- `NearNyquist`：接近采样极限，可能是像素栅格或真实细纹理；
- `BroadPeakOrRidge`：邻域高分点过多，更像方向性边缘脊线；
- `DenseNeighborhood`：附近存在大量峰，可能是规则纹理；
- `LowProminence`：虽超过径向背景，但相对局部邻域不尖锐；
- `SelfConjugate`：DC/Nyquist 自共轭点；
- `LargeSuggestedLoss`：按当前半径/强度预估会修改过多频点或能量。

风险由可复现数值事实产生，不输出来源概率。风险等级只决定默认是否建议，不禁止用户手动实验。

## 8. Notch 响应与遮罩

### 8.1 参数

- 半径 `r`：以 cycles/pixel 的欧氏距离表示，G0 冻结 UI 范围和步长；必须大于 0；
- 衰减强度 `a ∈ [0,1]`：0 为全通，1 为中心完全抑制；
- 过渡 `Ideal/Butterworth/Gaussian`；
- Butterworth 阶数 `n ∈ [1,12]`，其他过渡将阶数规范化为 1；
- V1 所有启用陷波共享一组半径/强度/过渡参数，避免每个候选形成难以审查的参数表。

### 8.2 单中心响应

令 `d` 为频点到某个陷波中心的环面频率距离，衰减量 `A(d)` 和增益 `H(d)` 定义为：

```text
Ideal:       A(d) = a,                         d <= r；否则 0
Butterworth: A(d) = a / (1 + (d/r)^(2n))
Gaussian:    A(d) = a * exp(-ln(2) * (d/r)^2)
H(d) = 1 - A(d)
```

因此 Butterworth/Gaussian 在 `d=r` 时衰减量为中心衰减的一半。实现必须处理 `d=0`、指数溢出/下溢、极小半径和边界，
最终增益裁切到 `[0,1]`。代码注释要说明这里是复频谱振幅增益，不是功率 dB。

### 8.3 多陷波组合

V1 使用逐点最小增益：

```text
Htotal(u,v) = min(H1, H2, ... Hn)
```

这使重叠陷波不会因数量增加而产生隐藏的乘法叠加，单个陷波强度仍可直接解释。若未来需要级联乘法，必须作为新配方
版本设计，不能静默改变 V1 结果。

### 8.4 共轭不变量

`NotchMaskFactory` 对每个 canonical 中心显式取得其共轭中心，并按相同响应计算。完成后构造 `FrequencyGainMask`，由共享
构造门禁再次验证 `1E-12` 对称容差。任何导入配方都必须重新光栅化，不能接受外部逐 bin 增益数组。

## 9. 重建、比较与损失诊断

### 9.1 执行顺序

```text
只读 FrequencySpectrum
    ↓ NotchMaskFactory
FrequencyGainMask
    ↓ 共享 FrequencyMaskApplier
raw double 通道平面 + 最大虚部残差
    ↓ ImageChannelConverter
结果 PixelImage（Alpha 保持）
    ↓ 共享投影/质量 + 专用损失分析
结果频谱、差异、PSNR/SSIM、能量和风险摘要
```

原始频谱不得原地修改。处理后频谱预览来自 `F * H`，不能对量化后的重建 PNG 再做一次 FFT 冒充精确结果频谱。

### 9.2 通道语义

- R/G/B：只替换选定颜色通道，其他颜色通道和 Alpha 逐字节保持；
- Y/Cb/Cr：通过现有颜色转换器重建 RGB，Alpha 逐字节保持，并报告颜色重建裁切像素；
- 不提供“自动选择最佳通道”；切换通道会释放旧 Session 并要求显式重建 FFT；
- 原尺寸执行继续受共享 2048² 补零预算限制，超预算时只允许明确标注的代理结果。

### 9.3 必须展示的诊断

- 候选总数、建议数、已选择数、人工/自动来源和风险分布；
- 遮罩最小/最大/平均增益、修改 bin 数与比例；
- 原频谱总能量、移除能量和移除比例；
- 每个已选候选中心的原幅值、结果幅值和抑制比例；
- 最大虚部残差，超过 `1E-8` 直接失败；
- raw 最小/最大值、低于 0/高于 255 的样本数、颜色重建裁切数；
- 通道平均绝对差、最大绝对差、PSNR 和 SSIM；
- 代理/原尺寸、草案/已采用、当前/过期结果标识；
- 一条固定不可逆损失说明和当前风险摘要。

诊断只报告数值事实，不用单一分数宣布“修复成功”。

## 10. Session、状态与持久化

### 10.1 Session 所有权

`PeriodicNoiseSession` 独占源图、代理图、选定通道、只读频谱、原频谱预览和随机 Session 指纹。它实现 `IDisposable`，
释放后所有用例必须拒绝执行。路径、通道或代理档位变化时取消旧 generation、释放 Session、候选和结果。

### 10.2 缓存与失效

- 检测设置变化：候选、自动建议草案和相关结果过期，FFT 仍可复用；
- Notch 参数或选择变化：草案结果过期，候选和 FFT 可复用；
- 采用草案：更新已采用指纹，旧已采用结果过期，显式重建后才可导出；
- 仅切换显示页、覆盖层透明度或差异显示增益：不重跑检测/IFFT；
- 路径、通道、代理档位变化：释放整个 Session；
- 旧 generation、取消或异常结果不能覆盖新状态。

### 10.3 防抖与取消

候选检测由显式命令触发。Notch 参数连续变化采用约 150 ms 防抖，并保持“立即预览”命令；每次新请求取消旧请求，
最终只允许最后 generation 提交。测试使用可控调度或直接调用提交边界，不依赖真实时间睡眠。

### 10.4 快照

Document 快照只保存：源路径、通道、分析档位、检测设置、已采用配方、草案配方和有限 UI 选择。不得保存 FFT、候选数组、
raw 平面、图片字节或 Bitmap。恢复快照不自动读磁盘、不自动检测、不自动执行 IFFT；用户显式加载后再建立 Session。

### 10.5 配方 JSON

版本化 JSON 至少包含：

- 固定 `productId`、`schemaVersion` 和算法版本；
- 通道、半径、强度、过渡和规范化阶数；
- 最多 32 个 canonical `fx/fy`、来源和启用状态；
- 规范指纹所需字段，不保存候选分数、Bitmap、逐 bin mask 或源图路径。

导入上限 1 MiB，拒绝未知根字段、重复属性、非有限数、越界频率、超量中心和不支持版本。候选摘要另用只读导出 DTO，
不与可重放配方 schema 混合。

## 11. Document 与 UI 信息架构

### 11.1 注册与组合

由唯一 Module 登记：

```text
AddPersistableDocument<PeriodicNoiseRemovalDocument, PeriodicNoiseRemovalView>
```

完成后 Module 应为十三个 Persistable Document、零 Tool。Document 和 View 为 scoped；无状态检测器、响应和工厂为 singleton；
Session 由每个 Document 显式拥有和释放。

### 11.2 布局

- 顶部：源图、通道、分析档位、载入/检测/立即预览/采用草案/原尺寸/导出；
- 左侧：检测参数、候选表、风险筛选和手动/自动来源；
- 中央：原始频谱与处理后频谱并排，候选点和共轭点使用一致编号；
- 右侧：半径、强度、过渡、阶数、草案/采用状态和损失警告；
- 下部：原图、结果图、符号差异、绝对差异及数值诊断；
- 状态栏：Session/Recipe/Result 指纹短值、代理/原尺寸、耗时和结构化错误。

### 11.3 频谱交互

`PeriodicSpectrumControl` 只负责 letterbox、DPI、缩放、绘制和命中测试，输出归一化显示坐标。Document/Application 将它映射为
频率、canonical/conjugate 对并更新草案。左键加入/选择，明确的移除操作删除一对；不允许只删镜像一侧。

候选编号、颜色和表格选择同步；颜色不能是唯一状态编码，还要有图形、文字和风险标签。键盘可选择候选、加入/移除草案、
采用草案并访问参数；所有 Slider 同时提供精确数值输入。

### 11.4 可见规则

- 未建立 Session：检测、草案和导出禁用；
- 候选检测完成但无选择：允许手动选点，不显示“无噪声”结论；
- 草案未确认：预览区显示醒目标记，导出禁用；
- 已采用但结果过期：导出禁用并提示重新执行；
- 代理结果和原尺寸结果使用不同标签，不允许把代理导出描述成原图处理；
- 任何错误保留原 Session/配方和最后一个有效结果，不提交半成品。

## 12. 中文注释与设计说明规范

所有新增生产类型和关键算法必须使用详细中文 XML 注释：

- `summary` 解释唯一职责；
- `remarks` 解释设计原因、坐标、单位、数值公式、资源上限和误判边界；
- 候选检测说明对数功率、径向背景、MAD、局部最大值和稳定排序；
- Notch 说明半径/强度含义、振幅增益、三种公式、多陷波最小组合与共轭原因；
- Session/Document 说明所有权、取消、generation、草案/已采用和过期规则；
- JSON/IO 说明输入上限、拒绝规则和原子写入；
- 复杂循环在取消检查、索引转换、数值保护和 tie-break 处写中文行内注释。

注释解释“为什么”和不变量，不逐行翻译代码，也不写“未来自动扩展”的空泛说明。架构测试扫描新增 Domain/Application/Feature
关键文件，保证中文设计注释存在；人工审查保证内容与实现一致。

## 13. 单元测试矩阵

### 13.1 模型、校验与指纹

- 合法默认配方、三种过渡、六通道和阶数规范化；
- NaN/Infinity、越界半径/强度/频率、未知 enum、空集合和超量 32 对拒绝；
- 输入列表防御性复制、结果只读；
- 相同语义得到相同指纹，通道/中心/参数变化改变指纹；
- 候选来源不改变数学指纹，显示字段不污染数学指纹；
- JSON 严格字段、大小、版本、重复属性、未知字段、规范 round-trip 和稳定顺序。

### 13.2 坐标与共轭 Golden

- 奇偶尺寸下 display/internal/frequency 往返；
- DC、Nyquist、轴上点、四象限点和自共轭点；
- canonical 选择、pair 去重、边界环面距离和稳定 tie-break；
- 代理频率映射到原尺寸的固定舍入；
- 手动加入/移除始终成对且不重复。

### 13.3 检测核心

- 常量、零频谱和极小图片不产生候选且不产生非有限值；
- 水平、垂直、斜向单一正弦的 top candidate 在冻结误差内命中真实频率；
- 两个正弦、不同幅度、接近峰、频谱边缘和共轭重复；
- DC 排除、Nyquist 高风险、局部平台 tie-break 和非极大值抑制；
- 径向能量下降背景不会让低频整环成为候选；
- 宽脊线、密集规则纹理和低突出度候选被标记风险且不进入默认建议；
- 最大 64 候选、默认 12 建议、确定性顺序和跨重复运行逐字段一致；
- 取消不返回部分候选；资源结构不为每个频点创建对象。

### 13.4 Notch 数值 Golden

- 三种响应在中心、半径、远处和极小半径的精确值；
- Butterworth 1–12 阶、Gaussian 下溢、Ideal 边界包含规则；
- `a=0` 全通，`a=1` 中心为 0，所有结果有限且在 `[0,1]`；
- 多陷波采用逐点最小值而非乘法；
- 每个中心及共轭中心响应一致，自共轭只处理一次；
- 构造后的 `FrequencyGainMask` 满足 `1E-12` 对称门禁；
- 输入顺序变化不改变遮罩和指纹。

### 13.5 合成图像端到端

- 常量基底叠加已知水平/垂直/斜向正弦，检测命中并抑制对应峰；
- 精确全强度陷波后最大虚部残差不超过 `1E-8`；
- 受控 Golden 中结果相对无噪声基准的 MSE/PSNR 明显改善，阈值在 G0 固定；
- 陷波外频率保持，DC 在默认规则下保持；
- 全通配方 raw 与重建逐值/逐字节等价；
- R/G/B 只改选定通道并保持 Alpha，Y/Cb/Cr 报告颜色裁切；
- 原/结果频谱、符号/绝对差异、能量移除和候选抑制统计一致；
- 真实规则纹理夹具只证明“会产生候选且必须提示风险”，不编造自动区分能力。

### 13.6 Application、状态与导出

- Prepare 只解码一次并缓存只读 FFT；
- Detect 不修改频谱、配方和已采用结果；
- 自动建议只生成草案；未采用草案禁止导出；
- 采用草案、修改参数、切换路径/通道/档位的精确失效规则；
- 旧 generation、取消、异常和 dispose 后调用不能覆盖当前状态；
- 代理/原尺寸结果严格区分，超出 2048² 预算可见拒绝；
- Session/Recipe/Result 指纹不一致时导出拒绝；
- PNG、遮罩、配方和候选摘要走原子端口，取消不留下半文件；
- 两个 Document Scope 的 Session、草案、结果和取消互不影响；
- 快照轻量、无图片/FFT/Bitmap，恢复不自动 IO。

### 13.7 组合、View 与架构

- Module 按固定顺序贡献十三个稳定 Persistable Document、零 Tool；
- Headless 环境可构造真实 View、频谱控件和编译绑定；
- Standalone 从真实 Module/DI 创建第十三个独立 Scope；
- 频谱 letterbox 命中、候选/共轭坐标、键盘操作和状态可见规则；
- Domain 无 Avalonia/IO/JSON/DI/SDK，Document 无 FFT/检测/Notch/JSON 循环；
- 数值服务 singleton、Document scoped、接口保持窄；
- 产品 NuGet 白名单不变；没有 AIFLOW、Workflow、Workbench Command、Windows CI 或发布配置；
- 新增关键生产类型具备详细中文设计注释。

## 14. 本地开发门禁

### 14.1 每个实施包

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Debug --no-build --no-restore
git diff --check
```

每个 G 包都必须记录实际命令、通过数、失败数、跳过数、警告数、错误数和明确未证明事项。不得预填未来测试总数。

### 14.2 G9 完整本地封板

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Release --no-build --no-restore
git diff --check
```

通过标准：

- Debug/Release 构建均 0 警告、0 错误；
- Debug/Release 测试均 0 失败、0 跳过；
- 所有新增测试实际被发现，总数不得低于 G0 的 408；
- locked restore、架构/注释/依赖扫描和 `git diff --check` 通过；
- 测试文档只记录实跑数据，不把 Standalone 描述成 Host 或发布证据。

### 14.3 本阶段明确不执行

- 不新增或修改 Windows CI；
- 不运行 ZIP 打包、安装、升级、卸载或发布门禁；
- 不把真实 Host Catalog/Dock/布局恢复列为自动完成条件；
- 不使用 AIFLOW，也不以其他名称登记 Workflow Action；
- 不因为当前插件 RID 为 `win-x64` 就把本地开发门禁写成发布验收。

发布阶段到来时，再按 `docs/design/shared/deployment-and-release.md` 单独启用发布门禁。

## 15. 分阶段实施包

### G0：产品、数学和基线冻结

- 复跑 locked restore、Debug/Release build/test，记录 408 基线；
- 冻结候选术语、草案/采用状态、cycles/pixel、共轭规范、三类公式和最小值组合；
- 用合成频谱冻结检测桶数、阈值、突出度、风险和资源上限；
- 建立本专用目录与 G0 历史记录。

验收：没有生产功能注册；数学 Golden、误判边界和回滚点明确。

### G1：领域模型与不变量

- 实现检测设置、候选、风险、陷波、配方、指纹和校验；
- 固定最多 64 候选、32 陷波、12 自动建议和严格有限值；
- 完成坐标/canonical/conjugate Golden。

验收：模型不依赖 Avalonia/IO/JSON，非法状态不能进入算法。

### G2：确定性候选检测

- 实现对数功率、径向稳健背景、局部峰、突出度、稳定排序和 NMS；
- 实现风险评估和保守自动建议；
- 覆盖正弦、宽脊线、密集纹理、取消和上限。

验收：重复运行逐字段一致；候选不会自动进入已采用配方。

### G3：Notch 响应与遮罩

- 实现三类响应、最小值组合和共轭安全 Mask Factory；
- 构造共享 `FrequencyGainMask` 并复跑已有频域回归；
- 完成公式、边界、对称和顺序无关 Golden。

验收：遮罩有限、`[0,1]`、`1E-12` 共轭对称；未复制 IFFT。

### G4：重建、频谱和损失诊断

- 复用 `FrequencyMaskApplier` 完成代理 IFFT；
- 生成精确处理后频谱、六通道回写、差异、PSNR/SSIM；
- 完成能量移除、候选抑制、raw 越界和不可逆损失摘要。

验收：合成周期噪声端到端通过；虚部残差不超过 `1E-8`。

### G5：Session、用例和导出

- 实现 Prepare、Detect、Preview Draft、Render Full、Import/Export 窄用例；
- 完成一次解码、缓存、取消、指纹和原子文件边界；
- 完成严格配方 JSON 和候选摘要 DTO。

验收：草案禁止导出；过期/跨 Session/超预算结果被结构化拒绝。

### G6：Document、快照和组合根

- 实现草案/已采用状态、命令、generation、防抖和 Dispose；
- 新增第十三个稳定 Document ID、DI 注册和 Module 贡献；
- 更新 Standalone 为第十三个独立 Scope，保持零 Tool。

验收：两个 Scope 隔离；快照轻量且恢复不自动 IO。

### G7：频谱交互和联动 UI

- 实现候选/共轭覆盖、手动选点、候选表和参数面板；
- 完成原/结果频谱与图像/差异联动、风险和不可逆说明；
- 完成 Headless、编译绑定、键盘、DPI/letterbox 测试。

验收：所有核心流程可在真实插件 View 和 Standalone 中完成；UI 不包含数值核心。

### G8：文档与人工验收

- 完成 README、guide、user-manual、mathematical-principles、recipe-schema、testing；
- 建立 `history/g0-...g9` 并同步根 README、docs 索引、设计总览和未来能力状态；
- 执行基本、误判、损失、取消、双 Scope 和 Standalone 人工场景。

验收：文档参数、公式、限制和实跑证据与代码一致；明确未执行 Windows CI/发布门禁。

### G9：本地开发封板

- 复跑 Debug/Release 全量门禁、依赖/注释扫描和 `git diff --check`；
- 检查无跳过、无弱化断言、无未登记测试；
- 记录实际测试总数、已证明和未证明事项。

验收：只宣称“V1 本地开发封板”，不宣称发布完成。

## 16. 实际代码、测试与文档落点

### 16.1 生产代码

```text
src/ImageLabPlugin.Plugin/
  Domain/PeriodicNoiseRemoval/
    PeriodicNoiseModels.cs
    RadialSpectrumBaseline.cs
    PeriodicPeakDetector.cs
    PeriodicPeakRiskAssessor.cs
    NotchResponse.cs
    NotchMaskFactory.cs
    PeriodicNoiseLossAnalyzer.cs
  Application/PeriodicNoiseRemoval/
    PeriodicNoiseContracts.cs
    PeriodicNoiseUseCases.cs
  Infrastructure/Persistence/
    PeriodicNoiseRecipeSerializer.cs
    PeriodicNoiseCandidateSummarySerializer.cs
  Features/PeriodicNoiseRemoval/
    PeriodicNoiseRemovalDocument.cs
    PeriodicNoiseRemovalView.axaml
    PeriodicNoiseRemovalView.axaml.cs
    PeriodicSpectrumControl.cs
```

并最小更新 `PluginIds.cs`、`ImageLabPluginServices.cs`、`ImageLabPluginModule.cs` 与 Standalone 组合。最终文件可以按职责拆分，
但不得把算法、Application、Feature 和 Infrastructure 合回一个大文件。

### 16.2 测试

```text
tests/ImageLabPlugin.Tests/
  PeriodicNoiseDomainTests.cs
  PeriodicNoiseApplicationTests.cs
  PeriodicNoiseArchitectureTests.cs
```

Document 状态机与 Headless View 闭环落在现有 `ImageCodecAndUseCaseTests.cs`，组合与双 Scope 落在
`CompositionAndPersistenceTests.cs`。测试以 xUnit 事实/理论为主，不引入新的
mock 框架；小型 fake 继续围绕窄端口手写。

### 16.3 专用文档

```text
docs/design/periodic-noise-removal/
  README.md
  implementation.md
  guide.md
  user-manual.md
  mathematical-principles.md
  recipe-schema.md
  testing.md
  history/
    README.md
    g0-product-math-and-baseline.md
    ...
    g9-local-gates.md
```

本轮先建立当前 `implementation.md`。其余专用文档在相应实现包完成时同步新增，不能在代码封板后一次性补写。

## 17. 人工验收场景

### 17.1 水平与垂直扫描线

1. 载入带明显水平扫描线的图片并选择 Y；
2. 检测候选，确认频谱上成对峰与条纹方向解释一致；
3. 生成建议草案，调节三种过渡、半径和强度；
4. 对比原/结果频谱、图片和差异；
5. 采用草案后导出新 PNG，源文件不被覆盖。

### 17.2 手动陷波

1. 不运行自动检测，直接单击异常峰；
2. 确认共轭点同步加入且列表只有一对；
3. 删除任意可视点时整对删除；
4. 草案未采用前导出始终禁用。

### 17.3 真实纹理防误判

1. 载入织物、网格或建筑立面图片；
2. 观察候选的宽脊线/密集邻域/高损失风险；
3. 取消高风险候选并比较差异图中的纹理损失；
4. 确认界面从不宣称候选一定是噪声。

### 17.4 生命周期与边界

1. 在检测或预览期间切换路径/通道，旧结果不得回写；
2. 两个 Document 同时载入不同图片，候选、草案和取消互不影响；
3. 恢复快照时不自动读文件；
4. 超过原尺寸 FFT 预算时只提供代理结果和可见阻断；
5. Standalone 只作为本地 View/DI 证据，不显示 Host/发布通过文案。

## 18. 风险与回滚

| 风险 | 控制 | 回滚点 |
| --- | --- | --- |
| 真实纹理被误判 | 候选术语、风险事实、保守建议、草案确认、差异和损失 | 保留手动选择，关闭自动建议入口 |
| 径向背景估计不稳 | 合成 Golden、MAD、固定桶与空桶回退 | 回滚 G2，不影响共享 FFT |
| Notch 破坏实值对称 | 成对中心、`FrequencyGainMask` 二次校验、虚部门禁 | 回滚 G3，不修改 Applier |
| 重叠陷波损失过大 | 最小值组合、数量上限、能量损失警告 | 减少默认建议，不改 schema |
| 大图内存/耗时过高 | 代理档位、2048² 上限、有界直方图/候选、取消 | 禁用 2048 默认，仅保留显式选择 |
| 草案与导出状态混淆 | Accepted/Draft 双状态、指纹和导出用例硬拒绝 | 隐藏导出入口，保留预览 |
| 共享频域回归 | 复用公开核心前先加回归，不原地改 Session 频谱 | 回滚小型共享改进 |
| UI 复杂度过高 | 固定三栏+比较区，不建通用工作流 | 先保留参数表与静态预览 |

若任一硬门禁失败，按 G 包回滚新增类型和第十三个 Module 贡献，不修改既有十二个 Document 的稳定身份、快照和用户语义。

## 19. V1 完成检查清单

### 产品与安全

- [x] 第十三个贡献是多实例 Persistable Document，仍为零 Tool；
- [x] 自动检测只产生候选/草案，不静默应用或导出；
- [x] 手动和自动中心始终显示并应用共轭对；
- [x] 半径、强度和三种过渡语义与数学文档一致；
- [x] 原/结果频谱、图像、差异和不可逆损失同时可见；
- [x] 不把候选描述成已确认噪声，不覆盖源文件。

### 架构与质量

- [x] SOLID 依赖方向、窄接口、Scope 和资源所有权通过自动测试；
- [x] 复用 FFT、坐标、通道、质量指标、`FrequencyGainMask` 和 `FrequencyMaskApplier`；
- [x] 没有 Strategy 炫技、Mediator、Event Bus、反射框架或万能服务；
- [x] 新增生产核心具有详细、准确的中文设计注释；
- [x] 候选、配方、Session、结果和文件输入均有结构上限；
- [x] 取消、generation、stale、dispose、双 Scope 和异常路径齐全。

### 测试、文档与门禁

- [x] 合成正弦、风险纹理、Notch 公式、共轭、IFFT、六通道和导出门禁齐全；
- [x] Debug/Release build 均 0 警告、0 错误；
- [x] Debug/Release test 均 0 失败、0 跳过，并记录实际总数；
- [x] locked restore、架构/注释/依赖扫描和 `git diff --check` 通过；
- [x] 专用文档、根 README、docs 索引、未来能力和历史同步；
- [x] 未使用 AIFLOW，未新增 Windows CI，未执行或宣称发布门禁。
