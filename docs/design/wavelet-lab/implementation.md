# ImageLabPlugin V1 Wavelet Lab／小波实验室设计与实施计划

> 计划状态：G0–G10 已完成，本地开发封板<br>
> 基线日期：2026-08-31<br>
> 产品名称：Wavelet Lab／小波实验室<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 起始自动基线：2026-08-31 实际复跑 locked restore；Debug/Release build 均零警告、零错误；两配置 test 均 304/304 通过、零跳过<br>
> 完成自动证据：2026-08-31 locked restore；Debug/Release build 均零警告、零错误；两配置 test 均 333/333 通过、零跳过<br>
> 核心路线：可逆二维离散小波变换 + 多层系数金字塔 + 可解释子带投影 + 阈值去噪与有界参数扫描 + 同协议负载下的 DCT/DWT 水印实验对比 + 可复用 Haar 基础<br>
> 首要原则：SOLID 是所有实现取舍的第一约束；设计模式只用于已经存在的变化点并保持朴素；生产代码使用详细中文注释解释数学约定、边界和对象所有权；不使用 AIFLOW；不新增 Windows CI；本阶段不执行 ZIP、真实 Host 或发布门禁

| 实施包 | 计划状态 | 目标 | 主要交付物 |
| --- | --- | --- | --- |
| G0 | 完成 | 冻结产品范围、术语、轴向、边界、资源预算和 Golden 基线 | 决策记录、测试向量、预算表 |
| G1 | 完成 | 建立不可变小波值对象、金字塔布局和校验 | `Domain/Wavelets` 基础模型 |
| G2 | 完成 | 完成 Haar 一维/二维正变换、逆变换和奇数尺寸处理 | Haar 数值核心与 Golden 测试 |
| G3 | 完成 | 增加 CDF 5/3 基础小波，并固定统一的扩展与重建协议 | 第二种朴素策略与交叉回归 |
| G4 | 完成 | 完成多层分解、子带可视化和重建诊断 | 金字塔、投影器、重建诊断 |
| G5 | 完成 | 完成硬/软阈值去噪、噪声估计和有界质量扫描 | 去噪配方、扫描结果、质量指标 |
| G6 | 完成 | 建立窄应用用例、Session、代理/完整尺寸执行和 PNG/报告导出 | `Application/Wavelets` 用例层 |
| G7 | 完成 | 完成实验性 DWT 水印载体及 DCT/DWT 公平对比 | 载体适配、容量/隐蔽性/鲁棒性报告 |
| G8 | 完成 | 接入第十个多实例 Persistable Document 和持久化生命周期 | Document、组合根、Standalone |
| G9 | 完成 | 完成 Avalonia 联动界面、取消和 Headless View 门禁 | View、专用控件、交互测试 |
| G10 | 完成 | 完成双配置本地门禁、专用文档和开发阶段封板 | 测试证据、文档集、历史记录 |

本文是 Wavelet Lab 的总计划与实施状态。G0–G10 对应生产代码、自动测试、历史记录和本地门禁证据已经落地，
能力已注册为第十个 Persistable Document。该状态仍只表示本地开发封板，不表示真实 Host、Windows CI、ZIP 或
发布验收已经完成。

## 0. 实施结果摘要

- 生产代码按 `Domain → Application/Ports ← Infrastructure → Feature` 分层；未出现万能 Wavelet Service。
- 变化点只使用 Haar/CDF 5/3 Strategy 和 DCT/DWT benchmark Adapter；其余类型保持普通 sealed class/不可变值。
- 完成代理与显式完整尺寸、轻量快照、generation 防迟到、stale 导出、PNG 与 JSON/CSV 原子发布。
- 自动测试从 304 增至 333；两配置 333/333 通过、0 跳过，构建 0 警告/0 错误。
- 实际偏差：V1 UI 以当前子带投影和表格等价摘要呈现扫描/benchmark；没有引入复杂图表控件或任意交互式系数编辑，
  以保持朴素性和本轮资源边界。UI 可从最深层逐级逆变换到当前层，并以有界灰度图展示阶段结果。
- 未使用 AIFLOW，未增加 Windows CI，未运行 ZIP、真实 Host 或任何发布门禁。

## 1. 产品定位与 V1 用户闭环

Wavelet Lab 面向“看懂图像在空间位置和尺度上的局部变化”，不是一键降噪器，也不是新的通用水印编辑器。
它首先提供可信的 DWT 数值基础，然后让用户观察、修改和重建系数，最后在同一实验条件下比较 DCT 与 DWT
水印载体。wHash 只复用该基础，不在 Wavelet Lab 内复制一套哈希产品界面。

### 1.1 用户闭环

```text
显式选择一张 PNG 或 JPEG 图片
    ↓
解码 RGBA8888，并建立最大边 512/1024/2048 的抗混叠分析代理
    ↓
选择分析通道、小波基、分解层数和系数显示方式
    ↓
查看每一级 LL、LH、HL、HH 子带、能量占比和系数探针
    ↓
从指定层逐级逆变换，比较原图、重建图和重建残差
    ↓
选择硬阈值或软阈值，并明确选择受影响的层与细节子带
    ↓
单次去噪或运行受控的“阈值 × 层数”扫描
    ↓
可选载入干净参考图，比较 PSNR/SSIM；无参考图时只报告客观信号统计
    ↓
可选使用同一载体、同一有限 Payload 和同一扰动集合比较 DCT/DWT 水印
    ↓
显式执行完整尺寸结果，并原子导出 PNG 或 JSON/CSV 实验报告
```

### 1.2 产品形态

Wavelet Lab 固定为第十个多实例 Persistable Document，而不是 singleton Tool：

| 字段 | V1 决策 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.wavelet-lab` |
| 显示名称 | `小波实验室` |
| 描述 | `分解、观察并重建多尺度小波子带，实验阈值去噪及 DCT/DWT 水印差异` |
| 分类 | `图像分析` |
| Host 注册 | `AddPersistableDocument<WaveletLabDocument, WaveletLabView>` |
| 实例基数 | 多实例；每个实例独立拥有图片、参数、金字塔、结果、报告和取消令牌 |

Document 是正确形态，因为图片路径、分解配方、当前层、阈值扫描和完整尺寸结果都是可保存的实验上下文；
singleton Tool 无法让用户并排比较不同小波基或不同阈值，也无法在关闭某次实验时准确释放大数组和 Bitmap。

## 2. V1 范围与明确非目标

### 2.1 V1 必须完成

- Haar 正交小波，以及 CDF 5/3 双正交基础小波；两者都必须通过独立 Golden 与正逆重建门禁。
- 对 R、G、B、Y、Cb、Cr 单一分析通道执行二维可分离 DWT，默认使用 Y；Alpha 不参与系数变换。
- 1 至 6 级分解；实际最大层数同时受最短边、所选小波支持长度、填充尺寸和内存预算限制。
- 明确显示 `LL`、`LH`、`HL`、`HH` 的轴向定义、实际尺寸、系数范围、能量和相对能量。
- 支持“只重建当前层”“从最深层逐级重建”和“完整逆变换”，并显示重建误差。
- 硬阈值、软阈值、手动阈值和基于最细 HH 中位绝对偏差的通用阈值建议。
- 可选择阈值作用层和 LH/HL/HH 子带；LL 默认且始终不被去噪阈值修改。
- 受控的阈值与分解层数扫描，显示质量、残差、非零系数比例和耗时，不构造无界二维网格。
- 可选干净参考图。只有参考图与结果尺寸一致时才给出 PSNR/SSIM 改善结论。
- 实验性 Haar-DWT 水印载体，以及与现有 DCT 水印在共同 Payload 和共同扰动集合下的容量、隐蔽性、鲁棒性对比。
- 分析代理用于交互；完整尺寸变换必须由用户显式触发，并且只有与当前配方指纹一致的完整结果允许导出。
- PNG 结果导出、JSON/CSV 报告导出、取消、防迟到覆盖、多实例隔离、轻量快照和中文错误说明。
- Debug/Release 本地 locked restore、warn-as-error build、全部自动测试和零跳过门禁。

### 2.2 V1 明确不实现

- AIFLOW、Workflow Action、Workbench Command、节点图、DAG、脚本或任意批处理工作流。
- 无限种小波、运行时插件式小波发现、用户输入任意滤波器系数或“自动选择最佳小波”。
- 连续小波变换、三维小波、双树复小波、Curvelet、Shearlet 或小波包全树搜索。
- GPU、Compute Shader、原生库、SIMD 性能承诺或完整尺寸实时预览承诺。
- 无参考图像质量评价算法、学习型去噪、BM3D、深度网络或把“更平滑”表述成“恢复真实细节”。
- 任意多参数水印攻击搜索、隐藏几何配准、批量目录扫描或通用水印协议迁移。
- 把 DWT 水印伪装成现有 DCT 水印格式；V1 的 DWT 载体仅由 Wavelet Lab 实验入口写入和读取。
- 在 Image Fingerprint 内提前加入 wHash 占位实现；只有共享 Haar API 完成封板后再单独设计 wHash V1.1。
- JPEG 结果导出；有损编码会把变换/阈值误差与编码误差混为一谈。
- Windows CI、真实 Host、ZIP 打包、安装升级卸载、发布说明或发布完成声明。

### 2.3 教学结论边界

- `LH`/`HL` 的命名在不同教材中可能互换，界面必须同时显示轴向，不允许只显示两个字母。
- 细节系数变小表示该离散基下的响应变小，不等于对应物体或纹理已经被正确识别。
- PSNR/SSIM 更高只表示相对给定参考图更接近，不自动等于主观观感更好。
- 无干净参考图时，不能用“去噪后与噪声图更接近”证明去噪有效；此时只报告噪声估计、残差和系数稀疏度。
- 某一组 DCT/DWT 水印对比只对固定载体、Payload、强度、扰动和实现成立，不宣传一种变换普遍优于另一种。

## 3. 数学与数据协议

### 3.1 坐标、轴向和子带布局

二维变换固定先沿 X 轴逐行处理，再沿 Y 轴逐列处理。子带字母按 `(X 方向滤波, Y 方向滤波)` 定义：

| 子带 | 轴向定义 | 常见可视响应 | packed 布局 |
| --- | --- | --- | --- |
| LL | X 低通、Y 低通 | 近似图像 | 左上 |
| LH | X 低通、Y 高通 | 水平边缘/纵向变化 | 左下 |
| HL | X 高通、Y 低通 | 垂直边缘/横向变化 | 右上 |
| HH | X 高通、Y 高通 | 对角与高频细节 | 右下 |

界面标题必须采用如 `LH（X低通 / Y高通）` 的完整形式。方向测试使用水平条纹、垂直条纹和单点冲激，
防止实现正确但标签互换。每一级只继续分解上一级 LL，不能把四个子带都静默展开成小波包。

### 3.2 尺寸扩展与裁剪

- 源图可以是奇数尺寸，不要求用户先裁剪。
- 对请求的层数 `L`，分析平面在右侧和底部采用确定性的对称端点扩展，使宽高成为 `2^L` 的整数倍。
- 金字塔描述符保存源尺寸、扩展后尺寸、每级有效区域和裁剪信息；逆变换结束后严格裁回源尺寸。
- 扩展只发生在 double 分析平面中，不修改 `PixelImage`，也不写入快照。
- 资源预算按扩展后尺寸计算；扩展后超过预算时在分配前结构化阻断，不能捕获 `OutOfMemoryException` 后继续。
- Haar 能量守恒只相对于“扩展后的分析平面”校验，文档不得拿扩展能量与未扩展原图直接比较。

### 3.3 Haar 约定

Haar 使用正交归一化形式。对相邻样本 `a`、`b`：

```text
low  = (a + b) / sqrt(2)
high = (a - b) / sqrt(2)

a = (low + high) / sqrt(2)
b = (low - high) / sqrt(2)
```

计算使用 double，不在级间量化。正变换和逆变换必须使用同一系数与扫描顺序；字节舍入只发生在最终图像重建。
Haar 是 wHash 未来唯一允许复用的 V1 小波入口，wHash 不得依赖 Wavelet Lab 的 Document 或 Avalonia 类型。

### 3.4 CDF 5/3 约定

CDF 5/3 使用 lifting 实现，并固定对称边界延拓、predict/update 次序和缩放约定。G0 必须把每一步公式、奇偶样本
布局和参考向量写入 `mathematical-principles.md` 后才允许写生产代码。CDF 5/3 是双正交变换，不套用 Haar 的
Parseval 能量守恒断言；它只执行正逆重建、边界、方向和稳定性门禁。

选择第二种小波的目的，是证明领域边界确实支持不同分解策略，并让用户观察短支撑正交基与 lifting 双正交基的差异；
不是为了建立任意小波框架。V1 不再增加第三种小波。

### 3.5 packed 金字塔与所有权

- 每次分解拥有一个连续 `double[]` 系数缓冲，按标准四象限 packed 布局保存全部层级。
- `WaveletLevelDescriptor` 只保存矩形区域、子带范围、层号和原/扩展尺寸，不复制系数。
- 领域对象在构造时复制外部输入或接管明确标注的内部缓冲，之后只暴露只读视图。
- 阈值处理返回新的金字塔或由专用 builder 创建副本，不能修改仍被 UI、扫描案例或水印比较共享的基线金字塔。
- View 只能消费有界的投影像素和统计，不直接持有可写系数数组。

### 3.6 重建与量化

- 未修改系数的 double 平面正逆误差作为数值门禁，不能先转 byte 再判断算法可逆。
- 单通道重建沿用 `ImageChannelConverter`：R/G/B 直接替换；Y/Cb/Cr 按现有 YCbCr 公式回写；Alpha 逐字节保留。
- 最终样本使用项目统一的有限值校验、`MidpointRounding.AwayFromZero` 和 `[0,255]` 裁切，并报告低端/高端裁切数。
- “逐级重建”必须保存每一级输出尺寸和误差；显示代理不得冒充完整尺寸结果。

## 4. 阈值去噪与质量比较

### 4.1 阈值配方

`WaveletDenoiseRecipe` 是不可变值对象，至少包含：小波 ID、层数、阈值模式、阈值来源、阈值数值、目标层集合、
目标细节子带集合和通道模式。其稳定指纹绑定代理结果、完整尺寸结果和导出报告。

| 模式 | 公式 | V1 说明 |
| --- | --- | --- |
| Hard | `c' = 0, |c| < T; 否则 c` | 保留大系数，但可能产生不连续伪影 |
| Soft | `c' = sign(c) * max(|c|-T, 0)` | 更连续，但会压缩保留系数 |
| Manual | 用户显式给出 `T >= 0` | 输入必须有限，并受数据相关上限保护 |
| Universal | `T = sigma * sqrt(2 * ln(N))` | 只作为可见建议，不静默覆盖用户值 |

噪声尺度默认从最细层 HH 估计：`sigma = median(|HH|) / 0.67448975`。空子带、全零子带、非有限值和样本过少
必须产生明确状态。LL 不允许进入阈值选择，避免把低频近似误称为普通去噪细节。

### 4.2 单次结果

每次去噪结果至少报告：

- 当前配方指纹、代理/完整尺寸标识和耗时；
- 原始与保留的非零细节系数数、保留比例和各层/子带能量变化；
- 重建前后范围、裁切数、RGB/Alpha 改变量和残差投影；
- 有参考图时的 PSNR-Y、PSNR-RGB、全局 SSIM-Y；
- 无参考图时的噪声估计、残差 RMS 和系数稀疏度，并明确标记“无参考质量结论不可用”。

### 4.3 有界参数扫描

- 扫描只允许一个阈值轴与一个离散层数集合，不提供任意多参数笛卡尔积。
- 阈值点最多 21 个，层数最多 6 个，总案例最多 60；超过时在执行前阻断。
- 默认阈值序列由当前 `sigma` 派生并在报告中写出实际 double 值，不能只保存滑块百分比。
- 案例按固定顺序串行执行，逐案例检查取消；V1 不为追求速度引入并行调度器。
- 图表只展示已完成案例；取消后保留已完成结果并明确标记 `Canceled`，不伪装成完整扫描。
- 最优点只允许按用户显式选择的指标排序；无参考图时不得给出“最佳去噪质量”。

## 5. DCT 与 DWT 水印实验对比

### 5.1 公平比较原则

比较不是把两个 Document 的截图拼在一起，而是由窄应用用例执行同一实验定义：

- 使用同一张完整尺寸载体、同一 Payload 字节、同一密码/随机种子策略和同一纠错开销。
- 先分别报告两个载体的原始槽位和最大 Payload；共同恢复率比较只使用不超过两者较小容量的 Payload。
- 强度参数不能因为名字相同就假设量纲相同；报告保存 DCT QIM 步长和 DWT 差分 QIM 步长的实际值。
- 隐蔽性统一使用 PSNR-Y、PSNR-RGB、全局 SSIM-Y、改动像素比例和最大绝对误差。
- 鲁棒性统一使用相同的有限扰动配方和种子，报告检测、原始 BER、纠错后完整性和首次失败点。
- 每个结论都携带载体版本、小波/层/子带、Profile、Payload 长度、扰动参数和代码版本字段。

### 5.2 DWT 实验载体

V1 的 DWT 载体只采用 Haar、Y 通道、显式选择的 LH/HL 细节子带和固定层集合。它使用成对系数差分 QIM：

```text
d = c1 - c2
把 d 量化到由 bit 奇偶决定的格点
将校正量按 +delta/2、-delta/2 分配回 c1、c2
```

系数对映射、保留区、置乱、容量和读取置信度必须有版本化且确定性的定义。载体复用现有 Frame、安全和纠错语义时，
只能通过窄适配层接入；不得让 DWT 代码依赖 `FrequencyWatermarkCarrier` 的 8×8 块内部细节，也不得改写现有
DCT Golden。DWT 载体使用独立 carrier ID，V1 不要求现有“提取与验证”Document 自动探测它。

### 5.3 扰动集合

V1 对比固定复用 Robustness Lab 已有算子和确定性随机基础，至少包含：

- 无扰动回读；
- JPEG 质量 90、75、50；
- 等比例缩放后恢复原尺寸；
- 轻度高斯噪声；
- 轻度高斯模糊；
- 亮度/对比度小幅变化。

裁剪、旋转和透视可以作为明确标记的扩展案例，但不能为了得到“更好”曲线偷偷加入 DWT 几何配准。对比用例只
编排既有算子，不复制 Robustness Lab 的像素循环，也不引入 AIFLOW 或通用攻击工作流。

### 5.4 报告

报告至少分为 `ExperimentDefinition`、`CarrierCapacity`、`Imperceptibility`、`RobustnessCases`、`Conclusions` 和
`Limitations`。JSON 保存完整结构，CSV 保存扁平案例表。序列化器放在 Infrastructure，领域和应用层不依赖 JSON。
报告中的“推荐”只能是基于用户选择指标的排序，不写“DWT 天生更鲁棒”等超出样本的结论。

## 6. SOLID 架构与朴素设计模式

### 6.1 依赖方向

```text
Features/WaveletLab
        ↓
Application/Wavelets ─────→ Application/Ports
        ↓                         ↑
Domain/Wavelets             Infrastructure/Persistence
        ↓
Domain/Imaging、Domain/Comparison
```

- Domain 不引用 Avalonia、文件系统、DI、JSON、Document、Bitmap 或 Host SDK。
- Application 只协调用例和 Session，不写 DWT 数学循环，不创建 Avalonia 控件。
- Infrastructure 只实现编解码、原子文件和报告序列化等端口，不决定阈值或水印算法。
- Feature 拥有每实例状态、命令、Bitmap 与交互，不复制领域公式。
- 组合根是唯一登记事实；Standalone 通过 Module/DI 解析真实 Document 和 View。

### 6.2 单一职责拆分

| 类型/边界 | 唯一职责 | 明确不负责 |
| --- | --- | --- |
| `IWaveletTransform` | 对一个 double 平面执行某一种正/逆变换 | 图片解码、阈值、可视化、报告 |
| `HaarWaveletTransform` | Haar 数值协议 | 选择算法、UI 状态 |
| `Cdf53WaveletTransform` | CDF 5/3 lifting 数值协议 | Haar 分支、报告 |
| `WaveletPyramid` | 不可变系数所有权和层描述 | 修改系数、创建 Bitmap |
| `WaveletThresholdProcessor` | 按配方生成阈值后金字塔 | 估算质量、导出 |
| `WaveletNoiseEstimator` | 从指定 HH 子带估计 `sigma` | 自动替用户改参数 |
| `WaveletSubbandProjector` | 把系数映射为有界显示像素 | 修改真实系数 |
| `WaveletImageReconstructor` | 通道回写、舍入和裁切统计 | 文件保存、Document 状态 |
| `DwtWatermarkCarrier` | DWT 槽位、写入和读取 | DCT 实现、扰动执行 |
| `WatermarkCarrierBenchmarkUseCase` | 编排两个载体和共同案例 | 实现任一变换或算子 |
| `WaveletSession` | 持有一次解码的源图、参考图和代理 | 全局缓存、服务定位 |
| `WaveletLabDocument` | 每实例状态、生命周期和命令 | 数值算法、JSON 格式 |

### 6.3 允许使用的模式

- **Strategy**：只用于 Haar 与 CDF 5/3 两种已存在的正逆变换差异；以稳定 ID 选择，不做运行时反射发现。
- **Factory/switch**：组合根中的固定映射把稳定 ID 解析为策略；不建立抽象工厂层级。
- **Adapter**：只把既有 DCT 水印入口适配成统一 benchmark contract；不改写现有 DCT 载体。
- **Immutable Recipe/Result**：参数、金字塔描述和结果使用不可变对象，避免异步执行期间被 UI 修改。

不引入 Mediator、Event Bus、Visitor、Decorator 链、Service Locator、通用 Pipeline、插件式算法目录或为了“模式齐全”而
增加接口。只有一个实现且没有替换需求的类保持普通 sealed class。

### 6.4 SOLID 审查门禁

- SRP：任何类同时出现 DWT 循环、文件 IO 和 UI 状态即判定失败并拆分。
- OCP：新增第三种小波时只能增加策略和固定登记，不修改阈值、投影、Session 和 Document 的核心流程。
- LSP：每个 `IWaveletTransform` 都必须满足相同的尺寸、取消、有限值、正逆和所有权契约。
- ISP：报告导出、图片选择、水印 benchmark 使用窄端口，不扩充 `IImageFileDialog` 成万能文件服务。
- DIP：Application 依赖变换/载体抽象和现有端口；Feature 不直接 `new` Infrastructure 实现。

## 7. 建议目录与主要文件

```text
src/ImageLabPlugin.Plugin/
├─ Domain/Wavelets/
│  ├─ WaveletModels.cs
│  ├─ WaveletPyramid.cs
│  ├─ HaarWaveletTransform.cs
│  ├─ Cdf53WaveletTransform.cs
│  ├─ WaveletThresholdProcessor.cs
│  ├─ WaveletNoiseEstimator.cs
│  ├─ WaveletSubbandProjector.cs
│  ├─ WaveletImageReconstructor.cs
│  └─ DwtWatermarkCarrier.cs
├─ Application/Wavelets/
│  ├─ WaveletContracts.cs
│  ├─ WaveletAnalysisUseCases.cs
│  ├─ WaveletDenoiseUseCases.cs
│  ├─ WaveletBenchmarkUseCases.cs
│  └─ WaveletReportExportUseCase.cs
├─ Infrastructure/Persistence/
│  └─ WaveletExperimentReportSerializer.cs
└─ Features/WaveletLab/
   ├─ WaveletLabDocument.cs
   ├─ WaveletLabView.axaml
   ├─ WaveletLabView.axaml.cs
   ├─ WaveletPyramidControl.cs
   ├─ WaveletScanChartControl.cs
   └─ WaveletLabHelpCatalog.cs

tests/ImageLabPlugin.Tests/
├─ WaveletTransformTests.cs
├─ WaveletPyramidTests.cs
├─ WaveletDenoiseTests.cs
├─ WaveletWatermarkTests.cs
├─ WaveletUseCaseTests.cs
├─ WaveletLabDocumentTests.cs
└─ WaveletLabViewTests.cs
```

文件名是计划基线，不要求为了和表格一一对应而创建空文件。若实现后某个文件过大，应按职责拆分；若两个很小的值对象
自然属于同一上下文，可以保留在同一文件。禁止用一个 `WaveletService.cs` 承载全部算法和用例。

## 8. 应用用例、Session 与并发

建议保持窄用例：

- `IPrepareWaveletSessionUseCase`：只解码源图/可选参考图并建立分析代理。
- `IDecomposeWaveletUseCase`：只根据配方生成不可变金字塔和统计。
- `IReconstructWaveletUseCase`：只从给定金字塔逐级或完整重建。
- `IDenoiseWaveletUseCase`：只执行一次阈值处理与重建。
- `IRunWaveletQualityScanUseCase`：只编排有界案例并返回已完成结果。
- `IRunWatermarkCarrierBenchmarkUseCase`：只执行公平 DCT/DWT 对比。
- `IExportWaveletImageUseCase`：只允许导出与当前配方指纹一致的完整尺寸 PNG。
- `IExportWaveletReportUseCase`：只序列化并原子写入当前完整报告。

Document 对代理分解、完整尺寸执行、扫描和水印 benchmark 分别持有取消源与 generation。新请求先递增 generation 再取消
旧任务；旧任务即使未及时响应取消，也不得覆盖新状态。关闭 Document 时统一取消并释放 Session、Bitmap 和大结果。
V1 不建立全局结果缓存，也不跨 Document 共享可变金字塔。

## 9. 持久化、资源与错误处理

### 9.1 快照

快照 schema 从 `1` 开始，只保存：

- 源图路径和可选参考图路径；
- 小波稳定 ID、分析通道、分解层数、当前层/子带；
- 阈值模式、来源、数值、目标层和子带；
- 分析代理档位、显示归一化模式和最后选择的归一化坐标；
- 水印对比的非敏感实验参数。

快照不保存图片字节、double 系数、Bitmap、完整尺寸结果、扫描案例、Payload 明文、密码、派生密钥或取消对象。恢复只
恢复路径和参数，不自动读文件、不自动计算、不自动执行水印。未知 schema 或非法参数回退到安全默认值并显示可恢复错误，
不能让整个 Host 工作区恢复失败。

### 9.2 资源预算

- 沿用 64 MiB 编码输入和 16,000,000 像素上限。
- 代理默认最大边 1024，可选 512/2048；UI 始终显示实际代理尺寸。
- 层数硬上限 6；扩展后系数样本数和预计字节在分配前计算并检查溢出。
- 单次代理操作只保留基线金字塔、当前修改金字塔和必要投影，不缓存每个历史滑块位置。
- 扫描结果保存统计和有界缩略图，不保存每个案例的全尺寸系数数组。
- 完整尺寸 DWT、去噪和水印必须显式执行、可取消，并显示阶段和耗时。
- 所有像素/系数循环至少按行或按层检查取消，禁止只在入口检查一次。

### 9.3 结构化错误

至少区分：路径不存在、解码失败、参考图尺寸不匹配、层数不可用、扩展后预算超限、非有限系数、无有效细节子带、
阈值非法、扫描案例超限、容量不足、Payload 恢复失败、结果过期、导出失败和用户取消。领域层抛出精确异常或返回明确
结果；Document 负责把它翻译成中文状态，不吞掉异常后继续导出旧结果。

## 10. UI 信息架构

建议采用与现有实验 Document 一致的三栏/分区布局：

- 左侧“输入与参数”：源图、参考图、小波、通道、层数、阈值、目标层/子带、代理档位和执行命令。
- 中间“金字塔”：packed 总览与当前 LL/LH/HL/HH，支持层级导航、缩放、同步坐标和系数探针。
- 右侧“结果与解释”：重建图、残差、能量表、去噪统计、质量曲线及水印对比矩阵。
- 底部状态区：当前是代理还是真实尺寸、实际尺寸、配方指纹摘要、进度、耗时、取消和错误。

交互约束：

- 系数显示的线性/对数/对称归一化只影响投影，不修改系数。
- 切换层、子带或显示方式不重新解码源图；修改小波、层数或通道才使金字塔失效。
- 修改阈值只使去噪结果失效，不重复构建未修改的基线金字塔。
- 修改任一执行参数后，旧完整尺寸结果和旧报告立即标记 stale，并禁用导出。
- 所有图表提供表格等价信息；颜色不是区分子带、成功/失败或曲线的唯一方式。
- 键盘可到达主要命令、层级选择和表格；Headless 测试必须实例化完整 View 并验证关键绑定。

## 11. 中文注释与设计说明规范

生产代码注释使用中文，并把“为什么”和协议边界写清楚：

- 每个领域模型、应用用例、端口、Document 和专用控件写详细 XML 摘要。
- Haar/CDF 5/3 公式、X/Y 扫描次序、LH/HL 命名、对称扩展、packed 偏移和逆变换次序必须在相邻代码处说明。
- 对 `Span`/数组切片注明所有权、长度、是否允许原地写入，以及为何不会与其他层区域重叠。
- 对取消、generation、防迟到覆盖、配方指纹和 stale 导出保护说明并发原因。
- 对水印对比说明“共同 Payload”“不同强度量纲”和结论限制，避免维护者误删公平性门禁。
- 对复用现有 DCT/Robustness 代码的位置说明依赖方向，禁止以后为了方便反向引用 Feature。

不要给赋值、显而易见的循环或控件属性逐行写“把 X 设置为 Y”式注释。详细不等于重复代码；注释必须帮助维护者
理解公式、边界、生命周期和取舍。重要设计变化同时同步本文、数学文档或历史记录，不能只留在代码注释里。

## 12. 单元测试与开发门禁

### 12.1 数值 Golden

- Haar 一维 2/4/8 样本的手算正变换与逆变换。
- 水平条纹、垂直条纹、对角图案和单点冲激的 2D 子带方向 Golden。
- 4×4、5×3、17×9、1×N/N×1 等边界尺寸；不支持的退化尺寸必须明确阻断或按冻结协议处理。
- 1 至 6 级 packed 区域无重叠、无越界，逐级尺寸和裁剪与描述符一致。
- 未修改 Haar/CDF 5/3 金字塔正逆重建的 double 最大误差和 RMS 误差阈值。
- Haar 在扩展平面上的 Parseval 能量误差；CDF 5/3 不错误套用同一断言。
- 固定输入、固定配方多次执行逐系数一致；取消在行/层边界可观察。

建议门槛：有限测试向量最大绝对误差 `<= 1e-10`；较大随机平面按规模使用记录在测试中的绝对/相对容差。不得用
“看起来一样”或只比较 byte 重建掩盖正逆公式错误。

### 12.2 去噪与质量测试

- `T=0` 的 Hard/Soft 都保持系数和重建结果不变。
- Hard/Soft 在阈值边界、正负系数、零和非有限输入上的行为精确固定。
- 增大阈值时非零细节系数数不增加；LL 永远不被修改。
- MAD 噪声估计、Universal 阈值、空/全零 HH 和样本过少分支。
- 只选择某层/某子带时，其他区域逐系数不变。
- RGB/Y/Cb/Cr 回写、Alpha 保留、舍入和裁切计数。
- 干净参考图尺寸不符时阻断；无参考图时不生成虚假的 PSNR 改善排序。
- 21 点/60 案例上限、取消后部分结果状态和固定案例顺序。

### 12.3 水印对比测试

- DWT 槽位容量、保留区、系数对不重叠和稳定映射 Golden。
- 容量边界 `0/1/max/max+1`、固定种子可复现和不同种子产生不同映射。
- 无扰动写入/读取、错误密码/错误载体、Payload 完整性和原始 BER。
- 写入前后图片尺寸、Alpha、像素范围和质量统计。
- 共同 Payload 规则：任一载体容量不足时不得执行伪公平恢复率比较。
- DCT adapter 输出与改造前现有 DCT Golden、容量和协议测试完全一致。
- 固定 JPEG/缩放/噪声/模糊/亮度案例由同一配方与种子作用于两个载体。
- 报告 JSON round-trip、CSV 转义、枚举稳定 ID、非有限数值表达和结论限制字段。

### 12.4 Application、Document 与 View 测试

- Session 只解码一次，代理与完整尺寸对象明确分离，释放后拒绝继续使用。
- 用例不读 UI 属性、不创建 Bitmap；Document 不包含 DWT 数学循环。
- 新请求取消旧请求，迟到结果不能覆盖当前 generation。
- 参数变化使完整结果和报告 stale，过期指纹导出被拒绝。
- 快照只含轻量参数；不含 RGBA、系数、Bitmap、Payload、密码或报告；恢复不自动解码。
- 两个 DI Scope 的 WaveletLabDocument 状态、Session、取消和结果互不影响，无状态算法服务可安全共享。
- Module 只新增第十个 Persistable Document，不新增 Tool、Workflow Action 或 Workbench Command。
- Standalone 使用真实 Module/DI/View，不复制贡献清单或业务实现。
- Avalonia Headless 下 View 可创建、关键命令/绑定有效、错误/空态/取消态可见。
- 生产源码扫描继续证明没有 `AIFLOW`、通用 DAG 和动态工作流入口。

### 12.5 每个 G 包的最低门禁

1. 本包新增/修改测试全部通过，且既有 304 项基线测试无回归。
2. `dotnet build -c Debug -warnaserror` 零警告、零错误。
3. 本包涉及的文档和 `history/gN-*.md` 同步完成。
4. G8 之后增加 Release build/test；G10 必须完整复跑下列开发阶段门禁。

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

本轮明确不创建 Windows CI，不运行 ZIP/发布 Target，不执行真实 Host 安装验收，也不把 Standalone 当成发布证据。

## 13. 分阶段实施细则

### G0：产品、数学与数值基线

- 冻结轴向、四象限布局、对称扩展、舍入、裁切、层数和资源预算。
- 为 Haar/CDF 5/3 建立手算与独立参考 Golden，记录来源和容差理由。
- 冻结去噪指标、无参考结论边界、水印公平比较定义和报告字段。
- 记录 304/304 起始测试基线，不修改插件注册和 UI。

### G1：领域模型与金字塔布局

- 建立稳定 ID、不可变配方、层描述符、子带矩形、结果和校验。
- 先证明 packed 布局、扩展尺寸、溢出检查和所有权，再写变换循环。
- 所有异常使用清晰中文消息，Domain 不引用 Avalonia/JSON/文件系统。

### G2：Haar 数值核心

- 完成 1D 行/列步骤、2D 正逆、多级 LL 递归、奇数尺寸扩展和裁剪。
- 通过方向、重建、能量、确定性和取消门禁。
- 暂不接 UI、去噪、水印或 Image Fingerprint。

### G3：CDF 5/3

- 用相同契约增加第二个策略，固定 lifting 和边界协议。
- 通过正逆、方向、奇偶尺寸和交叉策略隔离测试。
- 若 CDF 5/3 需要修改 Haar 代码分支，先重新审查接口是否泄漏实现细节。

### G4：投影、探针与逐级重建

- 完成线性/对数/对称系数投影、能量统计和系数探针。
- 完成当前层、逐级和完整重建，并输出 double 误差与 byte 裁切诊断。
- 投影器不得回写金字塔，显示归一化不得进入配方数学指纹。

### G5：阈值去噪和质量扫描

- 完成 Hard/Soft、MAD、Universal 建议、目标层/子带和单次结果。
- 复用 `FullReferenceQualityAnalyzer`，不复制 PSNR/SSIM 公式。
- 完成 60 案例上限、取消、部分结果和有/无参考结论边界。

### G6：用例、Session 和导出

- 接入 `IImageCodec`、分析代理、原子写入和窄报告端口。
- 分离代理与完整尺寸执行；结果绑定配方指纹。
- PNG 与 JSON/CSV 先编码到内存或临时文件，再通过现有原子写入发布。

### G7：DWT 水印与 DCT 对比

- 先冻结 DWT carrier ID、槽位、差分 QIM、容量和读取置信度。
- 用 adapter 复用 DCT 能力，用既有 Robustness 算子执行共同扰动集合。
- 完成公平性、协议回归、报告和结论限制测试后才允许接 UI。

### G8：Document、持久化和组合根

- 新增稳定 ID、服务登记和第十个 Persistable Document。
- 完成轻量快照、恢复、关闭取消、多 Scope 隔离和 stale 结果保护。
- 更新 Module 注册数量测试与公共架构文档，但不新增 Tool/AIFLOW。

### G9：UI、Standalone 与解释

- 实现参数区、金字塔、重建/残差、扫描图表和水印矩阵。
- 增加空态、错误态、取消态、代理/完整尺寸标识、键盘和表格等价信息。
- Standalone 只扩展现有承载入口，继续使用真实 Module/DI/View。

### G10：质量加固和本地封板

- 完成数值、资源、取消、架构、持久化、Headless 和既有能力回归。
- 复跑 Debug/Release locked 本地门禁并记录准确通过数、耗时、警告和跳过数。
- 补齐专用文档与历史记录；不执行 Windows CI、ZIP、真实 Host 和发布封板。

## 14. 文档同步清单

实施时按现有能力惯例建立 `docs/design/wavelet-lab/` 专用文档集：

- `README.md`：能力入口、已实现闭环、阅读顺序和边界。
- `implementation.md`：本文；逐包更新真实状态与证据。
- `mathematical-principles.md`：Haar、CDF 5/3、轴向、边界、阈值、DWT QIM 和指标。
- `testing.md`：测试命令、Golden、实际通过数、性能观察和未证明结论。
- `guide.md`：稳定 ID、参数、状态机、限制、错误和开发复用边界。
- `user-manual.md`：不要求数学背景的载图、分解、重建、去噪和比较步骤。
- `report-schema.md`：去噪扫描与 DCT/DWT 对比 JSON/CSV 字段和版本。
- `history/README.md` 与 `history/g0...g10`：每包实际改动、偏差、证据、风险和回滚。

同时同步根 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md`、
`docs/design/shared/project-and-window-responsibilities.md` 以及与 wHash 相关的感知指纹文档。只有 G10 通过后，才能把
“计划中”改为“V1 已实现”；发布资料继续保持“发布门禁延期”。

## 15. 完成定义

只有同时满足以下条件，Wavelet Lab V1 才能在开发阶段标记为完成：

1. 用户能在一个真实 Persistable Document 中完成分解、子带观察、逐级重建、阈值去噪、质量扫描和 DCT/DWT 水印对比。
2. Haar 与 CDF 5/3 的正逆、方向、边界、多层、所有权和取消全部通过数值门禁。
3. 代理与完整尺寸、基线与阈值结果、当前与 stale 结果在状态和 UI 上都不会混淆。
4. 有参考与无参考的质量结论严格区分，水印对比满足共同 Payload 和共同扰动规则。
5. Domain/Application/Infrastructure/Feature 依赖方向符合 SOLID，没有万能服务、通用 DAG 或不必要的模式层级。
6. 生产代码的重要数学、边界、生命周期和取舍均有详细中文注释。
7. 所有新增与既有测试在 Debug/Release 下零失败、零跳过，构建零警告、零错误。
8. 专用文档、实施历史、根索引和未来能力状态全部与实际代码一致。
9. 没有引入 AIFLOW、Windows CI 或发布阶段门禁。

上述完成定义仍然只代表本地开发封板。真实 Host、ZIP、安装升级、Windows CI 和发布验收留到用户明确进入发布阶段时执行。
