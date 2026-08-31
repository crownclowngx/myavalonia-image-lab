# ImageLabPlugin V1 SVD Decomposition／奇异值分解重建设计与实施计划

> 计划状态：V1 已实现并完成本地开发封板；本文同时保留原设计、实施顺序和边界<br>
> 基线日期：2026-08-31<br>
> 产品名称：SVD Decomposition／奇异值分解重建<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 起始自动证据：2026-08-31 实际复跑 locked restore；Debug/Release warn-as-error build 均为 0 警告、0 错误；两配置 test 均为 442/442 通过、0 失败、0 跳过<br>
> 核心路线：有界抗混叠分析代理 + 双精度单边 Jacobi SVD + 可复用分解结果 + Rank-k 重建 + 奇异值能量曲线 + 单分量有符号投影 + 单通道/RGB/YCbCr 策略比较<br>
> 首要规定：SOLID 是所有实现取舍的第一约束；设计模式只用于已经存在的变化点并保持朴素；新增生产代码使用详细中文注释解释数学约定、边界、所有权和设计思路；不使用 AIFLOW；不新增 Windows CI；本阶段不执行 ZIP、真实 Host 或任何发布门禁

本文规划并已交付 ImageLab 的第十三项产品能力、第十四个多实例 Persistable Document。它把图片通道看作有限二维实矩阵，
通过奇异值、累计能量、低秩重建和单个秩一分量，解释低秩近似与图像冗余。

它不是图片文件压缩器。V1 不计算或宣传文件压缩率，不生成自定义压缩格式，不把矩阵参数数量等同于真实文件大小，
也不承诺 Rank-k 图片一定更好看或更适合存储。

## 实施结果（2026-08-31）

- 已按 G0–G9 顺序完成 Domain、Application、Infrastructure、Document/View、Module/DI、Standalone、专用文档和本地门禁；
- `ImageAreaResampler` 已从既有代理策略中按 SRP 抽出，旧 512/1024/2048 白名单与行为保持不变；
- 新增 `Domain/SvdDecomposition`、`Application/SvdDecomposition`、严格报告 serializer 和第十四个 Persistable Document；
- 实施前 442 项测试，实施后 479 项；Debug/Release 均 479/479、0 失败、0 跳过，构建 0 警告/0 错误；
- 交付未使用 AIFLOW，未新增 Windows CI，未执行 ZIP、真实 Host、安装或发布门禁。

## 1. 决策摘要

### 1.1 产品形态

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `SVD Decomposition／奇异值分解重建` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID | `myavalonia.plugin.image.lab.document.svd-decomposition` |
| 显示名称 | `奇异值分解重建` |
| 显示分类 | `图像分析` |
| 输入 | 用户显式选择的一张 PNG/JPEG 图片 |
| 分析尺寸 | 最大边 128 或 256 的抗混叠代理；默认 128；小图不放大 |
| 数值核心 | double 单边 Jacobi SVD；宽矩阵通过转置归一为高矩阵后再交换左右奇异向量 |
| 交互核心 | 分解一次并缓存因子；Rank-k 或分量索引变化只做 O(kmn) 或 O(mn) 投影，不重新分解 |
| 通道策略 | 单通道、RGB 独立、YCbCr 独立；单通道可选 R/G/B/Y/Cb/Cr |
| 质量指标 | 矩阵 Frobenius 误差、相对误差、保留能量、图像 MAE/RMSE、PSNR-Y/RGB、全局 SSIM-Y |
| 分量可视化 | `σᵢuᵢvᵢᵀ` 的有符号发散色投影；显示归一化只服务观察，不冒充真实像素 |
| 导出 | 当前分析代理的 PNG、版本化 JSON/CSV 实验报告；不覆盖源文件 |
| 外部依赖 | V1 不新增 NuGet、原生线性代数库、GPU 或图表框架 |
| 模式使用 | 不可变值对象、普通 sealed 数值服务、窄用例、构造注入；不建立反射算法目录或通用 Pipeline |
| 明确排除 | AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP、真实 Host 与发布门禁 |

### 1.2 用户闭环

```text
显式选择一张 PNG/JPEG 图片
    ↓
建立最大边 128/256 的抗混叠分析代理，并显示“原图尺寸 ≠ 分析尺寸”事实
    ↓
选择单通道、RGB 独立或 YCbCr 独立策略
    ↓
异步执行一次有界 SVD，查看收敛、数值秩和各通道奇异值
    ↓
拖动 Rank k，联动查看重建图、累计能量、矩阵误差、PSNR 和 SSIM
    ↓
选择第 i 个秩一分量，查看有符号结构、σᵢ 和该分量能量占比
    ↓
按需运行三种策略的同图、同代理、同 k 有限比较
    ↓
导出明确标注为“分析代理”的 PNG，或导出 JSON/CSV 实验报告
```

### 1.3 固定实施顺序

1. G0 冻结产品语义、矩阵约定、算法、资源预算、Golden 和起始门禁；
2. G1 建立矩阵、配方、分解、能量、分量和诊断等不可变领域模型；
3. G2 完成单边 Jacobi SVD、转置适配、排序、符号规范和收敛诊断；
4. G3 完成 Rank-k 重建、理论残差、能量曲线和单分量有符号投影；
5. G4 完成单通道、RGB、YCbCr 三种朴素策略及公平比较；
6. G5 完成 Session、窄用例、缓存、取消、报告和 PNG 导出；
7. G6 接入第十四个 Persistable Document、快照、DI、Module 和 Standalone；
8. G7 完成曲线、图片、指标和分量联动 UI，以及 Headless View 门禁；
9. G8 同步专用文档、总索引、未来能力清单和人工验收记录；
10. G9 复跑 Debug/Release 全量本地门禁，完成本地开发封板。

不得先在 Document 或 View 中写一个“看起来能动”的矩阵近似，再补数学核心。矩阵协议、SVD 收敛、排序、
Rank-k 误差和颜色回写必须先在 Domain 中通过自动测试，UI 只显示结果并提交用户意图。

## 2. 当前项目事实与复用边界

### 2.1 已验证基线

仓库当前具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集，以及只供本地开发的 `ImageLabPlugin.Standalone`；
- 十二项已实现能力、十三个多实例 Persistable Document；当前没有 Tool、Workflow Action 或 Workbench Command；
- `PixelImage`、`ImageSize`、`ImageChannelPlane`、R/G/B/Y/Cb/Cr 六通道抽取与回写；
- `ImageAnalysisProxyProjector` 的面积加权抗混叠缩小、小图不放大语义；
- `FullReferenceQualityAnalyzer` 的 MAE、RMSE、PSNR-Y/RGB、全局 SSIM-Y 和 Alpha 独立统计；
- PNG/JPEG 解码、PNG 编码、原子文件写入、有限文本读取和文件对话框端口；
- Document Scope、generation 防迟到覆盖、取消、轻量快照、Bitmap 替换与释放惯例；
- 2026-08-31 实跑的 locked restore、Debug/Release 0 警告/0 错误、两配置 442/442 测试通过且 0 跳过。

442/442 只是 SVD 实施前的起始证据。每个实施包必须记录自己的新增测试和回归结果，不能把本段冒充完成功能的证据。

### 2.2 必须直接复用

- 图片解码、PNG 编码、原子发布和对话框继续使用现有端口，不在 SVD 目录复制文件 IO；
- 通道抽取与单通道回写继续使用 `ImageChannelConverter`；
- 图像质量继续使用 `FullReferenceQualityAnalyzer`，不另写一套 PSNR/SSIM；
- 代理缩放沿用现有面积加权算法；G0/G1 先抽出一个窄的共享 `ImageAreaResampler`，再由现有
  `ImageAnalysisProxyProjector` 和 SVD 专用代理策略共同调用；
- 差异视图优先复用现有差异投影基础；确需不同的有符号颜色语义时只新增 SVD 专用的窄投影器；
- Standalone 必须从真实 Module/DI 解析真实 Document 和 View，不复制一套演示实现；
- 无状态数值服务登记为 singleton；Session、分解因子、结果、Bitmap 和取消源归各 Document Scope 独占。

### 2.3 允许的共享改进

现有 `ImageAnalysisProxyProjector.Create` 将 512/1024/2048 档位校验和面积加权缩放放在同一类型中。SVD 的运算复杂度
要求 128/256 档位，但不能为了复用而把现有频域工具的产品选项改成 128/256。

允许做一次保持旧行为的 SRP 拆分：

```text
ImageAreaResampler
  只负责“按给定最大边做面积覆盖加权缩小，小图克隆”

ImageAnalysisProxyProjector
  保留 512/1024/2048 的既有白名单并委托 ImageAreaResampler

SvdAnalysisProxyPolicy / PrepareSvdSessionUseCase
  只接受 128/256，并委托同一个 ImageAreaResampler
```

旧公共行为、旧错误语义和旧测试必须保持。不能给所有现有工具悄悄增加更小档位，也不能复制面积积分循环。

### 2.4 禁止的错误复用

- 新 Document 不持有或调用 Wavelet、Spectrum、Frequency Filter 等其他 Document；
- 不把 `Dct8x8Transform`、FFT 或小波系数假装成 SVD；
- 不在 Feature 中实现 Jacobi 旋转、矩阵乘法、能量累计或颜色空间循环；
- 不把 Avalonia `Bitmap`、`Point`、Brush、Dispatcher、文件路径放入 Domain 模型；
- 不修改既有稳定 ID、快照 schema、现有工具的代理默认值或输出语义；
- 不创建万能 `MatrixService`、通用线性代数框架、Mediator、Event Bus、DAG 或运行时算法发现；
- 不为只有一个实现的 Jacobi 分解器提前建立 Strategy 集合或抽象工厂。

## 3. V1 范围与明确非目标

### 3.1 V1 必须完成

- 最大边 128/256 的抗混叠分析代理，默认 128，小图不放大；
- 单通道模式：R、G、B、Y、Cb、Cr；
- RGB 独立模式：R/G/B 三个矩阵分别分解，并以共同 Rank k 重建；
- YCbCr 独立模式：Y/Cb/Cr 三个矩阵分别分解，并以共同 Rank k 重建；
- 双精度单边 Jacobi SVD，支持高矩阵、宽矩阵、方阵、1×N、N×1 和秩亏矩阵；
- 奇异值降序、累计能量曲线、数值秩、条件提示、收敛状态和实际 sweep 数；
- k 从 0 到 `min(width,height)`；k=0 是明确的基线，不把它改写成 k=1；
- 当前通道/策略的 Rank-k 重建、理论 Frobenius 残差、实际 raw 残差和量化后的图片质量；
- 第 i 个 `σᵢuᵢvᵢᵀ` 分量的有符号可视化、显示比例、极值和能量占比；
- 同一源图、同一代理、同一 k 下的有限策略比较；
- 分解期间取消、参数变化后旧结果失效、generation 防迟到、多实例隔离和释放后阻断；
- 当前分析代理 PNG、JSON/CSV 报告、轻量快照、中文错误与限制说明；
- Debug/Release locked restore、warn-as-error build、全部自动测试、0 跳过和文档同步。

### 3.2 V1 明确不实现

- 图片文件压缩、压缩文件格式、编码器、解码器、码率、文件体积预测或“节省百分比”；
- 把 `k(m+n+1)` 与 `mn` 的参数数目比值命名为文件压缩率；
- JPEG/WebP/AVIF 输出、覆盖源文件、批量目录处理或无界图片队列；
- 原尺寸大图 SVD、分块 SVD、随机 SVD、截断 Lanczos、GPU、SIMD 或原生 BLAS/LAPACK；
- 自动选择“最佳 k”、主观画质评分、语义保真结论或“无损压缩”文案；
- 联合彩色张量分解、四元数 SVD、PCA 色彩旋转、非负矩阵分解或深度学习；
- 用户编辑 U/Σ/V、任意矩阵导入、通用线性代数工作台；
- 为每个秩一分量常驻一张全尺寸图片；分量必须按需投影；
- AIFLOW、工作流节点、Workflow Action、Workbench Command、脚本或宏；
- Windows CI、真实 Host、ZIP、安装/升级/卸载和任何发布门禁。

### 3.3 教学结论边界

- 奇异值下降快只说明当前矩阵在当前颜色策略和当前代理尺度下容易被低秩近似，不等于原始文件一定容易压缩；
- 保留能量高只描述 Frobenius 范数意义，不等价于人眼主观质量或语义正确；
- PSNR/SSIM 只比较当前代理重建与当前代理源图，不代表原尺寸图片指标；
- RGB 与 YCbCr 的结果受颜色空间、通道中心、舍入和裁切影响，不能据单张图宣布某策略普遍更优；
- 奇异向量在重复奇异值子空间内不唯一；此时不同正确实现可能显示不同单分量，但相应子空间重建相同；
- 单分量投影经过独立显示归一化，颜色只表达正负和相对幅度，不能按普通图片像素解释；
- k 达到完整代数秩时，double 矩阵重建应接近原矩阵；转成 8 位 RGB 后还会经历颜色转换、舍入和裁切。

## 4. 数学协议

### 4.1 矩阵坐标

对宽 `W`、高 `H` 的通道平面，固定：

```text
A ∈ R^(H×W)
A[y, x] = channel(x, y) - neutral(channel)
```

- 行对应 Y/图片高度，列对应 X/图片宽度；不得在不同类型中交换；
- R/G/B/Y 的 `neutral = 0`；Cb/Cr 的 `neutral = 128`；
- Chroma 中心化避免恒定 128 偏置独占一个大奇异值；重建时必须加回同一 neutral；
- 不做均值移除、直方图归一化、标准差归一化或隐式对比度拉伸；
- double 计算阶段不裁切，只有图片投影阶段按项目统一规则舍入并裁到 `[0,255]`；
- Alpha 不进入 SVD，重建图逐像素保留代理源图 Alpha。

### 4.2 SVD 定义

对 `A ∈ R^(m×n)`，令 `r = min(m,n)`：

```text
A = U Σ Vᵀ
U ∈ R^(m×r)
Σ = diag(σ₁, …, σᵣ)
V ∈ R^(n×r)
σ₁ ≥ σ₂ ≥ … ≥ σᵣ ≥ 0
```

经济型因子只保存 r 列，不构造完整 `m×m` 或 `n×n` 正交矩阵。结果必须同时保存原矩阵尺寸、通道、neutral、
收敛诊断和不可变因子所有权，禁止仅用三个裸数组在层间传递。

### 4.3 Rank-k 与单分量

```text
Aₖ = Σ(i=1..k) σᵢ uᵢ vᵢᵀ
Cᵢ = σᵢ uᵢ vᵢᵀ
```

- `k=0` 时 `A₀` 为零矩阵；通道回写时只加回 neutral；
- `k` 大于 r 必须在领域边界拒绝，不在 UI 静默裁切；
- `Cᵢ` 的索引在领域内用 0-based，在 UI 中显示为第 `i+1` 项；
- Rank-k 重建直接从只读因子计算，不改变分解结果；
- 不缓存 r 张 `Cᵢ` 图片；选择变化时只生成当前一个分量；
- repeated/near-equal singular values 的分量方向不作稳定身份承诺，报告必须保留该限制。

### 4.4 能量与理论误差

```text
总能量             E = Σ σᵢ² = ||A||²_F
Rank-k 保留能量率  R(k) = Σ(i≤k) σᵢ² / E
最优残差平方       ||A-Aₖ||²_F = Σ(i>k) σᵢ²
相对 Frobenius 误差 = ||A-Aₖ||_F / ||A||_F
```

- 累计能量使用补偿求和，避免长序列小项被吞掉；
- 全零矩阵的总能量为 0，能量比例标记为 `NotApplicable`，不能产生 NaN；
- 理论尾能量与直接逐元素残差都要计算；二者超出冻结容差时视为数值失败；
- 能量曲线同时提供 `σᵢ/σ₁` 和累计 `R(k)`，但 UI 默认突出累计能量；
- σ 的显示可使用对数纵轴，原始报告仍保存未变换的 double 值。

### 4.5 数值秩

数值秩只作为诊断：

```text
tolerance = max(m,n) × machineEpsilon × σ₁ × rankToleranceFactor
numericRank = count(σᵢ > tolerance)
```

`rankToleranceFactor` 在 G0 冻结为内部常量，不开放成随意 UI 参数。界面同时显示代数上限 r 与数值秩，不能用数值秩
限制用户观察尾部奇异值；当尾部接近误差地板时显示“数值不稳定/分量方向可能不唯一”。

## 5. 单边 Jacobi SVD 数值路线

### 5.1 选择理由

V1 使用双精度单边 Jacobi，而不通过 `AᵀA` 做特征分解：

- `AᵀA` 会把条件数平方，弱小奇异值更容易损失；
- 单边 Jacobi 直接正交化列，便于同时获得奇异值和左右奇异向量；
- 128/256 有界代理下，确定性全分解的时间和内存可控；
- 算法可以完全托管、可取消、可用小矩阵 Golden 和性质测试封板；
- 不需要为 V1 引入新 NuGet、原生库或平台特定部署风险。

这不是建立通用数值库。实现只服务本能力冻结的 dense double 矩阵、尺寸预算和诊断合同。

### 5.2 高/宽矩阵适配

- 当 `m >= n`，直接对 A 的列执行 Jacobi 正交化；
- 当 `m < n`，分解 `Aᵀ`，结束后交换 U/V 并恢复原始维度；
- 1×N 与 N×1 走同一协议，不写特判式“伪 SVD”；
- 转置创建一次连续缓冲，必须计入峰值内存预算；
- 结果总按原图 H×W 语义暴露，调用方不知道内部是否转置。

### 5.3 sweep、旋转与收敛

每个 sweep 以固定字典序枚举列对 `(p,q)`，计算：

```text
α = bₚᵀbₚ
β = bqᵀbq
γ = bₚᵀbq
```

当 `|γ|` 大于相对正交阈值时，使用稳定 Jacobi 公式求 `(c,s)`，同时旋转工作矩阵 B 和右奇异向量 V 的对应列。

实现必须满足：

- 点积使用补偿求和或经测试的缩放方案，所有中间值检查有限性；
- 不直接使用易溢出的 `tan(2θ)` 公式；
- 零列、极小列和相等列范数有显式分支，不能产生除零或 NaN；
- 每轮至少按行或按固定工作块检查取消，不让取消等待完整最大 sweep；
- 同一输入、同一运行时必须得到确定的奇异值顺序和诊断；
- 达到最大 sweep 仍不收敛时返回结构化 `NotConverged`，不把近似结果伪装成成功；
- 最大 sweep、相对正交阈值和有限值上限在 G0 通过 Golden/压力样本冻结。

### 5.4 归一化、排序与符号规范

收敛后 `σᵢ = ||bᵢ||₂`，非零列归一化得到 `uᵢ`。随后：

- 按 σ 降序稳定排序 U、σ、V 的对应列；
- 相等或近相等 σ 只保证稳定原列次序，不伪造数学唯一性；
- 每一对奇异向量使用确定性符号规范：找到绝对值最大的 U 元素，若其为负则同时翻转 uᵢ、vᵢ；
- 若 σ 为数值零，不要求该 U 列具有可解释方向；重建必须因 σ=0 而忽略它；
- 对数值非零的 U 列检查 `UᵀU`，对 V 的完整经济型列检查 `VᵀV`，并检查最大偏差、相对重建残差和奇异值单调性；
- 数值零奇异值对应的 U 方向本来就不唯一且可能无法从零列归一化，诊断与测试不得错误要求这些 U 列构成唯一正交基；
- 诊断超限即失败，不能只在 Debug `Assert` 中记录。

### 5.5 独立验证策略

自动测试不依赖生产算法本身生成期望值：

- 2×2 对角矩阵、单位矩阵、零矩阵和已知外积使用手算 Golden；
- 用固定正交矩阵与指定奇异值构造 `A=UΣVᵀ`，期望奇异值来自构造输入；
- 宽/高互为转置，验证奇异值一致与重建一致；
- 与独立工具生成的少量 Golden 只以固定数值资产提交，测试运行时不引入 Python/NumPy；
- 重复奇异值只断言奇异值、正交性、重建和子空间投影，不断言单列向量逐元素相等。

## 6. 颜色策略与公平比较

### 6.1 单通道策略

用户选择 R/G/B/Y/Cb/Cr 之一：

- 只分解该通道矩阵；
- Rank-k 后只回写该通道，其他颜色分量来自代理源图；
- Alpha 逐字节保持；
- Y/Cb/Cr 回写继续使用项目既有 YCbCr 公式；
- 报告同时给出矩阵域误差和最终 RGB 图片指标，避免把二者混为一谈。

### 6.2 RGB 独立策略

- 分别分解 R、G、B；
- 三个矩阵使用同一个 k，但各自按自身 σ 顺序保留前 k 项；
- 当 k 超过某通道数值秩时仍允许选择，只是新增项可能处于数值噪声地板；
- 聚合能量率以三个矩阵尾能量之和计算，不平均三个百分比；
- 最终 RGB 直接组合，Alpha 保持，不重复调用三次“只替换单通道”造成累积舍入。

### 6.3 YCbCr 独立策略

- 分别分解 Y、中心化 Cb、中心化 Cr；
- 三通道使用共同 k，重建后一次性转换回 RGB；
- Cb/Cr 的 128 neutral 必须只减一次、加一次；
- raw YCbCr、转换后 RGB 越界和裁切数量都进入诊断；
- 不能把 YCbCr 的高能量集中描述成视觉上必然更优。

### 6.4 策略比较规则

有限比较固定包含：

1. Y 单通道；
2. RGB 独立；
3. YCbCr 独立。

比较必须满足：

- 同一源图、同一个不可变代理实例、同一个 k、同一个舍入规则；
- 分解按固定顺序串行执行，不同时启动 7 个 CPU 密集任务；
- Session 内可按 `strategy/channel/proxyFingerprint` 缓存已完成分解；
- 比较取消后保留已完成案例并标记 `CancelledPartial`，不伪造完整排行；
- 最多生成固定三行，不提供无界策略×k 网格；
- 默认按策略固定顺序展示，不用单一 PSNR 自动宣布“最佳”；
- 报告展示矩阵数量、共同 k、保留能量、耗时和质量，但不展示“压缩比”。

## 7. SOLID 架构

### 7.1 依赖方向

```text
Features/SvdDecomposition
  SvdDecompositionDocument       每实例状态、命令、generation、取消和 Bitmap
  SvdDecompositionView           编译绑定、布局和可见状态
  SingularValueCurveControl      只绘制奇异值/累计能量并提交选中索引
                    │
                    ▼
Application/SvdDecomposition
  PrepareSvdSessionUseCase       解码、128/256 代理和不可变 Session
  DecomposeSvdUseCase            按策略抽取矩阵并协调有界分解/缓存
  ReconstructSvdRankUseCase      Rank-k、通道合成、质量和诊断
  ProjectSvdComponentUseCase     当前秩一分量有符号投影
  CompareSvdStrategiesUseCase    三种固定策略的公平有限比较
  ExportSvdImage/ReportUseCase   stale 防护、PNG 与 JSON/CSV 原子导出
                    │
                    ▼
Domain/SvdDecomposition          Domain/Imaging + Domain/Comparison
  DenseMatrix、SvdFactors、JacobiSvdDecomposer、EnergyAnalyzer
  LowRankReconstructor、ComponentProjector、ColorStrategyReconstructor
                    ▲
                    │
Infrastructure
  既有图片编解码、文件对话框、原子写入 + SVD 报告 serializer
```

Domain 不引用 Avalonia、文件系统、JSON、DI、Host SDK 或 Application；Application 不引用 Avalonia、View、Document 或
Infrastructure 具体类型；Feature 不拥有 SVD 数学循环；Infrastructure 只实现明确的副作用端口。

### 7.2 单一职责

| 类型 | 唯一职责 | 明确不负责 |
| --- | --- | --- |
| `DenseMatrix` | 保存尺寸和连续只读 double 值 | SVD、图片、文件、UI |
| `JacobiSvdDecomposer` | 对一个有界矩阵计算经济型 SVD 与收敛诊断 | 颜色策略、Rank slider、Bitmap |
| `SingularValueEnergyAnalyzer` | 从 σ 生成能量曲线、数值秩和尾能量 | 修改因子、选择 k |
| `LowRankReconstructor` | 从只读因子生成指定 Rank-k 矩阵 | 通道转换、质量结论、缓存策略 |
| `SvdComponentProjector` | 把一个秩一矩阵映射为有符号观察图 | 把显示归一化写回重建 |
| `SvdColorStrategyExecutor` | 抽取/组合固定颜色策略所需矩阵 | 实现 Jacobi、文件 IO、UI 状态 |
| `SvdReconstructionAnalyzer` | 计算理论/直接矩阵误差与图片质量 | 自动决定最佳 k |
| `SvdSession` | 独占源图、代理、分解缓存和当前结果 | 跨 Scope 缓存、持有 Avalonia Bitmap |
| `SvdDecompositionDocument` | 管理实例状态、命令、取消、快照和显示适配 | 矩阵循环、算法收敛、JSON 写入 |
| `SingularValueCurveControl` | 绘制曲线、命中测试和键盘选择 | 直接修改 Session 或触发文件 IO |

任何同时出现 Avalonia 指针事件、Jacobi 列旋转和文件写入的类型都违反 SRP，不能合入。

### 7.3 开闭原则与朴素模式

V1 只使用：

- 不可变值对象表达配方、矩阵、因子、能量、重建、分量和报告；
- 一个完整 `switch` 处理三种固定颜色策略；
- 窄应用用例和构造注入隔离副作用与实例状态；
- 普通字典作为 Session 内有限分解缓存；
- generation + `CancellationTokenSource` 实现 latest-wins，不引入消息总线。

颜色策略数量固定、没有第三方扩展和独立生命周期，因此 V1 不为每种策略建立接口、工厂和反射注册。
只有出现第二种真实 SVD 算法且需要独立测试/替换时，才考虑 `ISvdDecomposer`；当前不要为假想扩展制造层级。

### 7.4 依赖倒置与接口隔离

接口只放在确有副作用或 Document 需要替身的 Application 边界：

```csharp
internal interface IPrepareSvdSessionUseCase { /* 解码和代理 */ }
internal interface IDecomposeSvdUseCase { /* 策略分解和缓存 */ }
internal interface IReconstructSvdRankUseCase { /* Rank-k 与质量 */ }
internal interface IProjectSvdComponentUseCase { /* 单分量投影 */ }
internal interface ICompareSvdStrategiesUseCase { /* 固定三策略比较 */ }
internal interface IExportSvdImageUseCase { /* 当前代理 PNG */ }
internal interface IExportSvdReportUseCase { /* 版本化 JSON/CSV */ }
```

不建立包含 Prepare/Decompose/Reconstruct/Compare/Export 的 `ISvdService`。纯领域类直接构造注入，无需“一类一接口”。

## 8. 领域模型、所有权与不变量

### 8.1 建议模型

```text
SvdColorStrategy
  SingleChannel | IndependentRgb | IndependentYCbCr

SvdRecipe
  strategy, singleChannel, analysisMaximumEdge, rank

DenseMatrix
  rows, columns, ReadOnlyMemory<double>

SvdFactors
  rows, columns, rankLimit, U, singularValues, V, diagnostics

SingularValueEnergyReport
  totalEnergy, samples[], numericRank, energyStatus

SvdDecompositionSet
  strategy, channelFactors[], proxyFingerprint, elapsed

SvdRankResult
  recipeFingerprint, reconstructedMatrices, image, matrixDiagnostics, quality, clipping

SvdComponentProjection
  channel, componentIndex, singularValue, energyShare, min, max, scale, preview

SvdStrategyComparison
  commonRank, cases[], completionStatus
```

### 8.2 数组所有权

- `DenseMatrix` 构造时复制外部输入，或只接管由同一程序集内部 builder 明确移交的缓冲；
- `SvdFactors` 接管 U/σ/V 后不再暴露可写数组；
- `ReadOnlyMemory<double>` 只提供读取，不通过 `MemoryMarshal` 泄露可写引用；
- Rank-k 结果拥有新矩阵缓冲，不能覆写 factors；
- 三策略比较可共享只读 factors，不能共享可写重建缓冲；
- Session 释放时清空大对象引用和缓存；已排队任务即使完成也不得提交到释放后的 Document；
- Snapshot 只保存路径、策略、通道、代理档位、k、分量索引和 schema，不保存 U/σ/V 或图片字节。

### 8.3 构造门禁

- 行列必须为正并满足 G0 预算；`rows×columns` 使用 checked；
- 所有输入矩阵值、奇异值和因子必须有限；
- U/σ/V 尺寸必须与经济型 SVD 协议一致；
- σ 非负且按冻结容差单调不增；
- k 位于 `[0,r]`，分量索引位于 `[0,r-1]`；
- 颜色策略与通道集合必须完整且不重复；
- recipe 指纹包含 proxy、strategy、channel、rank、数值协议版本；
- 任一诊断失败都返回结构化错误，不在 UI 通过空图片表示。

## 9. Application、状态机与缓存

### 9.1 Session 建立

`PrepareSvdSessionUseCase`：

1. 校验路径、128/256 档位和取消；
2. 通过现有 `IImageCodec` 解码一次完整源图；
3. 通过共享面积缩放建立分析代理；
4. 计算源路径无关的代理内容指纹；
5. 返回拥有源图、代理和空缓存的 `SvdSession`；
6. 不在载图时自动执行 SVD，避免用户尚未选择策略就占用 CPU。

### 9.2 分解缓存

缓存键固定为：

```text
proxyFingerprint + strategy + singleChannel + numericProtocolVersion
```

- k 和分量索引不进入键，因为它们不需要重新分解；
- 切换代理档位或源图必须新建 Session 并释放旧缓存；
- RGB 与 YCbCr 的通道 factors 可按通道复用，但缓存实现保持显式，不建立通用计算图；
- 同一键同时只允许一个运行中任务；V1 可以由 Document 串行化，不需要复杂异步 memoizer；
- 失败、取消和非收敛结果不进入成功缓存；
- 缓存条目数量由固定策略集合自然限制，不实现 LRU。

### 9.3 Rank-k 实时语义

“实时”指 SVD 成功后，k 改变不重新分解，并在有界代理上快速重建；不承诺原尺寸或分解本身实时。

- Slider 连续变化由 Document 做 80–120 ms debounce；具体值在 G7 人工体验后冻结；
- 每次请求推进 generation，并取消旧 Rank 投影；
- UI 可立即更新 k 文本和曲线标记，图片/指标在当前 generation 完成后一次提交；
- 不在每个 slider tick 创建无界后台任务；
- k 增减均从只读 factors 计算，V1 不维护复杂的可变增量累加器；
- 若 256 档位的当前策略无法保持可用交互，优先优化循环和分配，不牺牲数值正确性或扩大并行度。

### 9.4 状态转换

```text
Empty
  └─ Load → Ready
Ready
  └─ Decompose → Decomposing → Decomposed | Failed | Cancelled
Decomposed
  ├─ RankChanged → Reconstructing → CurrentResult
  ├─ ComponentChanged → Projecting → CurrentComponent
  ├─ Compare → Comparing → ComparisonReady | CancelledPartial
  └─ Strategy/ChannelChanged → Ready 或命中缓存后 Decomposed
任意状态
  └─ Source/ProxyChanged → 取消旧任务、释放旧 Session、Ready/Empty
```

导出只接受 `CurrentResult.recipeFingerprint == CurrentRecipeFingerprint`。stale、失败、取消、仅有 factors 或旧 k 的图片一律阻断。

### 9.5 取消与错误

- 分解按 sweep、列对工作块和矩阵行检查取消；
- Rank-k、分量投影、通道合成、质量统计和报告序列化都传播同一 token；
- 新操作、关闭 Document、重新载图和更换代理都会取消旧操作；
- `OperationCanceledException` 映射成“已取消”，不显示成算法失败；
- 非有限输入、预算超限、未收敛、正交性失败、报告写入失败分别使用不同中文消息；
- 错误消息提供用户下一步，例如切换到 128、重新载图或保留报告，不建议用户关闭安全门禁。

## 10. UI 与交互设计

### 10.1 建议布局

```text
┌ 顶部命令：载入 | 代理 128/256 | 策略 | 通道 | 开始分解 | 取消 | 导出 ┐
├ 左：原分析代理 ───────────────┬ 中：Rank-k 重建 ───────────────┤
│ 原图/代理尺寸、Alpha 说明       │ k、stale/处理中、裁切和代理标记    │
├ 奇异值与累计能量曲线 ──────────┼ 右：当前秩一分量有符号投影 ──────┤
│ σ、累计能量、k 竖线、键盘选择    │ i、σᵢ、能量占比、min/max、色标     │
├ Rank slider + 精确数字输入 ────────────────────────────────────┤
├ 指标：理论/直接残差 | 能量 | MAE/RMSE | PSNR | SSIM | 收敛诊断 ┤
├ 策略比较表：Y / RGB / YCbCr，固定三行 ─────────────────────────┤
└ 状态与教学边界：分析代理、非压缩器、分量归一化、结果可用性 ─────┘
```

### 10.2 曲线控件

可增加一个窄 `SingularValueCurveControl`，不引入第三方图表包：

- 输入只读奇异值、累计能量、当前 k 和当前分量索引；
- 绘制 σ 相对值/对数曲线和累计能量曲线，双轴必须有清晰图例；
- 支持鼠标点击、左右键、Home/End 选择 k 或分量；
- 空矩阵能量、单奇异值、全零矩阵和窄尺寸不崩溃；
- 高对比主题不只依赖红绿区分；
- 控件只抛出索引意图，不直接调用 UseCase 或修改 Session；
- Headless 测试验证尺寸、属性、键盘和命中边界，不做脆弱像素截图断言。

### 10.3 分量色标

- 负值使用一侧颜色，0 使用中性色，正值使用另一侧颜色；
- 以 `max(abs(min),abs(max))` 对称缩放，0 始终位于色标中心；
- 全零分量显示统一中性色并标记“该分量在数值容差内为零”；
- 显示 `displayScale` 和 raw min/max；
- 禁止对每个像素先裁 `[0,255]` 再做分量图，这会丢失负号；
- 分量 PNG 如在后续版本开放，必须连同色标元数据导出；V1 只在报告保存其数值摘要。

### 10.4 可访问性与可解释性

- 所有图片提供文字标题和尺寸，所有指标有单位；
- `∞` PSNR 显示为“∞（像素误差为 0）”，JSON 不写非有限数字；
- Slider 配套精确数字输入、当前值文本和键盘操作；
- 分解运行时禁用冲突命令，但取消始终可用；
- 代理结果永久显示“分析代理”徽标，不能只在首次载图时提示；
- 页面固定显示“本工具解释低秩近似，不是图片文件压缩器”。

## 11. 资源、性能与并发预算

### 11.1 尺寸边界

- V1 只允许最大边 128/256，默认 128；
- 代理 `rows×columns` 必须 checked 且不超过 65,536 样本；
- `r=min(rows,columns)` 最大 256；
- 单次最多分解三个通道；策略比较串行并复用缓存；
- 不对原图执行 SVD；即使原图小于其他工具的 512 档，也必须通过 SVD 自己的 256 上限；
- 任何将上限提高到 512/1024 的修改都需要新的复杂度、内存、取消和人工延迟评审，不能只改常量。

### 11.2 内存预算

G0 需要把下列峰值按 checked 公式写入 `SvdResourceEstimate`：

```text
输入/工作矩阵          O(mn)
经济型 U               O(mr)
经济型 V               O(nr)
Rank-k 输出            O(mn)
颜色策略               × 1 或 × 3
转置适配临时缓冲        最多额外 O(mn)
RGBA 代理/重建/分量图   各 4mn bytes
```

- 在分配前估算，不通过捕获 `OutOfMemoryException` 实现预算；
- 分解器应复用工作数组，避免每个列对分配；
- dot/rotation 循环内禁止 LINQ、闭包和临时数组；
- 不常驻每个 k 或每个分量的矩阵/Bitmap；
- 新 Bitmap 替换成功后立即释放旧 Bitmap；
- 两个 Document Scope 的缓存和取消完全隔离。

### 11.3 性能门禁

- 自动测试不使用脆弱的绝对毫秒阈值；
- 用分配计数、案例上限、任务数量和“Rank 改变不调用 Decompose”证明结构性性能；
- 128/256 的 Debug/Release 实际耗时记录在 testing 文档，作为观察值而非 SLA；
- G7 人工验证拖动 k 不造成无界排队和长期 UI 冻结；
- 分解在后台任务执行，但数值核心内部不自行 `Task.Run`、不并行列对；
- 并行化会改变归约顺序和资源占用，V1 明确不实现。

## 12. 快照、报告与导出

### 12.1 Document 快照

schema 1 只保存：

- source path；
- analysis maximum edge；
- color strategy 和单通道选择；
- rank k、当前分量索引；
- 曲线显示模式和 UI 分栏等轻量偏好；
- `numericProtocol = "one-sided-jacobi-v1"`。

不保存图片、矩阵、U/σ/V、Bitmap、比较结果或报告正文。恢复后显示参数但要求重新载图/分解；未知 schema 安全回退，
路径不存在时保留可解释状态，不在反序列化过程中自动读取文件。

### 12.2 JSON 报告

建议 `image-lab.svd-report` schema 1，至少包含：

- 产品、schema、数值协议和生成时间；
- 原图/代理尺寸、代理档位、策略、通道、neutral、k；
- 每个矩阵的尺寸、全部奇异值、总能量、累计能量、数值秩；
- sweep、是否收敛、正交误差、重建残差和耗时；
- Rank-k 理论/直接 Frobenius 误差、保留能量和 raw min/max；
- 图片 MAE/RMSE、PSNR-Y/RGB、SSIM-Y、Alpha 与裁切诊断；
- 当前分量索引、σ、能量占比、raw min/max 和显示比例；
- 三策略比较案例与完成/取消状态；
- “分析代理”“不是文件压缩率”“重复奇异值分量不唯一”等解释字段。

所有非有限值使用结构化状态表达。例如 exact reconstruction 的 PSNR 使用 `isExact=true, psnrDb=null`，不能输出非法 JSON
`Infinity`、`NaN` 或字符串冒充数字。

### 12.3 CSV 报告

CSV 采用稳定列顺序，至少提供：

- `recordType=singular-value`：strategy、channel、index、sigma、relativeSigma、energyShare、cumulativeEnergy；
- `recordType=rank-result`：strategy、rank、retainedEnergy、frobeniusError、relativeError、PSNR、SSIM；
- `recordType=strategy-case`：共同 k 下三种策略的聚合结果；
- `recordType=diagnostics`：sweeps、converged、orthogonality、elapsed。

字段使用 invariant culture；文本严格 CSV 转义；换行固定；同输入与同结果应产生除时间字段外的确定顺序。

### 12.4 PNG 导出

- 只导出当前 recipe 指纹一致的 Rank-k 分析代理；
- 只允许 PNG，不提供 JPEG；
- 文件对话框明确命名“导出分析代理重建 PNG”；
- 不允许覆盖源路径；
- 导出前再次检查当前 Session、策略、k、proxy fingerprint 和结果状态；
- 写入走 `IImageCodec` + `IAtomicFileWriter`，失败不留下半文件；
- 导出成功提示同时写出代理尺寸，不能称为“压缩图片”。

## 13. 中文注释与设计说明规范

新增生产代码必须使用详细中文注释，重点解释“为什么”和不易从语法看出的约定，不对每行机械复述。

必须有详细中文说明的位置：

- `JacobiSvdDecomposer`：为何不走 `AᵀA`、旋转公式、稳定分支、收敛和取消粒度；
- 宽矩阵转置适配：U/V 交换、尺寸恢复和所有权；
- 排序与符号规范：为何向量符号不唯一、重复奇异值为何不能逐列比较；
- Chroma neutral：为何 Cb/Cr 减 128，以及何时加回；
- 理论尾能量与直接残差：二者分别证明什么；
- 分量发散色投影：显示归一化为何不能进入重建；
- Session 缓存：为什么 k 不进入缓存键，何时失效；
- generation/取消：如何防止旧结果覆盖新参数；
- 资源估算：数组数量、checked 乘法和限制来源；
- PNG/报告导出：为何只允许当前代理结果、为何不是压缩器。

建议类级注释包含“职责、输入输出、不变量、所有权、线程/取消、明确不负责”；关键公式旁给出变量含义和行列方向。
不得留下“优化一下”“处理矩阵”“做 SVD”这类无法指导维护的空泛注释。

## 14. 单元测试与本地门禁

### 14.1 G0/G1 模型与矩阵协议

- H×W 行列映射、索引、转置往返和 checked 溢出；
- R/G/B/Y neutral=0，Cb/Cr neutral=128；
- 非有限样本、空尺寸、错误数组长度、错误 factors 尺寸拒绝；
- k=0、k=r、k<0、k>r 和分量索引边界；
- 数组防御复制、只读暴露和 Session 释放后阻断；
- 资源估算对 1×1、128²、256²、单/三通道和转置临时缓冲正确；
- 现有 512/1024/2048 代理行为在抽取 resampler 后逐字节回归。

### 14.2 G2 SVD Golden

- 1×1、1×N、N×1、2×2 对角、单位、零、常量和 rank-1 外积；
- 已知 U/Σ/V 构造的 3×2、2×3、5×3、3×5 矩阵；
- 奇异值降序、非负、稳定符号和宽矩阵 U/V 尺寸；
- `A` 与 `Aᵀ` 奇异值一致；
- 数值非零 U 列满足 `UᵀU≈I`，V 列满足 `VᵀV≈I`，且 `UΣVᵀ≈A`；
- rank deficient、重复奇异值、近零列、极大/极小有限比例；
- 固定输入确定性、取消、最大 sweep 和结构化非收敛；
- 任何输出 NaN/Infinity 立即失败。

### 14.3 G3 重建、能量与分量

- k=0、1、中间值、r 的 Rank-k Golden；
- 误差随 k 单调不增，累计能量单调不减；
- `Σσ²≈||A||²F`，理论尾能量≈直接残差平方；
- Eckart–Young 性质使用受控小矩阵验证，不用生产重建生成期望；
- 全零矩阵能量为 NotApplicable，不产生 NaN；
- 单分量总和恢复 Rank-k，分量 Frobenius 能量≈σ²；
- 分量正负色标对称、零居中、全零分量安全；
- 选择分量不重新调用 Decompose，不常驻所有分量图片。

### 14.4 G4 颜色策略

- 单 R/G/B 只改变目标通道语义，Alpha 逐字节不变；
- Y/Cb/Cr neutral 只减/加一次；
- RGB 三通道一次组合，不因调用顺序产生不同结果；
- YCbCr 转换、AwayFromZero 舍入、raw 越界和裁切计数；
- full-rank 矩阵重建误差与 8 位图片量化误差分别断言；
- 三策略共用源代理、共同 k、固定顺序和聚合能量公式；
- 策略比较取消返回有序部分结果，不产生“最佳策略”字段；
- PSNR/SSIM 直接复用 shared analyzer，并通过 recording double 证明调用边界。

### 14.5 G5 Application 与导出

- 解码一次、代理一次、载图不自动分解；
- 缓存键包含 proxy/strategy/channel/protocol，不包含 k；
- 同策略再次分解命中缓存；切换源图/代理使缓存失效；
- RGB/YCbCr 固定串行，不并发启动通道分解；
- generation 防迟到、取消、失败不污染成功缓存；
- stale、旧 k、仅 factors、旧 proxy 结果禁止 PNG/报告导出；
- PNG 固定格式、Alpha 保持、不覆盖源路径、原子失败无半文件；
- JSON schema、严格非有限值、稳定属性和奇异值顺序；
- CSV invariant culture、引号/逗号/换行转义和固定 record 顺序。

### 14.6 G6/G7 Document、组合根与 View

- 稳定 ID、显示名、分类和第十四个 Document 注册；
- 两个 Scope 的 Session、因素缓存、Bitmap、取消和 k 完全隔离；
- snapshot schema 1 往返、未知 schema、坏值、缺失路径和轻量体积；
- 快照不包含 U/σ/V、RGBA、Bitmap 或报告；
- Standalone 通过真实 Module 解析第十四个 Document/View；
- AXAML 编译绑定、空态、运行态、失败态、stale 和取消态；
- 曲线控件空值、单值、全零、点击边界、键盘和高对比可读性；
- Rank debounce 只提交最新 generation，不产生无界任务；
- Document/Feature 源码不存在 Jacobi 旋转、矩阵乘法和 JSON 文件循环。

### 14.7 架构与禁止项门禁

增加源码级测试或明确审查：

- `Domain/SvdDecomposition` 不引用 Avalonia、Application、Infrastructure、JSON、DI、Host SDK；
- `Application/SvdDecomposition` 不引用 Avalonia、Feature、Infrastructure 具体实现；
- `Features/SvdDecomposition` 不包含核心 SVD/矩阵循环；
- 生产源码不存在 AIFLOW、Workflow Action、Workbench Command、通用 DAG 或算法反射发现入口；
- 不新增 MathNet、原生线性代数、图表、GPU 或机器学习依赖；
- 不出现 `CompressionRatio`、`CompressedBytes`、`SaveSpace` 等误导产品字段；
- 所有新增生产类的重要边界均有中文注释；
- 所有测试 0 跳过，不能用 Skip 暂时绕过慢例或平台问题。

### 14.8 每个实施包的最低本地门禁

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

这是本地开发门禁，不是 Windows CI 或发布门禁。本轮不新增 CI YAML，不制作 ZIP，不启动真实 Host，不执行安装、升级、
卸载或发布验收。任何文档都不得把 Standalone/Headless 结果写成发布完成。

## 15. G0–G9 实施包

### G0：产品、数学与基线冻结

**目标**

- 冻结非压缩器语义、128/256 代理、三颜色策略、矩阵坐标、neutral、Jacobi 协议和资源预算；
- 复跑并记录完整起始门禁；
- 准备手算、构造型和独立数值 Golden。

**交付物**

- `mathematical-principles.md` 初版；
- Golden 矩阵与来源说明；
- 资源估算表、收敛阈值候选和风险登记；
- `history/g0-product-math-and-baseline.md`。

**出口门禁**

- 不存在未决定的矩阵轴向、Chroma 中心或 k=0 语义；
- 442/442 起始测试在两配置复跑，0 警告、0 错误、0 跳过；
- 不改生产功能即可审查产品边界和 Golden。

### G1：共享缩放拆分与领域模型

**目标**

- 抽出不改变旧行为的面积缩放核心；
- 建立 SVD recipe、矩阵、因素、能量、诊断和资源估算模型。

**交付物**

- `Domain/Imaging/ImageAreaResampler`；
- `Domain/SvdDecomposition` 的不可变模型与 validator；
- 旧代理逐字节回归和新 128/256 预算测试；
- `history/g1-matrix-models-and-resource-boundary.md`。

**出口门禁**

- 旧 512/1024/2048 产品选项和结果不变；
- 数组所有权、有限值、尺寸和 checked 预算全部有负向测试；
- Domain 依赖方向门禁通过。

### G2：单边 Jacobi SVD 核心

**目标**

- 完成高/宽矩阵、旋转、收敛、归一化、排序、符号和诊断；
- 在 UI/Application 之前封板数值核心。

**交付物**

- `JacobiSvdDecomposer`；
- 转置适配与 factors builder；
- Golden、性质、取消和非收敛测试；
- `history/g2-one-sided-jacobi-core.md`。

**出口门禁**

- 所有固定矩阵通过重建、正交、奇异值和确定性门禁；
- 重复奇异值测试不错误依赖单向量唯一性；
- 生产结果不存在非有限值，取消可在 sweep 内生效。

### G3：能量、Rank-k 与单分量

**目标**

- 完成累计能量、数值秩、理论/直接误差、Rank-k 和有符号分量图；
- 证明 k 改变不重新分解。

**交付物**

- `SingularValueEnergyAnalyzer`、`LowRankReconstructor`、`SvdComponentProjector`；
- 能量与误差诊断模型；
- 曲线数据合同和单分量摘要；
- `history/g3-energy-rank-and-components.md`。

**出口门禁**

- 误差/能量单调性、Eckart–Young、全零和 σ² 分量能量通过；
- 无所有分量全图缓存；
- 理论尾能量与直接残差在冻结容差内一致。

### G4：颜色重建与策略比较

**目标**

- 完成单通道、RGB 独立、YCbCr 独立；
- 建立公平、有限、不自动排名的三策略比较。

**交付物**

- `SvdColorStrategyExecutor`、`SvdImageReconstructor`、`SvdReconstructionAnalyzer`；
- 三策略案例和取消部分结果；
- Alpha、neutral、裁切、质量与公平性测试；
- `history/g4-color-strategies-and-comparison.md`。

**出口门禁**

- 同图、同代理、同 k 规则不可绕过；
- RGB/YCbCr 一次性组合，Alpha 逐字节保持；
- 不存在“最佳策略”或“压缩率”产品字段。

### G5：Application Session、缓存与导出

**目标**

- 完成载图、分解、Rank、分量、比较、PNG 和报告窄用例；
- 建立 Session 缓存、指纹、stale、取消和原子导出。

**交付物**

- `Application/SvdDecomposition` 合同与用例；
- `SvdReportSerializer`、JSON/CSV schema 1；
- 文件对话框窄端口扩展和 PNG 导出；
- `history/g5-session-use-cases-and-export.md`。

**出口门禁**

- k/分量变化不调用分解；策略比较串行并复用缓存；
- stale/代理/指纹语义清晰，导出原子且不覆盖源图；
- 非有限 PSNR 使用合法结构化 JSON。

### G6：Document、DI、Module 与 Standalone

**目标**

- 登记第十四个 Persistable Document；
- 完成多实例生命周期、快照、组合根和 Standalone 入口。

**交付物**

- `SvdDecompositionDocument` 与 snapshot schema 1；
- Plugin ID、Module descriptor、服务注册；
- Standalone 入口；
- `history/g6-document-composition-and-standalone.md`。

**出口门禁**

- 两个 Scope 状态、缓存、取消和 Bitmap 隔离；
- snapshot 轻量、坏数据安全、恢复不自动读文件；
- Module 注册数量、顺序和稳定 ID 测试通过。

### G7：Avalonia UI 与交互

**目标**

- 完成原图/重建/分量三视图、曲线、Rank 联动、指标和比较表；
- 完成 debounce、键盘、高对比和 Headless 状态门禁。

**交付物**

- `SvdDecompositionView.axaml(.cs)`；
- `SingularValueCurveControl`；
- Headless View/Control 测试和人工交互清单；
- `history/g7-ui-and-interaction.md`。

**出口门禁**

- 拖动 k 不重新分解、不无界排队、旧结果不覆盖；
- 分量色标、代理徽标和“非压缩器”提示持续可见；
- View/Document 不含 SVD 数学循环。

### G8：专用文档与人工验收

**目标**

- 按现有能力目录惯例完成全部专用文档；
- 同步根索引、未来能力和共享职责说明；
- 记录 Standalone 人工验收，但不冒充真实 Host。

**交付物**

- `docs/design/svd-decomposition/README.md`；
- `user-manual.md`、`guide.md`、`mathematical-principles.md`、`implementation.md`、`testing.md`、`report-schema.md`；
- `history/README.md` 与 G0–G9 实施记录；
- 根 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md`、共享职责文档同步；
- `history/g8-documentation-and-manual-review.md`。

**出口门禁**

- “计划中”只能在生产代码与门禁全部完成后改为“V1 已实现”；
- 文档中的能力数、Document 数、测试数和输出限制与代码一致；
- 没有发布完成、文件压缩率或主观最优结论。

### G9：本地开发封板

**目标**

- 复跑全部 Debug/Release 本地门禁；
- 清理警告、跳过、临时资产、调试入口和文档偏差；
- 只声明本地开发完成。

**交付物**

- 最终 `testing.md` 自动证据；
- 风险、偏差和未验证结论；
- `history/g9-local-sealing.md`。

**出口门禁**

- locked restore 成功；Debug/Release build 0 警告/0 错误；全部 test 0 失败/0 跳过；
- `git diff --check`、架构和禁止项门禁通过；
- 不使用 AIFLOW，不新增 Windows CI，不执行 ZIP、真实 Host 或发布门禁。

## 16. 文档同步清单

### 16.1 本计划落地时（历史步骤，已完成）

- 在 `docs/future-capabilities.md` 的 SVD 条目链接本文，并标记“已完成设计、待实施”；
- 在 `docs/design/README.md` 增加“计划中能力”入口，不混入已完成能力表；
- 在 `docs/README.md` 的未来能力说明旁增加本计划入口；
- 不修改根 README 的“当前已实现能力/Document 数”，因为本轮只生成计划。

### 16.2 实施完成时

- 建立完整 `docs/design/svd-decomposition/` 专用文档集；
- 将未来能力条目改为实现摘要和专用 README 链接；
- 更新根 README、docs README、design README 的能力数和第十四个 Document；
- 更新 `shared/project-and-window-responsibilities.md` 的实例所有权；
- 更新测试数字、技术基线、已证明与未证明结论；
- 发布资料继续保持“延期”，直到用户明确进入发布阶段。

## 17. 风险、对策与回滚

| 风险 | 早期信号 | 对策 | 回滚边界 |
| --- | --- | --- | --- |
| Jacobi 在病态矩阵不收敛 | sweep 达上限、正交误差不降 | G2 压力 Golden、稳定旋转、结构化失败 | 不开放 UI；保留 G1 模型 |
| 256 三通道延迟过高 | Standalone 长时间无响应 | 默认 128、串行、缓存、取消、减少分配 | V1 仅保留 128，不放宽算法容差 |
| 重复奇异值分量跳变 | 单分量随实现细节改变 | 稳定排序/符号、明确子空间不唯一 | 不承诺分量身份，不删正确 SVD |
| Cb/Cr 偏置误处理 | k=0/全秩出现色偏 | neutral Golden、一次性组合 | 暂停 YCbCr 策略，单通道/RGB 不受影响 |
| Rank slider 任务堆积 | CPU 持续高、旧图闪回 | debounce、generation、取消、单任务 | 临时改为松手提交，数值核心不变 |
| 因子数组被修改 | 同一 k 重建不确定 | 接管/复制、只读暴露、所有权测试 | 禁止合入可写暴露 |
| 被误解为压缩器 | UI/报告出现文件大小结论 | 固定边界文案、禁止字段测试 | 删除误导展示，不影响低秩实验 |
| 共享 resampler 回归 | 既有工具代理变化 | 逐字节旧行为回归 | 恢复原 projector，SVD 暂不复用 |
| 报告非法非有限数字 | exact PSNR 序列化失败 | nullable + 状态字段、schema 测试 | 阻断报告导出，不影响观察功能 |

## 18. 完成定义

只有同时满足以下条件，SVD Decomposition V1 才能在开发阶段标记为完成：

1. 用户能在真实 Persistable Document 中完成载图、分解、能量观察、Rank-k 重建、单分量观察和三策略有限比较；
2. 高/宽/方/退化矩阵的 SVD 通过独立 Golden、重建、正交、排序、有限值、取消和非收敛门禁；
3. Rank-k 理论尾能量、直接矩阵误差和量化后图像质量被清晰分开；
4. 单通道、RGB、YCbCr 的 neutral、Alpha、舍入、裁切和共同 k 规则全部有测试；
5. 分解一次后 k/分量变化不重新分解，缓存、stale、generation、多实例和释放边界正确；
6. Domain/Application/Infrastructure/Feature 依赖方向符合 SOLID，没有万能服务、不必要接口、通用 DAG 或模式炫技；
7. 生产代码的重要数学、数值稳定、颜色、资源、生命周期和导出取舍均有详细中文注释；
8. JSON/CSV/PNG 只导出当前分析代理事实，不包含非法数值、文件压缩率或原尺寸暗示；
9. 所有新增与既有测试在 Debug/Release 下 0 失败、0 跳过，构建 0 警告、0 错误；
10. 专用文档、实施历史、根索引和未来能力状态全部与实际代码一致；
11. 没有使用 AIFLOW，没有新增 Windows CI，也没有执行或宣称 ZIP、真实 Host、安装或发布门禁。

上述完成定义仍只代表本地开发封板。Windows CI、真实 Host、ZIP、安装升级和发布验收留到用户明确进入发布阶段时执行。
