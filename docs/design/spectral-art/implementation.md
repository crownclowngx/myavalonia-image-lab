# ImageLabPlugin V1 Spectral Art／频谱文字、Logo 与二维码设计与实施计划

> 计划状态：待实施；本文只冻结 V1 设计、实施顺序与本地门禁，不代表功能已完成或已发布<br>
> 基线日期：2026-08-31<br>
> 产品名称：Spectral Art／频谱文字、Logo 与二维码<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 实跑起始证据：locked restore 成功；Debug 构建 0 警告、0 错误；629/629 测试通过、0 失败、0 跳过<br>
> 核心路线：亮度通道全局 FFT + 有界图案栅格 + 必选中心共轭映射 + 径向稳健背景归一化 + 只修改幅度并保持相位 + IFFT + 空间域质量与频谱可见性联动<br>
> 首要规定：SOLID 是所有实现取舍的第一约束；设计模式只用于真实变化点并保持朴素；新增生产代码必须使用详细中文注释解释公式、坐标、共轭不变量、所有权、资源、取消和设计思路；不使用 AIFLOW；不新增 Windows CI；本阶段不执行 ZIP、真实 Host、安装或任何发布门禁

本文是 ImageLab 下一项能力的实施基线。当前仓库已有十六项产品能力、十七个多实例 Persistable Document；
Spectral Art 完成接入后才会成为第十七项产品能力、第十八个 Persistable Document。未来能力清单中的“16”是候选条目编号，
不是当前 Document 数量。

本产品把人眼可辨认的文字、Logo 或二维码外形写入全局 FFT 幅度谱。它追求的是“打开频谱图即可看见中心对称图案”，
不是从普通图片中机器恢复 Payload。它不得复用 `ImageLab Watermark V1`、DCT-QIM、Control Channel、Data Channel、
纠错、加密、BER 或 Payload 提取等协议名称和成功语义。

## 0. 计划摘要

### 0.1 当前结论

- V1 新增一个多实例 `Persistable Document`，稳定 ID 候选为 `myavalonia.plugin.image.lab.document.spectral-art`；
- 输入是一张用户显式选择的载体图，以及文字、Logo 图片或二维码图片形成的一张有界灰度图案；
- V1 固定在 Y／亮度通道工作，不提供六通道自动试探，不修改 Alpha；
- Logo 和二维码均由用户显式导入现有图片；V1 不生成、解析或验证二维码内容，也不承诺频谱中的二维码可被扫码软件识别；
- 文字先经可替换的文字栅格端口生成灰度图案，再立即固化为确定性的 `SpectralPattern`，数值核心不依赖字体系统；
- 图案只允许放入远离 DC、坐标轴和 Nyquist 自共轭点的合法主区域，并强制生成中心共轭副本；该约束不能关闭；
- 写入器修改全局 FFT 的幅度，尽量保留载体原相位；零幅值点采用固定的确定性相位规则；
- 强度以径向对数功率背景的稳健尺度归一化，避免同一滑块在低频和高频代表完全不同的能量；
- 正常图像、放大差异、原频谱、目标图案、写入后频谱和频谱差异必须同步显示；
- 质量至少报告 PSNR-Y、PSNR-RGB、全局 SSIM-Y、MAE/RMSE、最大误差、改变像素比例、裁切数和虚部残差；
- 可见性至少报告写入前后图案前景相对局部背景的稳健对比度，并以实际频谱预览作为主要事实；
- 完整尺寸 FFT 仍遵守共享 2048×2048 补零预算；超预算时结构化阻断，不静默缩小后再伪装成原尺寸结果；
- G0–G9 只执行本地开发门禁；不使用 AIFLOW，不新增 Windows CI，不执行发布门禁。

### 0.2 固定实施顺序

1. G0 冻结产品语义、坐标、幅度注入公式、Golden 样本、资源预算和专用文档骨架；
2. G1 完成图案值对象、文字／图片归一化、二值与灰度模式；
3. G2 完成嵌入区域、中心共轭映射、禁止区和目标权重栅格；
4. G3 完成径向背景、幅度注入、相位保持、零幅值和自共轭点协议；
5. G4 完成 IFFT、亮度回写、裁切、质量、差异和频谱可见性诊断；
6. G5 完成 Session、窄用例、取消、generation、预算和完整尺寸执行；
7. G6 完成严格配方／报告、PNG 导出、轻量快照和原子写入；
8. G7 接入第十八个 Persistable Document、DI、Module、Standalone 和可访问 UI；
9. G8 完成全部专用文档、索引同步和有限人工验收；
10. G9 复跑 Debug/Release 全量本地门禁并完成本地开发封板。

不得先在 View、Control、Document 或 code-behind 中直接改 `Complex[]`、计算共轭坐标或执行 IFFT，再把这些代码称为领域实现。
图案、坐标、幅度、相位、质量和资源协议必须先在 Domain 中冻结并通过自动测试。

## 1. 产品形态与用户闭环

### 1.1 产品决策

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Spectral Art／频谱文字、Logo 与二维码` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID 候选 | `myavalonia.plugin.image.lab.document.spectral-art`；只在 G7 实际接入后成为持久身份 |
| 显示名称 | `频谱艺术` |
| 显示分类 | `图像分析` |
| 载体输入 | 一张用户显式选择的图片，解码为现有非预乘 RGBA8888 `PixelImage` |
| 图案输入 | 文字、Logo 图片、二维码图片；归一化后统一为不可变 `[0,1]` 灰度权重矩阵 |
| 工作通道 | 固定 Y／亮度通道；Alpha 与源 RGB 颜色关系由既有 `ImageChannelConverter` 负责保持 |
| 变换 | 共享全局二维 FFT；完整尺寸补零后宽高均不超过 2048，总复数样本不超过 4,194,304 |
| 嵌入区域 | 中心化频谱归一化坐标中的一个主矩形；必须落在合法半平面和频率安全带内 |
| 对称映射 | 必选中心共轭映射；主图案与 180° 对称副本同时写入，不提供关闭开关 |
| 写入对象 | 只提高目标频点的对数功率／幅度，不写 Payload bit，不建立检测或提取协议 |
| 强度 | 有界有限值，按径向稳健背景归一化；`0` 是严格无操作 |
| 输出 | 与载体尺寸严格一致的新 PNG；不覆盖输入文件 |
| 预览 | 原图、结果、放大差异、原频谱、目标映射、结果频谱、频谱差异和指标 |
| 设计模式 | 文字栅格使用一个窄端口；二值／灰度缩放可用一个朴素 Strategy；其余优先 sealed 服务和值对象 |
| 明确排除 | DCT-QIM 水印复用、机器解码、AIFLOW、Workflow Action、Workbench Command、Windows CI 和发布门禁 |

### 1.2 用户闭环

```text
显式选择载体图片
    ↓
输入文字，或选择 Logo／二维码图片
    ↓
选择二值或灰度图案模式，调整阈值、反相、留白和适配方式
    ↓
在中心化频谱上移动／缩放主嵌入矩形
    ↓
查看不可关闭的中心共轭副本、禁止区和实际占用 bins
    ↓
调节强度；防抖执行幅度注入、IFFT 和亮度回写
    ↓
联动观察正常图、放大差异、写入前后频谱和可见性指标
    ↓
确认 PSNR／SSIM、裁切、虚部、能量增加和频谱可见性
    ↓
导出不覆盖原图的 PNG；可选导出配方和不含原图像素的报告
```

### 1.3 产品解释边界

- “隐藏”只表示图案主要在频谱预览中可见、空间域变化尽量较小，不表示密码学保密或不可检测；
- 频谱图案越明显，通常需要注入越多频率能量，空间域差异也会增大；UI 必须诚实呈现这一取舍；
- V1 不提供 Payload、密码、纠错、检测置信度、BER 或提取按钮；
- Logo 或二维码被写入的是外形权重，不携带由 ImageLab 定义的机器可恢复 Frame；
- 二维码源图即使在频谱中肉眼可辨，也可能因对称、缩放、对比度和频谱显示方式而无法被扫码；
- 裁剪、缩放、JPEG、滤波和重编码可能明显破坏频谱图案；V1 不宣称鲁棒性；
- PSNR/SSIM 只描述空间域差异，不证明图案“足够隐蔽”；频谱对比度也不证明所有显示器和观察者都能看清。

## 2. 当前项目事实与复用边界

### 2.1 已验证基线

2026-08-31 在当前工作树实跑：

- `dotnet restore ImageLabPlugin.slnx --locked-mode` 成功；
- Debug `--no-restore -warnaserror` 构建 0 警告、0 错误；
- Debug 测试 629/629 通过、0 失败、0 跳过；
- 当前 Module 登记十七个 Persistable Document、零个 Tool；
- 当前没有 Spectral Art 生产代码、稳定 ID、Document、View 或专用测试。

629/629 只是本计划起点。后续每个 Gate 必须填写真实测试总数，不得预填完成数字或为了数量拆分无意义测试。

### 2.2 必须直接复用

- `PixelImage`、`ImageSize`、图片解码上限、PNG 编码、原子写入和文件对话框；
- `ImageChannelConverter` 的 Y 通道抽取与回写、Alpha 保持和裁切计数；
- `Fft1DTransform`、`Fft2DTransform`、`FrequencyCoordinates`、`FrequencySpectrum` 和 2048² 资源上限；
- `FrequencySpectrumBuilder` 的补零语义，但不得让新能力依赖 Frequency Filter 的 Document 或 View；
- `SpectrumProjector` 的中心化对数幅度预览；如需“前后固定量程比较”，只增加职责一致的窄重载；
- `FullReferenceQualityAnalyzer` 的 PSNR、全局 SSIM、MAE/RMSE 和改变像素统计；
- 既有差异投影、Bitmap 生命周期、Document Scope、取消、generation 和 stale 导出惯例；
- Standalone 必须通过真实 Module 和 DI 解析真实 Spectral Art Document／View，不复制演示业务。

### 2.3 允许的共享改进

当前 `PeriodicNoiseRemoval.RadialSpectrumBaseline` 已实现径向对数功率中位数和 MAD。Spectral Art 也需要类似事实，
但两者目的不同：前者找异常峰，后者归一化写入强度。实施时允许将纯数学部分提取为
`Domain/Frequency/RadialLogPowerBaseline`，让 Periodic Noise Removal 通过窄适配继续消费。

共享提取必须满足：

- 提取前先补足 Periodic Noise Removal 的 Golden 和取消测试；
- 原候选排序、阈值、报告、指纹和现有测试结果保持不变；
- 共享类型只计算径向背景，不知道“噪声”“艺术”“Document”或 UI；
- 若两种语义不能无损统一，则 Spectral Art 保留自己的小型估计器，不为消除几行重复破坏职责。

### 2.4 禁止的错误复用

- 不调用 `WatermarkEmbedDocument`、`FrequencyMaskEditorDocument` 或 `SpectrumInspectorDocument`；
- 不复用 DCT-QIM Frame、Profile、密码学、纠错或提取结果类型；
- 不把 `FrequencyGainMask` 直接冒充图案写入结果。现有增益遮罩只能缩放已有系数，零幅值点无法可靠形成图案；
- 不把频谱遮罩编辑器的 Recipe 改名后复制；Spectral Art 有独立图案、区域、强度和报告语义；
- 不让 Domain 引用 Avalonia、Bitmap、文件路径、JSON、Features 或 Host SDK；
- 不在 Document 中保存可变 `Complex[]`，不在 View 中执行像素／频点循环；
- 不建立通用信号工作流、节点图、Event Bus、Mediator、Repository、反射算法目录或脚本层；
- 不为只有一个实现的写入器、诊断器或重建器机械创建接口和工厂。

## 3. V1 范围与非目标

### 3.1 V1 必须完成

- 一张显式载体图片；超出全局 FFT 预算时在分配大数组前阻断；
- 文字输入、Logo 图片输入、二维码图片输入三种明确来源；
- 文字字号／字重／内边距、图片阈值／反相／透明背景处理和预览；
- 图案二值模式与灰度权重模式；二维码默认二值、最近邻适配并保留用户设置的 quiet zone；
- 主嵌入矩形移动和缩放；显示坐标、归一化频率、实际 bin 尺寸和被禁止原因；
- 不可关闭的中心共轭映射；主区域和副本采用完全相同权重且旋转 180°；
- DC、中心低频、坐标轴自共轭点、Nyquist 自共轭点和越界保护；
- 强度 `0` 严格无操作；所有非零强度结果有限、确定性并保持共轭；
- 原图、结果、2×／4×／8×放大差异、原频谱、映射遮罩、结果频谱和频谱差异；
- 空间质量、裁切、频谱能量变化、虚部残差和频谱图案可见性诊断；
- 防抖预览、最终提交、取消、generation、防迟到、关闭释放和多实例隔离；
- 输出 PNG、版本化配方 JSON、版本化实验报告 JSON/CSV；
- 快照恢复不自动读取图片或执行 FFT；失效结果不得导出；
- Debug/Release 本地自动门禁、0 跳过、中文详细注释和文档同步。

### 3.2 V1 明确不实现

- 从普通图片机器检测、定位、读取或验证 Spectral Art；
- 复用或兼容 ImageLab DCT-QIM 水印、LSB Frame 或任意 Payload 协议；
- 根据文本内容生成二维码、解析二维码或验证扫码成功率；V1 接受用户已有的二维码图片；
- 自动选择“最隐蔽”区域、自动搜索强度、AI 生成 Logo 或语义理解；
- AIFLOW、Workflow Action、Workbench Command、批处理、宏、脚本或目录扫描；
- R/G/B/Cb/Cr 多通道写入、跨通道编码、彩色频谱图案；
- 可关闭的非共轭模式、任意复数写入或故意保留复数空间结果；
- 相位谱写入、DCT 写入、小波写入或混合载体；
- 分块 FFT、超过 2048² 的伪原尺寸处理、先缩小写入再放大回填；
- JPEG/WebP/AVIF 输出、覆盖输入文件或写回原文件；
- 鲁棒性保证、隐写安全保证、版权标记效力或取证结论；
- 新增 Windows CI、ZIP、签名、真实 Host 安装和任何发布门禁。

## 4. 图案协议

### 4.1 不可变领域模型

`SpectralPattern` 是与 UI、字体和文件无关的不可变值对象：

```text
Width, Height       1..512
Weights             width × height 个有限 double，范围 [0,1]
SamplingMode        BinaryNearest | GrayscaleArea
SourceKind          Text | LogoImage | QrImage
Fingerprint         由规范化尺寸、权重、模式和 schema 计算
```

- 构造时防御性复制，外部只能获得只读视图；
- 总样本不超过 262,144；宽高、乘法和缓冲长度全部 checked；
- 全零图案拒绝进入写入器；全一图案允许，但 UI 提示它更像频谱矩形而不是可辨图形；
- `Fingerprint` 不包含绝对路径；相同规范化图案必须得到相同指纹；
- 图案权重只表达“应增加多少频谱对数功率”，不表达 bit、字符或 Payload。

### 4.2 文字来源

- Application 定义窄端口 `ISpectralTextRasterizer`，输入文本、字体族、字号、字重、内边距和最大图案尺寸；
- Avalonia 实现位于 Infrastructure／Presentation 边界，不把 `FormattedText`、字体对象或 Bitmap 传入 Domain；
- 栅格完成后立即转成 `SpectralPattern`，此后嵌入结果只由固化权重决定；
- 相同平台字体渲染是否逐像素一致不作为跨机器数学协议；固化后的 Pattern 指纹才是本次实验事实；
- 文本为空、只有不可见字符、字体不可用或栅格后全透明时结构化失败；
- UI 必须说明导出的配方可能包含可恢复的图案轮廓，不应把敏感文字当作秘密。

### 4.3 Logo 与二维码图片

- 通过既有受限图片解码端口读取，不直接使用 Avalonia Bitmap 作为领域输入；
- Alpha 为 0 的像素权重固定为 0；其余像素先合成到用户选择的黑／白背景，再转亮度；
- 二值模式使用显式阈值和反相选项；灰度模式将亮度／Alpha 组合映射到 `[0,1]`；
- Logo 默认 `GrayscaleArea`，二维码默认 `BinaryNearest`；用户可显式切换，但报告必须记录；
- 二维码 quiet zone 由用户预览和设置，工具不解析版本、纠错级别或模块矩阵；
- 导入图片路径不进入报告；快照和配方只保存有界规范化图案或其显式引用策略。

### 4.4 图案适配

- `Contain`：完整保留图案比例，区域剩余部分填 0；V1 默认；
- `Stretch`：拉伸到主区域，允许图案变形并必须显示警告；
- 二值最近邻不得产生灰边；灰度面积采样用于缩小，避免高频细线随机消失；
- 任何图案旋转都在映射前完成；共轭副本始终是主映射的 180° 点对称，不单独编辑。

## 5. 频率坐标、区域与对称协议

### 5.1 坐标事实

继续复用 `FrequencyCoordinates`：

- FFT 自然索引 `(ix,iy)` 用于数组；
- 中心化显示坐标 `(dx,dy)` 用于 UI，中心为 DC；
- 有符号频率 `fx=kx/W`、`fy=ky/H`；
- `ConjugateIndex(ix,iy) = ((W-ix)%W, (H-iy)%H)`；
- 实值空间信号必须满足 `X[-k] = conjugate(X[k])`。

View 的缩放、DPI、letterbox 和滚动偏移不进入领域坐标。专用坐标映射器把指针提交为归一化显示坐标，
Domain 再按固定的 `MidpointRounding.ToEven` 离散到 bin。

### 5.2 主区域

`SpectralArtRegion` 使用中心化归一化矩形：

```text
Left, Top, Right, Bottom ∈ [-0.5, 0.5]
区域为闭开区间 [left,right) × [top,bottom)
最小实际尺寸 8×8 bins
最大实际占用不超过频谱总 bins 的 20%
```

主区域必须完全位于规范半平面：`fy < 0`，或 `fy == 0 && fx > 0`。V1 默认把图案放在上半频谱，副本自动落在下半。
区域不能跨越 DC、中心坐标轴或 Nyquist 边界，也不能与自己的共轭副本重叠。

### 5.3 禁止区

- 归一化径向半径 `ρ < 0.08` 默认禁止，避免直接改变平均亮度和主要结构；
- `|kx| <= 1` 或 `|ky| <= 1` 的中心轴保护带禁止，避免触及自共轭和强轴能量；
- 偶数尺寸下的 Nyquist 行／列及相邻一格保护带禁止；
- DC 和全部自共轭 bin 永远不修改，即使未来允许扩大区域；
- 保护带常量属于协议，G0 通过 Golden 和视觉样本冻结；发布后变更必须升级 recipe schema／protocol ID。

### 5.4 中心共轭映射

对主区域中每个图案权重 `p(k)`：

```text
p(-k) = p(k)
```

写入器只遍历每对频点的规范代表一次，然后同时提交 `k` 与 `-k`。UI 必须同时显示：

- 主区域边框；
- 180° 对称副本边框；
- 主图案和对称副本；
- DC／轴／Nyquist 禁止区；
- 实际命中、被留白和被拒绝的 bin 数。

“对称映射”是实值 IFFT 的数学前提，不是装饰性复选框。V1 不提供关闭它的高级模式。

## 6. 幅度注入数学协议

### 6.1 对数功率

对一对共轭频点先取共同幅度，再定义对数功率：

```text
Mpair(k) = (|X(k)| + |X(-k)|) / 2
L(k) = log(1 + Mpair(k)^2)
```

共同幅度防止浮点噪声使一对系数在写入后产生细小不对称。对数功率比线性幅度更适合预览和跨频带调节。
自然图像低频通常远强于高频，不能用一个固定线性增量覆盖全部半径。

### 6.2 径向稳健尺度

按共享归一化半径划分 128 个桶，对每桶计算 `L` 的中位数 `median(r)` 和 MAD：

```text
scale(r) = max(1.4826 × MAD(r), 0.15)
```

`0.15` 是平坦或常量输入的最小对数功率步长候选值，必须在 G0 Golden 中校准后冻结；不得根据机器性能变化。

### 6.3 写入公式

令图案权重 `p(k) ∈ [0,1]`，用户强度 `s ∈ [0,8]`：

```text
L'(k) = L(k) + s × p(k) × scale(r)
M'(k) = sqrt(exp(L'(k)) - 1)
```

- `s=0` 必须走严格无操作短路，不执行会造成舍入差异的 FFT 往返；
- 指数计算前检查上界和有限值；任何 NaN／Infinity 都使本次结果失败，不提交半成品；
- 只增加目标区域能量，不降低载体原幅度；这样图案语义明确，代价是空间域能量只增不减；
- 强度上限是资源／数值安全边界，不是推荐值；UI 默认候选值为 `2.0`，G0 通过样本校准后冻结；
- V1 不自动追逐 PSNR 或可见性目标，避免隐藏的迭代搜索和不可解释参数变化。

### 6.4 相位保持和共轭提交

对非零系数：

```text
X'(k) = M'(k) × X(k) / |X(k)|
X'(-k) = conjugate(X'(k))
```

- 使用规范代表的相位一次性生成成对系数，不能分别计算后依赖浮点“恰好相等”；
- 若 `|X(k)| <= 1E-12`，规范代表相位固定为 0，对应副本取共轭；该分支必须有中文注释和 Golden；
- 自共轭 DC／Nyquist 点不修改；
- 输入共轭残差超过冻结容差时失败，不能先“修好”未知频谱继续；
- 原 `FrequencySpectrum` 不可变，写入器只操作独占工作副本。

### 6.5 IFFT 数值门禁

- 使用共享 `Fft2DTransform.Inverse`；
- padded 空间平面的最大虚部残差必须不超过 `1E-8`；
- 超出门禁视为共轭不变量或数值实现失效，不允许静默丢弃虚部；
- IFFT 后只裁回原图尺寸，补零区不进入结果图；
- raw Y 值保留 double，直到统一回写时才按 `ToEven` 量化和 clamp。

建议新建 `SpectralAmplitudeWriter`，而不是扩大 `FrequencyMaskApplier` 的职责。前者负责“根据图案与径向尺度创建新幅度”，
后者继续负责“实数增益乘法”。这符合单一职责，也避免用可选参数把两种数学语义塞进一个万能类。

## 7. 重建、质量与可见性

### 7.1 亮度回写

- 从源 `PixelImage` 抽取 Y 平面并构建频谱；
- IFFT 得到 raw Y 后通过 `ImageChannelConverter.Apply` 回写；
- 原 Alpha 逐像素保持；
- 记录低于 0、高于 255 的 raw 样本数、最小／最大值、裁切像素和裁切 RGB 分量数；
- 输入对象和缓存频谱不得被修改；输出始终是新 `PixelImage`。

### 7.2 空间域质量

复用 `FullReferenceQualityAnalyzer`，至少显示和报告：

- PSNR-Y、PSNR-RGB；
- 全局 SSIM-Y；
- MAE-RGB、RMSE-RGB、最大绝对 RGB 误差；
- RGB 改变像素数／比例；
- Alpha 误差，正常结果应严格为 0；
- raw 越界数、裁切像素／分量数；
- 2×、4×、8×放大绝对差异图。

V1 可将 `PSNR-Y < 40 dB` 或 `SSIM-Y < 0.98` 显示为醒目质量提醒，但不自动禁止导出。这两个值是 UI 提醒候选，
须在 G0 样本上校准后冻结；不得描述为人眼不可见的普适阈值。

### 7.3 频谱可见性

实际结果重新执行 FFT 或直接消费写入后的工作频谱，使用与 Spectrum Inspector 相同的中心化固定量程预览。为避免
“前后各自自动拉伸”制造虚假提升，原频谱与结果频谱必须共享同一显示上限。

对图案前景与同区域留白背景计算：

```text
foreground = p(k) >= 0.5 的写入后平均对数功率
background = p(k) <= 0.05 的同区域写入后平均对数功率
visibility = (foreground - background) / max(局部稳健尺度, 0.15)
```

同时报告写入前基线、写入后值和增量。该数值只用于同一次实验的相对比较，不称为识别率、置信度或扫码成功率。
若图案没有足够前景或背景样本，则返回结构化 N/A，而不是 0。

### 7.4 频谱与能量诊断

- 实际写入的频点对数、独立主频点数和总占用比例；
- 原／结果频谱总能量和增加比例；
- 主区域、共轭区域和其他区域的能量变化；
- 相位最大偏差；除零幅值固定相位分支外应接近数值误差；
- 最大共轭残差和 IFFT 最大虚部残差；
- Recipe、Pattern、Session 和 Result 指纹。

## 8. 架构与 SOLID 落点

### 8.1 依赖方向

```text
Features/Avalonia View
        ↓ 绑定与命令
Features/SpectralArtDocument
        ↓ 只调用窄用例
Application/SpectralArt
        ↓ 编排、取消、Session、端口
Domain/SpectralArt + Domain/Frequency + Domain/Imaging + Domain/Comparison
        ↑
Infrastructure/Imaging + Infrastructure/Persistence
```

- Domain 不知道 Avalonia、文件、JSON、Host 和 Document；
- Application 不创建 Bitmap，不直接弹对话框；
- Infrastructure 实现文字栅格、严格 JSON 和现有 IO 端口；
- Document 管理 UI 状态、命令、generation 和 Bitmap 所有权，不实现频域公式；
- View/code-behind 只处理指针捕获、坐标转发和视觉状态，不直接写频谱数组。

### 8.2 SOLID 逐项约束

| 原则 | 本能力的具体门禁 |
| --- | --- |
| SRP | 图案规范化、区域映射、幅度写入、诊断、序列化、Document 各有单一变化原因 |
| OCP | 文字与图片先归一成同一 `SpectralPattern`；新增来源不修改幅度写入器 |
| LSP | 只在真实替换边界使用端口；所有实现遵守同一尺寸、取消和失败语义 |
| ISP | 文字栅格、配方序列化、报告序列化分别使用窄端口，不建立万能媒体服务 |
| DIP | Document／用例依赖抽象端口；Domain 数值核心不依赖 Avalonia 或文件系统 |

### 8.3 朴素设计模式

- 二值最近邻与灰度面积适配存在真实算法差异时，可使用一个两实现 Strategy；
- 文字栅格是外部平台边界，使用 Port／Adapter；
- Session 和 Result 使用不可变值对象／受控所有权对象；
- DI 只做构造注入；不使用 Service Locator；
- 不为每个 record 建 Builder、Factory、Visitor 或抽象基类；
- 不使用 CQRS、Event Sourcing、Pipeline 框架或责任链包装简单的顺序用例。

### 8.4 建议职责

| 类型 | 只负责 | 明确不负责 |
| --- | --- | --- |
| `SpectralPatternNormalizer` | 把已解码灰度／Alpha 变成有界 Pattern | 字体、FFT、文件 |
| `SpectralPatternMapper` | 区域离散、适配和中心共轭权重 | 改复数系数、质量指标 |
| `SpectralAmplitudeWriter` | 径向尺度、幅度公式、相位和共轭提交 | 图片解码、UI、导出 |
| `SpectralArtReconstructor` | IFFT、裁回和 Y 平面结果 | Avalonia Bitmap、对话框 |
| `SpectralArtDiagnostics` | 质量、能量、可见性和不变量摘要 | 改写结果或选择参数 |
| `SpectralArtSession` | 独占源图、Y 平面、频谱和预览事实 | Document、ServiceProvider |
| `SpectralArtDocument` | 状态、命令、Bitmap、取消和 generation | FFT、JSON 和像素循环 |

## 9. Session、缓存、并发与资源

### 9.1 Session 所有权

每个 Document Scope 独占一个 `SpectralArtSession`：

- 已解码源 `PixelImage`；
- Y 通道 double 平面；
- 一份只读 `FrequencySpectrum`；
- 原频谱预览所需的轻量事实；
- 源尺寸、padded 尺寸、源指纹和 Session 指纹；
- 预算说明和可执行状态。

Session 不持有 Document、View、Bitmap、ServiceProvider、配方历史或跨图片静态缓存。释放后拒绝使用。

### 9.2 缓存失效

| 变化 | 重建 Session／FFT | 重映射图案 | 重做写入／IFFT | 只更新显示 |
| --- | --- | --- | --- | --- |
| 更换载体图片 | 是 | 是 | 是 | 是 |
| 更换文字／Logo／二维码或阈值 | 否 | 是 | 是 | 是 |
| 移动／缩放区域、适配方式 | 否 | 是 | 是 | 是 |
| 修改强度 | 否 | 否 | 是 | 是 |
| 差异放大倍数、频谱缩放、面板布局 | 否 | 否 | 否 | 是 |
| 导入配方 | 仅当显式重新选择载体后 | 是 | 是 | 是 |

任一影响 Pattern、Region、Strength 或源图片的变化都使旧 Result stale；stale 结果可留作视觉参考，但不得正式导出。

### 9.3 调度与取消

- 图案和区域拖动即时更新轻量目标映射；
- FFT 写入／IFFT 使用 150–250 ms 防抖候选值，Pointer release 强制安排最终执行；
- 同一 Document 同时最多一个重建；新请求先推进 generation，再取消旧令牌；
- 图案栅格按行、映射按行、频谱扫描按固定块、FFT 按行／列、诊断按行检查取消；
- 取消不作为错误，不提交半成品，不覆盖最后一个有效结果；
- 关闭 Document 取消任务、释放 Bitmap／Session，并拒绝任何迟到回调；
- Dispatcher 只提交最终小状态与 Bitmap，不执行大数组循环。

### 9.4 结构资源预算

2048×2048 最坏情形的大致同时存活内存：

- Session 只读 `Complex[]` 约 64 MiB；
- 写入／IFFT 工作 `Complex[]` 约 64 MiB；
- Y 平面与 raw 结果各约 32 MiB；
- 径向对数功率／诊断临时数组最多约 32 MiB；
- 源／结果 RGBA 各约 16 MiB；
- 频谱／差异预览、Bitmap 和框架对象另有开销。

目标是通过流式统计和及时释放，把单个活动 Document 的结构预算控制在约 300 MiB 内。该数值不是进程峰值或性能承诺。
自动测试检查数组上限、同时持有关系和分配前门禁，不使用易受 GC／机器影响的严格工作集断言。

### 9.5 完整尺寸边界

Spectral Art 的输出语义要求与载体尺寸一致，因此 V1 不提供“代理结果冒充原图结果”：

- 原图宽高经 `NextPowerOfTwo` 后均不得超过 2048；
- padded 样本不得超过 4,194,304；
- 超限时保留输入和参数，显示明确原因，不分配 FFT 工作副本；
- 可以显示普通缩略图，但不得生成或导出伪完整 Spectral Art；
- 后续若支持更大图片，必须另立分块／外存全局 FFT 设计，不能静默改变 V1 协议。

## 10. Application 用例

### 10.1 Prepare Carrier

`IPrepareSpectralArtCarrierUseCase`：

- 通过现有图片端口解码一次；
- 在大数组分配前检查图片和 padded FFT 预算；
- 抽取 Y 平面、建立只读频谱和原始频谱预览事实；
- 返回独占 Session；失败或取消不返回半成品。

### 10.2 Create Pattern

`ICreateSpectralPatternUseCase`：

- 文字来源调用窄栅格端口；图片来源调用现有受限解码端口；
- 应用背景、阈值、反相、二值／灰度和最大尺寸；
- 生成不可变 Pattern、预览、统计和指纹；
- 不接触载体 FFT。

### 10.3 Render Preview

`IRenderSpectralArtUseCase`：

- 校验 Session、Pattern、Region、Strength 和所有资源上限；
- 映射主图案与中心共轭副本；
- 写入幅度、执行 IFFT、回写 Y；
- 生成固定量程频谱、差异、质量、能量和可见性诊断；
- 返回绑定 Session／Recipe／Pattern 指纹的不可变 Result；
- 失败或取消不修改 Session 和最后有效 Result。

### 10.4 Export

- PNG 只接受当前未过期的完整 Result；
- 写入临时文件、正式回读、验证尺寸／Alpha／关键像素事实后原子替换；
- 配方／报告使用严格 schema 和 UTF-8；
- 用户取消、编码失败或原子发布失败时保留内存结果，可重试；
- 默认文件名不包含文字内容、Logo 名、二维码内容或原绝对路径。

## 11. Document、UI 与交互

### 11.1 Document 状态

建议状态：

```text
Empty
CarrierReady
PatternReady
ReadyToRender
Rendering
Completed
CompletedWithQualityWarning
Blocked
Faulted
Disposed
```

Document 另行维护 `IsDirty`、`Revision`、`IsResultStale`、`Generation` 和当前取消源。异常转为结构化中文消息，
不把堆栈直接显示给用户。

### 11.2 页面布局

- 顶部：载体选择、图案来源、运行／取消、PNG／配方／报告导出；
- 左侧：文字或图片参数、二值／灰度、阈值、反相、适配、区域和强度；
- 中间：正常图与结果的并排／分割线视图，可切换 2×／4×／8×差异；
- 右侧：原频谱、目标映射、结果频谱、频谱差异四视图；
- 底部：PSNR／SSIM、可见性、能量、裁切、共轭、虚部、耗时和限制说明。

### 11.3 频谱区域控件

- 使用专用轻量 Control 画频谱、主区域、副本、禁止区和拖拽手柄；
- 控件只输出归一化矩形意图，不创建 Pattern、不写频谱；
- letterbox、DPI、边界、最后像素和 Pointer capture 必须有自动测试；
- 键盘可用方向键移动、Shift 加速、可访问名称读取区域和频率；
- 非法拖动显示原因并保留最后合法区域，不把非法坐标 clamp 成另一个未告知结果。

### 11.4 可见性预览

- 原频谱与结果频谱固定使用同一对数显示量程；
- 目标映射单独显示 Pattern 权重，不冒充实际结果；
- 频谱差异只显示真实写入后变化；
- 结果频谱必须从实际工作频谱生成，PNG 回读检查则从回读图片重新 FFT；
- 默认放大频谱区域，但允许一键查看全谱和中心对称关系；
- UI 明示“看见图案”和“空间图变化小”是需要平衡的两个目标。

## 12. 持久化、配方、报告与隐私

### 12.1 Document 快照

快照 schema 1 只保存：

- 载体最近路径的现有轻量引用策略；
- Pattern 来源种类、规范化参数和有界压缩权重；
- 区域、强度、适配、阈值、反相和显示参数；
- Dirty／Revision 所需的轻量状态。

快照不保存源图／结果图像素、Complex、Y/raw 数组、Bitmap、质量缓存或异常堆栈。恢复后不自动读图、不执行 FFT；
用户必须显式确认载体。压缩 Pattern 解码前后都执行尺寸与 128 KiB 候选字节上限。

### 12.2 独立协议名称

建议固定：

```text
Recipe protocol: spectral-art-fft-amplitude-v1
Recipe schema:   1
Report schema:   spectral-art-report-v1
```

严禁使用 `ImageLab Watermark V1`、`IWM1`、DCT-QIM 或“水印提取协议”等名称。代码命名也使用 `SpectralArt`、
`Pattern`、`Recipe`、`Visibility`，不使用 `PayloadFrame`、`EmbedBits`、`Extract` 或 `DecodeWatermark`。

### 12.3 配方 JSON

配方应包含：

- schema、protocol、Pattern 尺寸／模式／压缩权重／指纹；
- 来源种类与规范化参数；
- 主区域归一化坐标；
- 强度、径向背景协议、保护带协议和采样模式；
- 不包含载体像素、输出像素和绝对路径。

反序列化拒绝未知 schema、未知 enum、重复关键字段、非有限数、越界区域、超长文本、超限权重和尾随垃圾。

### 12.4 实验报告

JSON／CSV 报告包含：

- 软件／schema／protocol 版本；
- 图片宽高、padded 宽高和无路径的输入指纹；
- Pattern 种类、尺寸、指纹、区域和强度；
- 写入 bins、能量、相位、共轭和虚部诊断；
- PSNR／SSIM／误差／裁切；
- 写入前后可见性与 N/A 原因；
- 执行／取消／失败状态和阶段耗时；
- “非 Payload 水印、非扫码保证、非鲁棒性保证”的固定限制文本。

报告默认不包含原文字、Logo／二维码像素、绝对路径、原图／结果图像素或频谱数组。用户显式导出的配方为复现实验可包含
压缩 Pattern，因此导出前必须有清晰隐私提示。

## 13. 自动测试与质量门禁

### 13.1 图案与映射

- Pattern 非法尺寸、长度不匹配、NaN／Infinity、越界权重和全零失败；
- 构造后输入数组修改不影响 Pattern，外部读取不能改内部状态；
- 二值阈值、反相、透明背景、灰度权重、Contain／Stretch 和二维码最近邻 Golden；
- 文字栅格端口失败、空白文本、超限文本和取消；Domain 测试使用确定性 fake，不依赖机器字体；
- 主区域闭开边界、ToEven 离散、最小 8×8、20% 占用上限和禁止区；
- 任一命中点的共轭点权重严格相同；主／副区域不重叠；DC／轴／Nyquist 永不命中；
- 非方形频谱、奇偶边界、最小尺寸和 2048 边界坐标。

### 13.2 幅度与数值 Golden

- 强度 0 严格返回源对象克隆语义或逐字节相同结果，不做有损往返；
- 常量、冲激、单正弦、棋盘格、线性渐变和确定性纹理输入；
- 对数功率、径向中位数、MAD、最小尺度和强度公式；
- 非目标频点不变，目标频点幅度非减；
- 非零目标频点相位保持；零幅值固定相位；自共轭点不变；
- 输出频谱共轭残差、IFFT 虚部残差 `<= 1E-8`；
- 源频谱和 Pattern 不变，重复运行位级／容差内确定性一致；
- 指数溢出、非有限输入和超强度在提交前失败；
- 取消发生在扫描、写入、FFT、回写和诊断时不提交半成品。

### 13.3 重建、质量与可见性

- 结果尺寸与源图一致，Alpha 逐字节一致；
- raw 越界、ToEven、裁切像素和裁切分量统计准确；
- 既有 `FullReferenceQualityAnalyzer` Golden 不变化；
- 固定量程原／结果频谱避免各自拉伸；
- 前景／背景充足时 visibility 可复算；不足时返回 N/A；
- 提高强度在固定 Golden 上不降低目标区域平均对数功率；
- 频谱差异只出现在映射区域及其共轭副本的容差范围；
- PNG 正式编码／回读后尺寸和 Alpha 正确，并重新计算频谱可见性；
- 不用“自然图片看起来不错”代替数值断言。

### 13.4 Application、生命周期与文件

- 超出 FFT、Pattern、快照和报告预算时在大分配前阻断；
- Prepare／Pattern／Render／Export 各用例失败不污染上一次有效状态；
- 新请求只有最后 generation 可提交；取消、关闭和 Dispose 拒绝迟到结果；
- 两个 Scope 的源、Pattern、Session、强度、结果、取消和 Bitmap 完全隔离；
- 源／Pattern／区域／强度变化使旧 Result stale，显示参数变化不误判 stale；
- 快照恢复不自动读图／FFT，未知 schema 安全回退；
- 配方严格 JSON 往返、未知字段策略、非有限值、重复字段、大小前后门禁；
- PNG、JSON、CSV 原子失败不覆盖已有目标，不覆盖源图片；
- 文件名、报告和错误消息不泄露绝对路径或原文字。

### 13.5 Document、UI、组合与架构

- 完成 G7 后 Module 按固定顺序登记十八个 Persistable Document、零 Tool；
- Spectral Art 使用独立稳定 ID、独立协议，不引用 Watermarking Application／Infrastructure；
- Document 源码不包含 `Complex`、FFT 循环、共轭计算、图案栅格或 JSON 解析；
- Domain 命名空间不引用 Avalonia、Features、Infrastructure、Host SDK、文件系统或 JSON；
- View/code-behind 不出现幅度公式或直接数组写入；
- Headless 可创建 View，编译绑定、命令启禁、错误态和关键可访问名称正确；
- 频谱控件覆盖 letterbox、DPI、边界、拖动、键盘和非法区域保留；
- 源码／项目扫描证明没有 AIFLOW、Workflow、Workbench Command、Windows CI 或发布配置；
- 新增核心类型、公式、坐标、所有权、资源和取消边界具有详细中文注释。

### 13.6 回归门禁

- 当前 629 个测试全部继续通过；
- Spectrum Inspector 的 FFT／坐标／投影 Golden 不变；
- Frequency Filter 与 Frequency Mask Editor 的增益／IFFT 语义不变；
- Periodic Noise Removal 若提取径向背景，共享前后候选与报告 Golden 不变；
- DCT-QIM 水印三种 Profile、Frame、提取、纠错、安全和回读测试不变；
- Composition 测试只在 G7 接入时从十七个更新为十八个 Document，仍为零 Tool；
- 不删除、跳过、放宽或改写既有测试来换取通过。

### 13.7 本地开发门禁命令

每个 G 包至少执行相关测试和 Debug 全量测试；G9 完整执行：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

硬条件：

- locked restore 成功且依赖变化是显式、可审计的；
- Debug／Release 构建均 0 警告、0 错误；
- 两配置新旧测试全部通过、0 失败、0 跳过；
- 实际测试总数大于 629，但不得为数字拆分无意义测试；
- 共轭、虚部、相位、零强度、资源、取消、stale、Scope 和协议隔离均有自动证据；
- `git diff --check` 通过；
- 文档状态、测试数量和代码事实一致；
- 没有新增 AIFLOW、Windows CI、发布脚本或无关改动。

### 13.8 本阶段明确不做的门禁

- 不创建或修改 GitHub Actions、Azure DevOps 等 Windows CI；
- 不运行插件 ZIP／发布 Target；
- 不执行真实 Host 安装、升级、卸载、Dock 或布局恢复验收；
- 不把 Standalone／Headless 结果冒充真实 Host 证据；
- 不修改发布版本、签名、市场资料和发布清单；
- 不声明产品已经发布。

准备发布时，必须由用户明确进入发布阶段，再按 `docs/design/shared/deployment-and-release.md` 单独执行。

## 14. 分阶段实施包

### G0：产品、数学与基线冻结

- 实跑 locked restore、Debug／Release build/test，记录实际起始数量；
- 冻结独立协议名称、Y 通道、区域、禁止区、共轭和强度语义；
- 校准 `scale` 下限、默认强度和质量提醒，但不引入自动优化；
- 准备常量、冲激、正弦、棋盘格、渐变、确定性纹理和小型 Pattern Golden；
- 冻结 Pattern、FFT、快照、报告和内存预算；
- 创建专用 README、数学、测试、schema 和 `history/g0-*.md`；
- 不登记 Document，不做 UI。

验收：重要公式、默认值、错误语义和延期项无未决选择；原代码基线无变化。

### G1：Pattern 与输入归一化

- 先实现 `SpectralPattern`、规范化参数、指纹和防御性复制测试；
- 实现图片亮度／Alpha、阈值、反相、二值／灰度和尺寸上限；
- 实现 BinaryNearest／GrayscaleArea 两个朴素适配策略；
- 建立文字栅格窄端口和 Headless 基本集成；
- 完成 Logo／二维码图片正式解码测试。

验收：所有下游只消费不可变 Pattern，不依赖字体、Bitmap 或文件路径。

### G2：区域与共轭映射

- 实现 Region 值对象、规范化、离散和禁止区；
- 实现 Pattern 到主区域的适配；
- 实现不可关闭的中心共轭映射和命中统计；
- 覆盖非方形、边界、自共轭、重叠、最小区域和占用上限；
- 输出轻量映射预览，不做 FFT 写入。

验收：任一合法映射有限、确定性、成对且不触碰禁止点。

### G3：幅度写入核心

- 必要时以回归保护提取共享径向对数功率背景；
- 实现 `SpectralAmplitudeWriter` 和写入结果；
- 冻结对数功率、稳健尺度、相位保持、零幅值和自共轭协议；
- 加入非有限、指数上界、取消和源不可变门禁；
- 完成频谱级 Golden，不做图片 UI。

验收：目标幅度、相位、共轭和确定性全部通过；无第二套 FFT 实现。

### G4：IFFT、回写与诊断

- 执行共享 IFFT 并验证 `1E-8` 虚部门禁；
- 裁回原尺寸并通过既有 Y 通道回写；
- 生成固定量程前后频谱、映射、频谱差异和空间差异；
- 计算质量、能量、裁切、相位、共轭和可见性；
- 完成零强度、常量、单频、纹理、透明 Alpha 和裁切 Golden。

验收：输出尺寸／Alpha 正确，指标可复算，结果不依赖 Avalonia UI。

### G5：Session、用例与资源

- 实现 Prepare／CreatePattern／Render 用例和独占 Session；
- 在每个大分配前执行 checked 预算；
- 实现 generation、取消、防迟到、Dispose 和 stale 绑定；
- 实现 150–250 ms 防抖调度边界，但不把调度器放入 Domain；
- 完成多 Scope、取消点、最大合法尺寸和超限失败测试。

验收：用例失败不提交半成品，Document Scope 之间无共享可变状态。

### G6：配方、报告、快照与导出

- 实现 strict recipe schema 1、报告 JSON／CSV 和大小门禁；
- 实现有界 Pattern 压缩和隐私提示；
- 实现 PNG 原子导出、正式回读和回读频谱诊断；
- 实现快照 schema 1，恢复不自动 IO／FFT；
- 覆盖未知 schema、重复字段、非有限、尾随数据和原子失败。

验收：结果、配方和报告语义独立于 DCT-QIM；不存在路径／Payload 泄漏。

### G7：Document、组合根、Standalone 与 UI

- 增加稳定 ID、scoped Document、View、专用频谱区域控件和编译绑定；
- 在 DI 注册无状态服务、端口适配和 scoped 状态；
- Module 更新为十八个 Persistable Document、零 Tool；
- Standalone 通过真实 Module 创建第十八个 Document；
- 完成状态、命令、Bitmap 释放、Headless、坐标、键盘和可访问性测试。

验收：Document 不含频域数学，UI 线程无大循环，多实例完全隔离。

### G8：专用文档与人工验收

- 完成 README、guide、user-manual、mathematical-principles、testing、recipe-schema、report-schema；
- 填写 G0–G8 实际历史记录；
- 同步根 README、`docs/README.md`、`docs/design/README.md`、未来能力和共享图像领域边界；
- 人工检查文字、Logo、二维码、区域、共轭、强度、差异、指标和导出；
- 明确记录未执行真实 Host、ZIP、Windows CI 和发布门禁。

验收：普通用户、开发者和维护者均有单一入口，文档没有超出实际证据。

### G9：本地开发封板

- 完整执行第 13.7 节 Debug／Release 命令；
- 记录实际测试数量、警告、失败、跳过、耗时和环境；
- 复核 SOLID、中文注释、协议隔离、资源、取消和隐私；
- 检查无 AIFLOW、Windows CI、发布文件和无关改动；
- 只有全部通过后才把状态改为“V1 本地开发封板”。

验收：全部本地门禁通过，或如实保留未完成状态并记录阻断；不得用计划勾选代替证据。

## 15. 预计代码、测试与文档落点

### 15.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Application/
│  └─ SpectralArt/
│     ├─ SpectralArtContracts.cs
│     └─ SpectralArtUseCases.cs
├─ Constants/
│  └─ PluginIds.cs
├─ Domain/
│  ├─ Frequency/
│  │  └─ RadialLogPowerBaseline.cs       # 仅在可无损共享时提取
│  └─ SpectralArt/
│     ├─ SpectralPattern.cs
│     ├─ SpectralArtRecipe.cs
│     ├─ SpectralPatternMapper.cs
│     ├─ SpectralAmplitudeWriter.cs
│     ├─ SpectralArtReconstructor.cs
│     └─ SpectralArtDiagnostics.cs
├─ Features/
│  └─ SpectralArt/
│     ├─ SpectralArtDocument.cs
│     ├─ SpectralArtView.axaml
│     ├─ SpectralArtView.axaml.cs
│     ├─ SpectralArtCanvasControl.cs
│     └─ SpectralArtCoordinateMapper.cs
├─ Infrastructure/
│  ├─ Imaging/
│  │  └─ AvaloniaSpectralTextRasterizer.cs
│  └─ Persistence/
│     ├─ SpectralArtRecipeSerializer.cs
│     └─ SpectralArtReportSerializer.cs
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs
```

这是职责落点，不要求为了文件数机械拆分。紧密且短小的值对象可以合并；Domain、用例、Document、View 和文件 IO
不得重新塞进一个大类。

### 15.2 测试

建议按职责新增：

- `SpectralPatternTests.cs`；
- `SpectralPatternMappingTests.cs`；
- `SpectralAmplitudeWriterTests.cs`；
- `SpectralArtReconstructionTests.cs`；
- `SpectralArtUseCaseAndReportTests.cs`；
- `SpectralArtDocumentTests.cs`；
- `SpectralArtViewTests.cs`；
- `SpectralArtArchitectureTests.cs`；
- 对 `SpectrumDomainTests`、`PeriodicNoiseDomainTests`、`ImageCodecAndUseCaseTests`、
  `CompositionAndPersistenceTests` 做必要的增量回归。

### 15.3 专用文档

```text
docs/design/spectral-art/
├─ README.md
├─ implementation.md
├─ testing.md
├─ guide.md
├─ user-manual.md
├─ mathematical-principles.md
├─ recipe-schema.md
├─ report-schema.md
└─ history/
   ├─ README.md
   └─ g0-... 至 g9-...
```

本次只先创建本 `implementation.md` 计划。其余专用文档由对应实施包创建并填写真实内容；不得预先复制空模板或伪造测试证据。

## 16. 人工验收场景

### 16.1 文字

1. 选择一张纹理自然、尺寸在预算内的图片，输入短英文与数字；
2. 确认 Pattern 预览非空，主区域和 180° 副本清晰；
3. 从强度 0 缓慢提高，确认结果频谱中的文字逐渐可辨；
4. 确认正常图、放大差异、PSNR／SSIM 和 visibility 同步更新；
5. 更换字体或文字后，旧结果变 stale 且不能导出。

### 16.2 Logo

1. 导入透明背景 Logo，分别检查黑／白背景、二值／灰度和反相；
2. 切换 Contain／Stretch，确认比例和变形警告；
3. 把区域移动到不同合法频带，比较空间纹理和频谱可见性；
4. 拖入禁止区，确认保留最后合法区域并显示具体原因；
5. 导出 PNG，重新打开并确认尺寸、Alpha 和图案可见性。

### 16.3 二维码图片

1. 导入带 quiet zone 的二维码图片，默认使用二值最近邻；
2. 确认模块边缘未被灰度插值，中心共轭副本正确；
3. 检查频谱中 QR 外形可辨，同时显示“不保证扫码”的固定说明；
4. 尝试过小区域，确认最小 bin 和细节损失提示；
5. 报告不得声称二维码内容已恢复或验证成功。

### 16.4 生命周期与边界

1. 打开两个实例，选择不同载体和图案，确认完全隔离；
2. 快速拖动区域和强度，最终结果只对应最后 generation；
3. 运行中取消或关闭，确认没有迟到结果和 Bitmap 泄漏；
4. 选择 padded 尺寸超过 2048 的图片，确认在 FFT 大分配前阻断；
5. 导出失败后重试，确认内存结果仍可用且旧文件未被破坏；
6. 快照恢复后确认不自动读取图片、不自动执行 FFT。

### 16.5 Standalone 边界

Standalone 可以证明：

- Module、DI、View、编译绑定、命令和插件内部对象图可工作；
- 多 Document Scope 隔离；
- 图案输入、区域交互、取消、导出和 Bitmap 生命周期；
- 主要交互在本地 Avalonia 窗口可用。

Standalone 不能证明：

- 真实 Host Catalog、Dock、布局恢复和插件卸载；
- AssemblyLoadContext 与发布依赖闭包；
- 正式 ZIP、Windows CI、签名或目标用户设备性能；
- 所有自然图片上的主观隐蔽性或所有扫码软件的二维码识别率。

## 17. 风险与对策

| 风险 | 对策 |
| --- | --- |
| 图案明显但空间伪影也明显 | 固定显示质量、差异和能量；不自动美化结论；允许用户降低强度或换区域 |
| 不同频带强度感受悬殊 | 用径向 median/MAD 归一化，并用多类 Golden 校准 |
| 共轭错误产生复数残差 | 成对一次提交、自共轭禁止、`1E-8` 虚部门禁和随机映射测试 |
| 前后频谱各自拉伸造成虚假可见性 | 强制共享显示量程，并报告数值 visibility |
| 二维码看似存在却不可扫码 | 产品和报告固定声明只写外形；V1 不做扫码成功承诺 |
| 字体跨平台不同 | 嵌入消费固化 Pattern；数学测试注入确定性栅格器；记录 Pattern 指纹 |
| 大图内存峰值高 | 2048² 前置门禁、独占工作副本、流式诊断、及时释放和结构预算测试 |
| 错把功能当 DCT 水印 | 独立 ID、命名、Recipe／Report schema、UI 文案和架构扫描 |
| 为复用破坏 Periodic Noise Removal | 提取前补 Golden；无法无损共享时保留独立小服务 |
| 快速交互提交旧结果 | generation + 取消 + stale 指纹三重保护 |

## 18. 兼容、迁移与回滚

### 18.1 兼容规则

- 现有十七个 Document ID、快照 schema 和显示顺序不变；
- DCT-QIM 水印线格式、Profile、Frame、密码学和提取行为不变；
- Spectrum Inspector、Frequency Filter、Frequency Mask Editor、Periodic Noise Removal 的配方和数值协议不变；
- 新 Spectral Art ID 在 G7 首次登记后不得更改；
- recipe schema 1 发布后，保护带、幅度公式、相位规则和 Pattern 离散发生语义变化时必须升级版本；
- 本计划不授权新增 NuGet。若文字栅格需要依赖变化，必须先单独记录理由、许可、锁文件和插件依赖风险。

### 18.2 回滚顺序

若某阶段无法达到门禁：

1. 不登记或移除尚未稳定的 Module／Standalone 入口；
2. 移除 Document、View 和应用用例；
3. 只有通过独立测试且不改变既有消费者时，才保留共享数学提取；
4. 对 Periodic Noise Removal 的共享重构必须整体回滚或整体完成；
5. 不回退、改名或放宽任何现有水印／频域能力；
6. 文档如实记录失败阶段、原因和保留内容，不将部分完成写成 V1 封板。

### 18.3 数据迁移

当前没有 Spectral Art 快照或配方需要迁移。开发期 schema 变更可清除仅供开发的样本；首次发布后必须保留旧 schema 的
显式读取路径或给出结构化不兼容提示，不能静默按新公式解释旧配方。

## 19. 中文注释与实施纪律

- 所有新增生产代码注释使用中文；
- 核心类 XML `<remarks>` 解释设计目的、输入输出、坐标、所有权、线程和取消边界；
- 对数功率、径向尺度、共轭索引、相位保持、零幅值和虚部门禁必须写公式或推导意图；
- Pattern 栅格、区域闭开边界、ToEven、padding／crop 和 fixed-scale preview 必须解释“为什么”；
- 资源注释说明哪些数组同时存活、何处分配前阻断、何时释放；
- 不给简单属性、赋值和显而易见的空判断堆砌无价值注释；
- 注释与代码冲突时视为门禁失败，必须同步修改；
- 不使用继承层次、反射、服务定位器或多层 Strategy／Factory 炫技；
- 接口只用于真实外部边界或可替换算法，不为每个 sealed 服务建立 `I...`；
- 不吞掉除预期取消以外的异常，不把异常堆栈直接展示给用户；
- 每个 G 包先增加相关失败测试，再实现，再同步专用历史与测试文档；
- 不修改用户无关代码，不通过删除／跳过测试换取通过；
- 当前阶段不增加 Windows CI，不执行发布门禁。

## 20. V1 开发封板检查清单

### 产品与协议

- [ ] 独立 Spectral Art 产品名、稳定 ID、Recipe／Report schema 已冻结；
- [ ] UI、代码和报告未复用 DCT-QIM Payload 水印语义；
- [ ] 文字、Logo、二维码图片三种来源可形成有界 Pattern；
- [ ] Y 通道、区域、强度和中心共轭映射可解释；
- [ ] 正常图变化与频谱可见性同时诚实展示；
- [ ] 不宣称机器提取、扫码、隐写安全或鲁棒性。

### 数值与资源

- [ ] 对数功率、径向尺度、幅度公式和默认值有 Golden；
- [ ] DC／轴／Nyquist 和自共轭点不被修改；
- [ ] 相位、共轭和 `1E-8` 虚部门禁通过；
- [ ] 强度 0 严格无操作；源频谱／Pattern 不变；
- [ ] 2048²、Pattern、快照、报告和约 300 MiB 结构预算有前置门禁；
- [ ] 超预算不静默缩放或伪装完整尺寸。

### 架构与生命周期

- [ ] Domain、Application、Infrastructure、Features 依赖方向正确；
- [ ] Document／View 不含频域数学和像素循环；
- [ ] 设计模式只用于 Pattern 适配和文字端口等真实变化点；
- [ ] Session、工作副本、Result、Bitmap 和取消源所有权明确；
- [ ] 多 Scope、generation、取消、关闭、stale 和原子导出有测试；
- [ ] Module 为十八个 Persistable Document、零 Tool；
- [ ] 不使用 AIFLOW、Workflow Action 或 Workbench Command。

### 测试与文档

- [ ] 当前 629 个测试全部保持通过；
- [ ] 新增图案、映射、数值、重建、用例、文件、Document、UI 和架构测试；
- [ ] Debug／Release locked 本地门禁通过，0 失败、0 跳过、0 警告；
- [ ] 专用 README、指南、说明书、数学、测试、schema 和 G0–G9 历史齐全；
- [ ] 根索引、未来能力和共享边界已同步；
- [ ] 详细中文注释已经评审并与实现一致；
- [ ] 文档明确未执行 Windows CI、真实 Host、ZIP、签名和发布门禁。

只有上述项目以真实代码、自动测试、人工检查和文档证据全部完成后，才能把本文状态改为“V1 本地开发封板”。
