# ImageLabPlugin V1 Palette And Color Transfer／调色板与颜色迁移设计与实施计划

> 计划状态：V1 生产实现与本地自动门禁完成；G0–G9 实证见 `history/`，发布阶段仍延期<br>
> 基线日期：2026-08-31<br>
> 产品名称：Palette And Color Transfer／调色板与颜色迁移<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 自动证据：起始 479/479；2026-08-31 封板复跑 locked restore，Debug/Release warn-as-error build 均为 0 警告、0 错误；两配置 test 均为 520/520 通过、0 失败、0 跳过<br>
> 核心路线：标准 sRGB/HSV/CIELAB 数值协议 + Alpha 加权颜色统计 + 确定性加权 Lab 聚类 + CIELAB 均值/标准差迁移 + 固定调色板最近色重映射 + 直方图/色域/ΔE 诊断<br>
> 首要规定：SOLID 是所有实现取舍的第一约束；设计模式只用于真实变化点并保持朴素；新增生产代码使用详细中文注释解释颜色约定、单位、边界、所有权和设计思路；不使用 AIFLOW；不新增 Windows CI；本阶段不执行 ZIP、真实 Host 或任何发布门禁

本文既是 ImageLab 第十四项产品能力、第十五个多实例 Persistable Document 的冻结设计基线，也记录实际落地差异。
产品用于观察图片中的颜色统计、
颜色空间、主色聚类、参考图颜色迁移和固定调色板量化误差。

它不是通用调色面板，也不是照片美化器。V1 不提供曲线、色轮、局部蒙版、画笔、滤镜链、LUT 调色、自动审美评分或
“一键变好看”结论；所有处理都必须能说明输入、数学规则、统计变化、色域裁切和误差。

## 1. 决策摘要

### 1.1 产品形态

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Palette And Color Transfer／调色板与颜色迁移` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID | `myavalonia.plugin.image.lab.document.palette-color-transfer` |
| 显示名称 | `调色板与颜色迁移` |
| 显示分类 | `图像分析` |
| 输入 | 用户显式选择的一张目标图；统计迁移时再显式选择一张参考图；两图尺寸可以不同 |
| 输出尺寸 | 始终等于目标图尺寸；Alpha 逐字节保持；不隐式缩放、裁剪或对齐参考图 |
| 颜色协议 | 非预乘 RGBA8888；sRGB IEC 61966-2-1 传递函数；XYZ D65；CIELAB；标准 HSV |
| 主色提取 | 5-bit RGB 聚合后的确定性、Alpha 加权、Lab 空间 k-means；`k=2..12`，默认 6 |
| 调色板排序 | 占比、L* 明度、HSV 色相三种固定排序；排序只改显示顺序，不改变聚类或重映射 |
| 颜色迁移 | 目标图 CIELAB 各通道按参考图均值/标准差匹配；强度 `0..1`；可选择保留目标 L* |
| 固定调色板 | 从目标图或参考图提取后显式“冻结”；目标图每个可见像素按 Lab `ΔE76` 最近色重映射 |
| 分布视图 | RGB、HSV、Lab 一维直方图；HSV H-S 与 Lab a*-b* 有界二维密度/色域图 |
| 感知误差 | 目标—结果逐像素 CIEDE2000 的均值、P50、P95、最大值和分布；另复用 PSNR/SSIM |
| 统计贴近度 | 目标/结果相对参考图的 Lab 均值残差、标准差残差和分通道 Jensen-Shannon 距离 |
| 导出 | 当前完整尺寸结果 PNG；版本化 JSON/CSV 实验报告；不覆盖源文件 |
| 外部依赖 | V1 不新增 NuGet、原生颜色库、GPU 或图表框架 |
| 模式使用 | 不可变值对象、普通 sealed 数值服务、一个真实变化点的 Strategy、窄用例、构造注入 |
| 明确排除 | AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP、真实 Host 与发布门禁 |

### 1.2 用户闭环

```text
显式选择目标图
    ↓
查看目标图 RGB / HSV / Lab 统计、色域密度和确定性主色调色板
    ↓
显式选择参考图（颜色迁移需要；固定调色板重映射也可只使用目标图）
    ↓
比较目标/参考的直方图、Lab 统计和调色板，不要求两图同尺寸
    ↓
选择“完整 Lab”或“保留目标 L*”，设置迁移强度并运行颜色统计迁移
    ↓
联动查看目标/参考/结果、迁移前后直方图、a*-b* 色域、ΔE 和色域映射诊断
    ↓
或从目标/参考提取并冻结 K 色调色板，再对目标图执行最近色重映射
    ↓
查看调色板占比、每簇误差、量化 ΔE、PSNR/SSIM 和像素探针
    ↓
按需导出完整尺寸 PNG 或不含像素与绝对路径的 JSON/CSV 实验报告
```

### 1.3 固定实施顺序

1. G0 冻结产品语义、颜色协议、Alpha 规则、资源预算、Golden 数据和起始门禁；
2. G1 建立 sRGB、XYZ D65、CIELAB、HSV、色域映射和 CIEDE2000 数值核心；
3. G2 建立加权统计、直方图、二维密度、确定性采样和分布距离；
4. G3 完成确定性加权 Lab 聚类、主色提取、排序和调色板冻结；
5. G4 完成 CIELAB 统计迁移、强度混合、零方差规则和色域诊断；
6. G5 完成固定调色板重映射、量化误差、探针和完整尺寸结果；
7. G6 完成 Session、窄用例、取消/generation、报告和 PNG 原子导出；
8. G7 接入第十五个 Persistable Document、快照、DI、Module 和 Standalone；
9. G8 完成可访问 UI、专用文档和有限人工验收；
10. G9 复跑 Debug/Release 全量本地门禁，完成本地开发封板。

不得先在 View 或 Document 中写 RGB 循环，再把它称为颜色迁移。颜色常量、白点、取值范围、色相无定义、
Alpha 权重、聚类确定性、零方差和色域映射必须先在 Domain 中形成协议并通过自动测试。

## 2. 当前项目事实与复用边界

### 2.1 已验证基线

仓库当前具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集，以及只供本地开发的 `ImageLabPlugin.Standalone`；
- 十三项已实现产品能力、十四个多实例 Persistable Document；没有 Tool、Workflow Action 或 Workbench Command；
- 自有非预乘 RGBA8888 `PixelImage`、16,000,000 像素上限和 64 MiB 编码输入上限；
- `ImageAreaResampler`、`ImageAnalysisProxyProjector` 和小图不放大的面积抗混叠缩放；
- R/G/B/Y/Cb/Cr 通道、BT.601 全范围 YCbCr、六通道 256-bin 直方图；
- `FullReferenceQualityAnalyzer` 的 MAE、RMSE、PSNR-Y/RGB、全局 SSIM-Y 和 Alpha 独立统计；
- PNG/JPEG 解码、PNG 编码、原子写入、文件对话框、Document Scope、取消、generation 和 Bitmap 释放惯例；
- 2026-08-31 实跑 locked restore、Debug/Release 0 警告/0 错误、两配置 479/479 测试通过且 0 跳过。

本轮已经新增 HSV、XYZ、CIELAB、sRGB 线性化、CIEDE2000、颜色聚类、颜色迁移、固定调色板重映射、
报告和第十五个 Document。479/479 仅是实施前起点；当前完成证据为两配置 520/520，详见第 23 节与 `history/`。

### 2.2 必须直接复用

- 图片解码、PNG 编码、原子发布和文件对话框继续使用现有端口，不复制文件 IO；
- 完整尺寸目标/参考图片继续使用 `PixelImage`，不建立第二种 RGBA 容器；
- 分析代理继续委托 `ImageAreaResampler`；颜色工具只建立自己的 256/512 档位策略，不修改既有白名单；
- 目标—结果的 PSNR、SSIM、RGB/Alpha 误差继续使用 `FullReferenceQualityAnalyzer`；
- 需要显示 R/G/B/Y/Cb/Cr 兼容直方图时复用既有统计语义；HSV/Lab 使用新的专用加权直方图，不能塞入六通道枚举；
- Standalone 必须从真实 Module/DI 解析真实 Document 和 View，不复制演示业务；
- 无状态颜色数学和分析服务登记为 singleton；目标图、参考图、结果、聚类结果、Bitmap 和取消源归 Document Scope 独占。

### 2.3 允许的共享改进

颜色空间转换将成为多个未来能力可能复用的基础，因此允许在 `Domain/Imaging` 增加窄而稳定的：

```text
SrgbColorSpace
  只负责 sRGB 编码值、线性 RGB 与 XYZ D65 的双向转换

CieLabColorSpace
  只负责 XYZ D65 与 CIELAB 的双向转换，并公开固定白点协议 ID

HsvColorSpace
  只负责 sRGB 与 HSV 的双向转换，以及灰阶色相 N/A 语义

CieDeltaE
  只负责 ΔE76 与 CIEDE2000，不依赖任何 Document 或调色板模型
```

如果类型只被本工具使用，先放在 `Domain/ColorTransfer`；只有经两个真实消费者证明稳定后，才移动到共享目录。
不得因为“以后可能复用”而先建立通用色彩管理框架、ICC Profile 抽象或算法注册中心。

### 2.4 禁止的错误复用

- 不把现有 YCbCr 命名为 Lab，也不使用 BT.601 亮度代替 CIE L*；
- 不用 `System.Drawing.Color`、Avalonia `Color` 或 Skia 类型作为领域颜色协议；
- 不把 Image Compare 的六通道直方图强行扩成混合不同量纲的万能直方图；
- 不让新 Document 持有或调用 Image Compare、SVD 或其他 Document；只复用其无状态领域服务；
- 不在 Feature 中实现 Gamma、矩阵变换、k-means、ΔE、统计迁移或像素循环；
- 不把参考图偷偷缩放到目标尺寸，也不做像素配对式“颜色迁移”；
- 不建立通用 Pipeline、Mediator、Event Bus、Repository、反射算法目录、DAG 或脚本层；
- 不为只有一个实现的 CIEDE2000、统计迁移器或色域映射器提前制造接口与工厂。

## 3. V1 范围与明确非目标

### 3.1 V1 必须完成

- 目标图必选、参考图可选；两图允许不同尺寸，输出始终与目标图同尺寸；
- RGB、HSV、CIELAB 三种颜色空间的一维分布和准确量纲说明；
- HSV H-S 与 Lab a*-b* 的有界二维密度视图，附等价数值范围、样本数和最大 bin；
- 目标/参考/结果的颜色均值、标准差、分位数、有效 Alpha 权重和色域事实；
- 目标图或参考图的 `k=2..12` 主色提取、簇占比、簇内误差和三种调色板排序；
- 确定性初始化、稳定 tie-break、最大迭代、收敛阈值和未收敛结构化状态；
- CIELAB 独立通道均值/标准差迁移，完整 Lab 与保留目标 L* 两种明确模式；
- `0..1` 迁移强度；`0` 必须逐字节等于目标图，`1` 表示完整应用冻结公式；
- 参考通道或目标通道零方差、非有限数、无可见像素和色域外颜色的可解释处理；
- 从当前目标或参考聚类结果显式冻结调色板，再对目标图执行 `ΔE76` 最近色重映射；
- 目标—结果逐像素 CIEDE2000 分布、PSNR/SSIM、改变像素数、色域映射数和最大映射距离；
- 目标/结果相对参考图的 Lab 统计残差与分通道 Jensen-Shannon 距离；
- 像素探针显示目标/参考（各自坐标）、结果的 sRGB/HSV/Lab、调色板索引和 ΔE；
- 异步运行、取消、generation 防迟到、参数变更使结果过期、多实例隔离和关闭释放；
- 完整尺寸结果 PNG、JSON/CSV 报告、轻量快照、中文错误与限制说明；
- Debug/Release locked restore、warn-as-error build、全部自动测试、0 跳过和文档同步。

### 3.2 V1 明确不实现

- 通用亮度、曝光、对比度、曲线、色阶、HSL 色轮、白平衡、局部画笔或区域蒙版；
- LUT/ICC Profile 导入导出、显示器校色、CMYK、HDR、线性浮点图片、广色域 P3/Rec.2020；
- 直方图规定化、最优传输、协方差白化/着色、局部颜色迁移、语义分割或深度学习风格迁移；
- 把当前方法直接命名为 Reinhard `lαβ` 算法；V1 使用的是明确记录的 CIELAB 独立通道统计匹配；
- 手工增删、拖拽编辑或任意取色器构建调色板；V1 的固定调色板来自可复现提取后显式冻结；
- Floyd–Steinberg、蓝噪声或其他抖动；V1 先观察无抖动最近色量化误差；
- 自动选择“最佳 k”“最佳迁移强度”或“最漂亮结果”；
- 文件夹批处理、图片队列、覆盖源文件、JPEG/WebP/AVIF 结果导出；
- 无界散点图、为每个像素创建对象、每次滑块变化立即重跑完整 16 MP 图片；
- AIFLOW、工作流节点、Workflow Action、Workbench Command、脚本或宏；
- Windows CI、真实 Host、ZIP、安装/升级/卸载和任何发布门禁。

### 3.3 解释边界

- 主色是当前量化、Alpha 权重、k 和 Lab 距离协议下的聚类中心，不是图片唯一或客观的“真正颜色”；
- 簇占比描述可见像素权重，不等于语义对象面积；半透明像素按 Alpha 权重计入；
- 颜色迁移只匹配全局一阶/二阶独立通道统计，不理解主体、天空、肤色或区域对应关系；
- 迁移后 Lab 均值/标准差接近参考图，不表示内容相似、审美更好或色彩科学上更准确；
- ΔE00 是目标与结果的逐像素感知色差近似；它不适用于不同尺寸目标/参考的逐像素比较；
- PSNR/SSIM 对强烈但有意的调色可能很低，只表达与原目标的差异，不判断结果好坏；
- sRGB 色域映射会使理论 Lab 统计无法完全达到参考值，必须显示映射数量和残差；
- HSV 的 Hue 在低饱和/灰阶颜色上无定义；不得把 Hue=0 伪装成“红色样本”。

## 4. 颜色与 Alpha 数值协议

### 4.1 像素、范围和透明度

- 输入为非预乘 RGBA8888；R/G/B 是 sRGB 编码字节，Alpha 不进入颜色空间变换；
- `A=0` 像素不进入颜色统计、聚类或误差分布，处理结果保持其 RGBA 四字节不变；
- `0<A<255` 的像素以 `w=A/255` 进入统计与聚类，执行处理时仍计算完整目标颜色并原样保留 Alpha；
- `A=255` 权重为 1；有效权重和有效像素数必须分别报告；
- 若总有效权重小于冻结的数值下限，返回 `NoVisiblePixels`，不能除零或生成黑色结果；
- 目标和参考的 Alpha 规则完全相同；报告保存 Alpha 协议 ID，避免后续版本静默改变统计。

### 4.2 sRGB 与线性 RGB

将字节归一化为 `c ∈ [0,1]`，固定 IEC sRGB 分段解码：

```text
c_linear = c / 12.92                              , c <= 0.04045
c_linear = ((c + 0.055) / 1.055)^2.4              , c > 0.04045
```

反向编码使用阈值 `0.0031308`。领域内部颜色为 double，只有最终 `PixelImage` 投影时按
`MidpointRounding.ToEven` 舍入到字节。不得对 sRGB 字节直接套 XYZ 矩阵。

### 4.3 XYZ D65 与 CIELAB

- 线性 RGB 到 XYZ 使用标准 sRGB D65 矩阵；XYZ 范围以 `Y=1` 为白色尺度；
- D65 参考白固定为 `(Xn, Yn, Zn)=(0.95047, 1.00000, 1.08883)`；
- Lab 使用 CIE 分段函数，`δ=6/29`；L* 理论范围 `[0,100]`，a*/b* 计算时保持 double；
- 反向 Lab→XYZ→线性 RGB 在进入色域映射前不得提前 clamp；
- 颜色协议 ID 冻结为 `srgb-d65-cielab-v1`，矩阵常量和阈值必须集中定义并有 Golden 测试；
- 不做 Bradford 白点适配，因为 V1 输入、参考、显示和输出都固定为 sRGB D65。

### 4.4 HSV

- H 使用角度 `[0,360)`；S、V 使用 `[0,1]`；
- 当 `max(R,G,B)-min(R,G,B)` 小于冻结 epsilon 时，Hue 状态为 `Undefined`；
- Hue 直方图只累计 Hue 有定义的 Alpha 权重，同时显示“无色相权重”；
- 二维 H-S 图不把灰阶压到 H=0；灰阶在独立的 achromatic 计数中显示；
- Hue 平均值若需要展示，必须使用加权圆统计，不允许普通算术平均跨越 0/360 边界。

### 4.5 sRGB 色域映射

统计迁移和 Lab 聚类中心可能落在 sRGB 色域外。V1 使用一个确定性的色度压缩映射：

1. 保留 L*；把 `(a*,b*)` 写成 chroma `C*` 与 hue；
2. 若直接转换后的线性 RGB 全部在 `[0,1]`，不映射；
3. 否则在 `[0,C*]` 上固定次数二分，寻找保持同一 L*、同一 hue 的最大可表示 chroma；
4. 若 L* 自身超界，先裁到 `[0,100]` 并记录 lightness clipping；
5. 最终只为浮点舍入误差做极小 clamp，再编码为 sRGB 字节。

报告必须区分“无需映射”“色度压缩”“L* 裁切”，并记录映射前后 `ΔE76`。不能简单逐通道 RGB clamp 后隐藏色相偏移。

## 5. 统计、直方图与可视化协议

### 5.1 统计扫描

- 对完整解码图片按行扫描，使用 double Alpha 权重；每行检查取消；
- 均值和方差使用加权在线算法或补偿求和，禁止平方和减均值平方造成大数相消；
- 保存 RGB、HSV、Lab 的均值/标准差；Hue 使用圆统计并同时报告有效圆集中度；
- L*、a*、b* 保存 P05/P50/P95；分位数从冻结直方图近似，不常驻每像素 double 数组；
- 统计结果不可变，并携带像素数、有效像素数、有效权重、颜色协议 ID 和 Alpha 协议 ID。

### 5.2 一维直方图

| 空间 | 通道 | V1 bin 协议 |
| --- | --- | --- |
| RGB | R/G/B | 256 bin，字节精确；bin 值为 Alpha 权重 double |
| HSV | H | 180 bin，每 bin 2°；只累计 Hue 有定义样本 |
| HSV | S/V | 100 bin，覆盖 `[0,1]` |
| Lab | L* | 100 bin，覆盖 `[0,100]` |
| Lab | a*/b* | 256 bin，显示范围 `[-128,128)`；范围外进入独立 under/overflow 计数 |
| ΔE00 | 目标—结果 | 100 个固定非均匀/封顶 bin，超上限进入 overflow，原始汇总仍保存真实最大值 |

直方图提供线性/对数显示，但显示缩放不改变统计。目标、参考和结果可叠加，图例、线型和数值表必须同时存在。

### 5.3 二维密度与色域

- HSV 使用 180×100 的 H-S 加权密度；Hue 无定义样本单独列出；
- Lab 使用 128×128 的 a*-b* 加权密度，显示范围 `[-128,128)`；
- 二维网格保存 double 权重，不保存每像素对象；
- UI 允许选择目标、参考、结果或三者轮廓叠加；
- 每个视图显示样本权重、非空 bin、最大 bin 和超界数；
- “色域”在 V1 表示当前图片在选定二维颜色坐标的占用分布，不冒充设备 ICC gamut volume。

### 5.4 分布贴近度

颜色迁移前后相对参考图，计算：

```text
meanResidual = || μ_candidate(Lab) - μ_reference(Lab) ||₂
stdResidual  = || σ_candidate(Lab) - σ_reference(Lab) ||₂
JSD_channel  = sqrt(0.5 * KL(P||M) + 0.5 * KL(Q||M))
M            = 0.5 * (P + Q)
```

Jensen-Shannon 使用归一化的 L*/a*/b* 固定 bin 和自然对数；双方为零的 bin 贡献 0。界面明确称为“直方图距离”，
不称为感知色差或图片相似度。

## 6. 主色提取与固定调色板

### 6.1 有界颜色聚合

为避免在 16 MP 图片上为每个像素创建 Lab 对象，先建立固定 32×32×32 的 5-bit RGB 聚合表：

- 每个 cell 保存 Alpha 权重、加权 R/G/B 和有效像素数；
- cell 索引固定为 `r5<<10 | g5<<5 | b5`，tie-break 始终使用较小索引；
- 非空 cell 的代表色来自该 cell 实际像素的加权平均 sRGB，而不是 bin 几何中心；
- 再把最多 32,768 个代表色转换为 Lab，作为 k-means 输入；
- 聚合表大小固定，内存不随图片像素数增长；结果记录 5-bit 协议和量化前有效权重。

### 6.2 确定性加权 k-means

V1 只提供一个经过测试的聚类器，不提前建立算法插件系统：

1. 第一个中心选择权重最大的 cell；并列取最小 cell 索引；
2. 后续中心选择 `weight × nearestDistance²` 最大的 cell；并列仍取最小索引；
3. 分配距离为 Lab `ΔE76²`；并列选择较小 cluster index；
4. 更新中心为簇内 Lab 加权均值；
5. 空簇用当前加权误差最大的 cell 重新播种，不复制另一个中心；
6. 最大 64 次迭代；所有分配不变或最大中心位移 `<0.05 ΔE76` 时收敛；
7. 输出中心按稳定 cluster identity 保存，显示排序另做投影；
8. 未收敛返回可观察结果和 `IterationLimitReached` 诊断，但不得静默标成成功。

### 6.3 调色板项

每个不可变 `PaletteEntry` 至少包含：

- 稳定 cluster index；
- 映射后的 sRGB 与原始 Lab 中心；
- Alpha 加权占比和有效像素数；
- 簇内加权 SSE、平均 `ΔE76` 和最大 `ΔE76`；
- 色域映射状态与中心映射距离；
- HSV Hue 状态、L* 和用于排序的明确键。

### 6.4 排序与冻结

- `PopulationDescending`：占比降序，tie-break 为 cluster index；
- `LightnessAscending`：L* 升序，再按 Hue、cluster index；
- `HueAscending`：有定义 Hue 升序，灰阶放最后，再按 L*、cluster index；
- 排序只生成只读视图，不改写 cluster identity；
- 用户必须从“目标调色板”或“参考调色板”显式冻结；冻结记录来源图片 fingerprint、k、聚类协议和颜色列表；
- 改变来源、k 或聚类协议使已冻结调色板过期；仅改变排序不使冻结颜色过期；
- V1 不允许手工改色，避免把工具扩成通用调色面板。

## 7. CIELAB 颜色统计迁移

### 7.1 冻结公式

对目标像素 Lab 通道 `x_c`，目标统计 `(μt_c, σt_c)`，参考统计 `(μr_c, σr_c)`：

```text
mapped_c = μr_c + (x_c - μt_c) * σr_c / σt_c
result_c = (1 - strength) * x_c + strength * mapped_c
```

- `strength ∈ [0,1]`；领域边界拒绝越界，UI 不静默裁切；
- 完整模式处理 L*、a*、b*；“保留目标 L*”模式只处理 a*、b*，L* 逐像素保持目标值；
- 目标 `σt_c < 1e-9` 时，受处理通道的 `mapped_c=μr_c`，并记录 `CollapsedTargetVariance`；
- 参考 `σr_c < 1e-9` 时，该通道收敛到参考均值，并记录 `CollapsedReferenceVariance`；
- 任何输入统计或中间结果非有限时结构化失败，不返回部分图片；
- Lab 结果经过第 4.5 节色域映射后转成 sRGB；Alpha 原样保留；
- `strength=0` 走明确的目标图 clone 快路径，保证逐字节一致且色域映射数为 0。

### 7.2 运行语义

- 统计使用目标/参考完整图片；不要求同尺寸；
- 每次运行从原目标图开始，不以上一次迁移结果为输入，防止无意累计；
- 参数滑块只更新草案和结果过期状态；通过显式运行或防抖后的用户命令执行完整尺寸处理；
- 运行在后台完成，每行检查取消；取消不返回半结果；
- 输出必须保存配方 fingerprint：颜色协议、Alpha 协议、模式、强度、目标/参考内容 fingerprint；
- 若目标或参考被替换，旧结果、误差、导出资格和探针全部失效。

### 7.3 不作出的承诺

- 独立通道均值/标准差忽略 Lab 通道协方差，不能保证完整联合分布匹配；
- CIELAB 并非原论文常用的 `lαβ` 去相关空间，文档必须使用准确名称；
- 全局统计无法保持局部对象颜色，对肤色、天空或多模态分布可能产生不自然结果；
- 色域映射和 8-bit 量化会改变理论统计，验收应比较冻结容差与诊断，不要求逐项数学完全相等。

## 8. 固定调色板重映射与量化误差

### 8.1 最近色重映射

- 输入必须是第 6.4 节有效冻结调色板；调色板包含 2..12 色；
- 对目标图每个 `A>0` 像素转换到 Lab，计算到各原始调色板 Lab 中心的 `ΔE76²`；
- 取最小距离；并列使用较小稳定 cluster index，而不是当前显示顺序；
- 输出写入该调色板项已映射到 sRGB 的颜色，Alpha 原样保留；`A=0` 的 RGBA 完全不变；
- 扫描顺序固定为行优先，每行取消；不使用无界并行或每像素 LINQ；
- V1 使用精确 K 次距离计算，不使用会引入额外误差但未披露的 3D LUT。

### 8.2 量化诊断

- 每个调色板项记录被映射像素数、Alpha 权重、平均/最大 `ΔE76` 与平均 `ΔE00`；
- 全局记录 CIEDE2000 均值、P50、P95、最大值和固定直方图；
- 复用 `FullReferenceQualityAnalyzer` 记录 PSNR-Y/RGB、全局 SSIM-Y、MAE/RMSE 和改变像素数；
- 记录使用色数、未使用色、最大簇、各项输出占比和颜色协议；
- 生成有界误差热力图时使用固定 ΔE00 标尺，并提供等价数值图例；
- 不把低误差称为“无损”，不把高 PSNR 称为“最佳调色板”。

## 9. SOLID 分层与朴素模式

### 9.1 分层职责

```text
Features/PaletteColorTransfer
  只负责命令、可观察状态、Avalonia Bitmap、视图交互和中文呈现
              ↓ 依赖应用端口
Application/ColorTransfer
  只编排载图、分析、迁移、重映射、探针和导出用例
              ↓ 依赖领域模型与外部端口
Domain/ColorTransfer + Domain/Imaging
  只负责颜色数学、统计、聚类、迁移、量化和不可变结果
              ↑ 被基础设施实现端口
Infrastructure/ColorTransfer
  只负责严格报告序列化；图片与原子文件继续复用既有 Infrastructure
```

### 9.2 SOLID 门禁

- **SRP**：颜色转换、ΔE、统计、聚类、迁移、量化、序列化和 Document 分开；禁止 `ColorService` 巨型类；
- **OCP**：只有已确认的调色板来源/排序和运行操作用枚举或窄 Strategy 扩展；不靠修改 Document 大 switch 添加算法；
- **LSP**：若引入 `IColorOperation`，所有实现都必须遵守输入不变、Alpha 保持、取消无半结果和完整诊断契约；
- **ISP**：用例接口按“准备、分析、迁移、重映射、探针、导出”拆分；View 不依赖万能工作台接口；
- **DIP**：Application 依赖 `IImageCodec`、`IAtomicFileWriter`、文件对话框和报告端口，不依赖 Avalonia/文件系统具体类；
- Domain 项目代码不得引用 Avalonia、Plugin SDK、JSON、文件路径对话框、Dispatcher 或 DI 容器。

### 9.3 允许使用的模式

- 不可变值对象：颜色、统计、调色板、配方 fingerprint 和诊断；
- Strategy：仅当统计迁移与调色板重映射需要被同一个运行用例按明确操作类型选择时使用两个窄实现；
- Session：每个 Document Scope 持有目标/参考/结果及其所有权；
- Adapter：既有图片 codec、Bitmap 和原子文件端口；
- Null Object、Service Locator、反射注册、抽象工厂、通用命令总线均不引入。

如果一个接口只有一个实现且没有测试替身或边界价值，优先使用普通 sealed 类。设计模式必须减少耦合，不能只增加文件数量。

## 10. 领域模型、应用用例与所有权

### 10.1 建议核心模型

```csharp
internal readonly record struct SrgbColor(double Red, double Green, double Blue);
internal readonly record struct LinearRgbColor(double Red, double Green, double Blue);
internal readonly record struct XyzD65Color(double X, double Y, double Z);
internal readonly record struct CieLabColor(double L, double A, double B);
internal readonly record struct HsvColor(double HueDegrees, double Saturation, double Value, HueStatus HueStatus);

internal sealed record ColorStatistics(...);
internal sealed record ColorDistributionSnapshot(...);
internal sealed record PaletteExtractionRecipe(int ColorCount, PaletteSource Source);
internal sealed record PaletteEntry(...);
internal sealed record ExtractedPalette(...);
internal sealed record FrozenPalette(...);
internal sealed record ColorTransferRecipe(ColorTransferMode Mode, double Strength);
internal sealed record ColorTransferResult(...);
internal sealed record PaletteRemapResult(...);
internal sealed record PerceptualDifferenceReport(...);
```

公共构造边界校验有限数、范围、bin 数、权重和数组长度；集合进入模型时复制或转成只读拥有值。不得把可写数组、
Avalonia `Color`、Bitmap 或源文件 Stream 暴露为领域结果。

### 10.2 建议无状态服务

- `SrgbColorSpace`：sRGB/linear RGB/XYZ D65；
- `CieLabColorSpace`：XYZ D65/CIELAB；
- `HsvColorSpace`：sRGB/HSV 与 Hue N/A；
- `CieDeltaE`：ΔE76、CIEDE2000；
- `SrgbGamutMapper`：固定色度压缩与诊断；
- `ColorDistributionAnalyzer`：统计、一维/二维直方图和分布距离；
- `RgbColorAggregator`：32³ 有界聚合；
- `DominantColorClusterer`：确定性加权 k-means；
- `PaletteSorter`：三种显示排序；
- `LabStatisticsTransfer`：第 7 节公式；
- `FixedPaletteRemapper`：第 8 节精确最近色；
- `PerceptualDifferenceAnalyzer`：ΔE00 汇总与热力图源数据；
- `ColorPixelInspector`：按各自图片坐标生成数值事实。

### 10.3 窄应用用例

```text
IPrepareColorTransferSessionUseCase
  解码目标或参考，创建有界显示代理与内容 fingerprint，不自动运行算法

IAnalyzeColorDistributionsUseCase
  生成目标/参考的完整统计、直方图、二维密度和提取调色板

IFreezePaletteUseCase
  从已完成且 fingerprint 匹配的提取结果建立不可变固定调色板

IRunColorTransferUseCase
  从原目标和原参考执行一次完整尺寸统计迁移并生成诊断

IRemapToPaletteUseCase
  从原目标和有效冻结调色板执行一次完整尺寸最近色重映射并生成量化诊断

IInspectColorPixelUseCase
  返回目标/参考/结果对应坐标的颜色空间数值、调色板归属和 ΔE

IExportColorResultUseCase / IExportColorReportUseCase
  PNG 编码回读或严格 JSON/CSV 序列化，再通过原子端口发布
```

### 10.4 Session 与并发

- `ColorTransferSession` 归一个 Document Scope 独占，并实现 `IDisposable`；
- Session 拥有目标/参考 `PixelImage`、显示代理、分布、提取调色板、冻结调色板、当前结果和轻量 fingerprint；
- 无状态服务不缓存 Document 数据；不得用 singleton 字典按路径保存图片；
- 载入目标、载入参考、分析、迁移、重映射各自使用 generation；新操作取消相同通道的旧操作；
- 迟到成功、迟到异常和关闭后返回都不得覆盖新状态；
- 配方变化推进 revision、清除导出资格并标记结果过期；纯显示空间、排序、图表缩放不标脏；
- 完整结果长期最多保留原目标、参考和一个当前输出；替换时先交换后释放旧 Bitmap/Session；
- 取消只丢弃当前临时数组，不修改已提交结果；失败后保留输入和上一次有效结果但明确标为与当前配方不匹配。

## 11. Document、UI 与交互计划

### 11.1 Document 生命周期

- 新增 `PaletteColorTransferDocument : IPersistablePluginDocument, IDisposable`；
- Stable ID 使用 1.1 节固定值；Module 完成后应恰好登记十五个唯一 Persistable Document；
- Document 不直接访问文件系统、JSON、图片像素算法或颜色矩阵；
- 命令状态必须区分：缺目标、缺参考、分析中、缺有效调色板、结果过期、可导出、已关闭；
- 两个 Document Scope 的路径、图片、调色板、结果、取消、generation 和 Bitmap 完全隔离；
- Standalone 增加真实入口，但不复制颜色服务或模拟 ViewModel。

### 11.2 建议布局

```text
┌ 目标图 [选择]  参考图 [选择]  [分析颜色]  状态/限制 ┐
├ 左：操作配方 ─┬ 中：图片比较 ─────────────┬ 右：统计摘要 ┤
│ K、来源、排序  │ 目标 / 参考 / 当前结果      │ 均值/标准差  │
│ [提取][冻结]   │ 同步缩放仅作用于各自视图    │ ΔE/PSNR/SSIM │
│ 迁移模式/强度  │ 不做目标—参考像素对齐       │ 色域映射     │
│ [运行迁移]     │ 像素探针与坐标事实           │ 过期/协议 ID │
│ [调色板重映射] │                             │              │
├ 调色板 ───────┴─────────────────────────────┤
│ 色块 + Hex + Lab + HSV + 占比 + 簇误差 + 等价表格          │
├ RGB / HSV / Lab / ΔE 标签页 ─────────────────┤
│ 一维直方图；H-S 或 a*-b* 密度；目标/参考/结果叠加和图例      │
└ [导出 PNG] [导出 JSON] [导出 CSV] [帮助] ───┘
```

### 11.3 专用控件

- `PaletteStripControl`：绘制色块、占比和冻结状态；必须配等价表格；
- `ColorHistogramControl`：同量纲最多三组曲线，线性/对数显示；
- `ColorDistributionPlaneControl`：HSV H-S 或 Lab a*-b* 固定网格；
- `PerceptualDifferenceControl`：ΔE00 直方图和固定标尺热力图；
- 图片视口优先复用现有缩放/指针坐标经验；不为此建立通用 Dock 子框架。

### 11.4 可访问性与防误解

- 色块必须同时显示 Hex、Lab、占比和序号，不靠颜色本身传递身份；
- 三组曲线同时使用颜色、线型、标签和可筛选表格；
- 键盘可以完成选择输入、切换模式、运行、冻结和导出；
- 高对比主题下边框、焦点、选中和过期状态可辨；
- Hue N/A、零方差、色域映射、结果过期、参考尺寸不同都要有可见中文文案；
- 不显示“颜色已匹配”“质量提升”“最佳 palette”等无证据结论。

## 12. 持久化、报告与导出

### 12.1 Document 快照

快照 schema 1 只保存轻量、可恢复的用户意图：

- 目标和参考路径文本；恢复时不自动读取；
- k、调色板来源、排序、迁移模式、强度；
- 当前图表空间、直方图通道、对数显示、视口缩放与中心；
- 不保存目标/参考/结果像素、直方图、聚类中心、冻结调色板、热力图或报告；
- 恢复后显示“需要重新选择/载入并运行”，不得用旧路径静默访问文件。

### 12.2 PNG 导出

- 只导出当前 fingerprint 与当前配方匹配的完整尺寸结果；
- 格式固定 PNG，禁止选择 JPEG 造成未披露的二次颜色误差；
- 编码后从内存真实解码，校验尺寸、Alpha 和关键 fingerprint，再原子发布；
- 不覆盖输入路径；同路径在应用边界拒绝；
- 失败、取消和回读不一致不留下半文件；已存在目标的替换行为沿用原子写入端口。

### 12.3 JSON/CSV 报告

建议 schema ID：`image-lab.palette-color-transfer-report/1`。报告至少包含：

- 产品、schema、颜色协议、Alpha 协议、聚类协议、色域映射协议；
- 输入尺寸、有效像素/权重、操作类型和配方；
- 目标/参考/结果的 RGB/HSV/Lab 汇总，不内嵌完整直方图时记录其摘要和 bin 协议；
- 提取/冻结调色板颜色、占比、误差、排序与来源 fingerprint；
- 迁移统计残差、Jensen-Shannon 距离、色域映射诊断；
- 重映射的 ΔE00、PSNR/SSIM、改变像素数和每 palette entry 诊断；
- 完成/取消/数值失败状态和结构化原因；
- 不写图片字节、绝对路径、用户名、机器名、临时目录或异常堆栈。

JSON 不输出 NaN/Infinity；不可适用值使用 `null + status`。CSV 使用稳定 UTF-8、RFC 风格转义和固定列；复杂调色板
可一项一行并带 `recordType`。报告 serializer 必须严格拒绝未知非有限值，不能静默写成 0。

## 13. 中文注释与设计说明要求

新增生产代码必须使用中文且详细，但注释用于解释设计和数学，不逐行翻译代码：

- 公共/内部核心类型、用例接口、颜色常量、公式、单位、范围、协议 ID、失败状态和所有权写 XML 注释；
- sRGB 分段阈值、D65 白点、Lab `δ=6/29`、Hue N/A、CIEDE2000 角度换算必须说明来源语义和风险；
- 聚类初始化、tie-break、空簇、收敛和 32³ 聚合说明为什么保证可复现及内存上限；
- 统计迁移说明为什么目标/参考可不同尺寸、为什么不叫像素配对、零方差如何处理；
- 色域映射说明为什么不直接 RGB clamp，以及映射会破坏理论统计匹配；
- 热点像素循环开头说明扫描顺序、Alpha 规则、取消粒度和临时内存；循环体保持直接；
- Session/Document 注释说明谁拥有 PixelImage、Bitmap、取消源，何时替换和释放；
- 报告注释说明隐私、非有限数和 N/A 编码；
- 注释如果需要解释多层工厂、路由或万能抽象，应先简化设计而不是补更多文字。

建议风格：

```csharp
/// <summary>
/// 将一个可能位于 sRGB 色域外的 CIELAB 颜色映射为可编码颜色。
/// </summary>
/// <remarks>
/// V1 保留 L* 与色相，只沿色度方向做固定次数二分。这样比逐通道裁切线性 RGB 更容易解释，
/// 也能把“统计迁移公式的结果”和“为了写回 8-bit sRGB 发生的损失”分别报告。
/// 本类型无状态且不拥有图片；调用方负责保留映射前颜色和聚合诊断。
/// </remarks>
internal sealed class SrgbGamutMapper
```

## 14. 单元测试、集成测试与质量门禁

### 14.1 颜色空间 Golden

- sRGB 黑、白、R/G/B 原色、50% 灰的 linear RGB、XYZ D65、Lab 参考值；
- sRGB 分段阈值两侧和反向阈值两侧；
- sRGB→Lab→sRGB 的字节往返，全部边界色和固定网格误差；
- HSV 红/黄/绿/青/蓝/品红、跨 0/360、灰阶 Hue N/A、极低饱和；
- Lab 分段阈值、D65 白点、负 a*/b*、色域外与非有限输入；
- 色度二分映射保持 L*/Hue 的冻结容差、确定迭代数和诊断分类；
- ΔE76 手算值；CIEDE2000 使用公开参考对的全部 Golden，覆盖 hue wrap、零 chroma 和对称性；
- Domain 结果不包含 NaN/Infinity，非法范围在边界失败。

### 14.2 Alpha、统计与分布

- 全透明、半透明、不透明和混合图片的有效像素/权重 Golden；
- A=0 隐藏 RGB 不进入统计且输出逐字节不变；
- 加权均值/方差、Hue 圆均值、Hue N/A 权重、小样本与零方差；
- RGB/HSV/Lab 每个 bin 的边界、under/overflow、总权重守恒；
- H-S、a*-b* 小矩阵二维网格 Golden；无逐像素对象和固定数组尺寸结构门禁；
- Jensen-Shannon：相同分布为 0、互斥分布、空 bin、对称性和有限范围；
- 完整扫描取消不返回部分统计；最大图片不分配逐像素 Lab 数组。

### 14.3 聚类与调色板

- 单色、双色、已知比例、渐变、透明权重和 `k=2/6/12`；
- 32³ cell 索引、实际均值代表色、权重守恒和固定内存；
- 第一个中心、最远加权中心、tie-break 和固定迭代的 Golden；
- 相同输入多次及 Debug/Release 得到同一中心顺序、占比和 fingerprint；
- 空簇重播种、相同颜色 cell 少于 k、迭代上限和结构化未收敛；
- 每个像素只归属一个 cluster，簇权重总和等于输入有效权重；
- 三种排序只改视图顺序，不改变 cluster identity/fingerprint；
- 冻结来源、替换图片、修改 k、仅修改排序的有效/过期规则。

### 14.4 统计迁移

- 手算 1D/3D Lab 均值标准差匹配；完整模式与保留 L* 模式；
- strength 0 的目标图逐字节相等、strength 1 的冻结公式、0.25/0.5 线性混合；
- 目标零方差、参考零方差、双方零方差、无可见像素和非有限失败；
- 目标/参考不同尺寸可运行，输出尺寸/Alpha 等于目标；
- 每次从原目标运行，不累计上次结果；输入 PixelImage 不变；
- 色域内合成样例的结果统计在冻结容差内贴近参考；色域外样例记录映射与残差；
- 取消无半结果，旧 generation 不覆盖新目标/新配方。

### 14.5 固定调色板与误差

- 黑白二色、三原色、等距离 tie、半透明和全透明像素；
- 每个输出 RGB 必须属于冻结 palette sRGB 集合，Alpha 逐字节不变；
- 最近色使用稳定 cluster index，不受显示排序影响；
- ΔE76 分配与 ΔE00 报告不得混用；手算小图的均值/P50/P95/max；
- 每 palette entry 的计数/权重总和、未使用色和占比；
- PSNR/SSIM 与共享 analyzer 一致；热力图固定标尺和代理尺寸；
- 输入、冻结调色板和结果所有权隔离；取消和异常不修改输入。

### 14.6 用例、报告、Document 与 UI

- 目标/参考载入、不同尺寸、替换、解码失败、取消和重试；
- 配方变化推进 Revision 并使结果过期，排序/图表缩放等纯显示操作不误标；
- 新运行取消旧运行，迟到成功/失败、关闭后返回不能覆盖状态；
- 两个 Scope 的路径、图片、调色板、结果、Bitmap、取消完全隔离；
- 快照不含像素、统计、palette 或结果，恢复不自动读取或运行；
- PNG 编码—真实解码—尺寸/Alpha 回读、原子写入、同输入路径阻断；
- JSON/CSV schema、UTF-8、转义、非有限数、N/A、隐私字段和原子写入；
- 第十五个真实 View 在 Avalonia Headless 加载，编译绑定、键盘、焦点、高对比、图例和等价表格可断言；
- Module 恰好登记十五个唯一 Persistable Document，零 Tool/Workflow Action/Workbench Command；
- 依赖/反射架构测试证明 Domain 无 Avalonia/JSON/文件/DI，Feature 无颜色算法；
- 生产代码与贡献清单不包含 AIFLOW 集成；现有 479 项不得删除、跳过或放宽。

### 14.7 本地开发门禁命令

G0 开始前和 G9 封板时都从仓库根目录执行：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

门禁要求：两配置构建零警告零错误；测试零失败零跳过；总数必须大于起始 479；不得删除、跳过或放宽既有测试；
颜色转换、CIEDE2000、Alpha 权重、聚类确定性、迁移和最近色都有确定值断言。性能使用结构/预算门禁，
不写依赖机器速度的严格毫秒断言。

本轮不创建 GitHub Actions、Azure Pipelines 或其他 Windows CI；本地 Release 只是另一编译配置回归，不表示发布。

## 15. G0–G9 交付与验收

### G0：产品、颜色协议与资源基线

交付：冻结第 1–8 节；审计复用类型；建立 sRGB/Lab/HSV/ΔE Golden、Alpha 规则、bin、聚类 tie-break、内存预算和风险措辞；复跑 479 项基线。

验收：没有未决定的白点、Gamma、范围、Hue N/A、透明像素、聚类 k、迁移零方差、色域映射或误差语义；历史记录写实际证据。

### G1：颜色空间、色域与 ΔE

交付：不可变颜色值、sRGB/XYZ/Lab/HSV 双向转换、色域映射、ΔE76/CIEDE2000 和协议 ID。

验收：全部参考 Golden 与边界测试通过；Domain 无 Avalonia/文件/JSON/DI；所有常量、单位和数值风险有详细中文注释。

### G2：统计与分布

交付：Alpha 加权在线统计、一维直方图、二维密度、分位数和 Jensen-Shannon 距离。

验收：权重守恒、Hue N/A、under/overflow、零样本、取消和固定内存测试通过；没有每像素对象或全图 Lab 缓存。

### G3：主色聚类与调色板

交付：32³ 聚合、确定性加权 Lab k-means、三种排序、调色板诊断、冻结与 fingerprint。

验收：合成图 Golden、Debug/Release 确定性、空簇/未收敛和过期规则通过；没有随机 seed 或运行时算法发现。

### G4：颜色统计迁移

交付：完整 Lab/保留 L*、强度、零方差、完整尺寸运行、色域映射和统计贴近诊断。

验收：不同尺寸参考可运行，目标不变、Alpha 保持、strength 0 逐字节一致、色域残差可见、取消无半结果。

### G5：固定调色板重映射与感知误差

交付：精确最近色、每项计数、CIEDE2000 汇总/热力图、PSNR/SSIM、探针和完整尺寸结果。

验收：输出颜色全集、tie-break、排序无关性、全透明规则和量化 Golden 通过；没有抖动或隐式 LUT 近似。

### G6：应用、Session 与报告

交付：七个窄用例、Session 所有权、取消/generation、JSON/CSV schema、PNG 双重回读和文件端口。

验收：用替身覆盖全部分支；Application 不依赖 Bitmap；失败可观察/重试；报告无像素和绝对路径；导出原子。

### G7：Document 生命周期与组合

交付：稳定 ID、Module/DI 登记、schema 1、命令状态、多 Scope、Bitmap 替换与关闭释放、Standalone 真实入口。

验收：第十五个贡献唯一；快照轻量且恢复不自动运行；两个实例隔离；迟到结果门禁完整；零 Tool/Workflow 注册。

### G8：UI、解释与专用文档

交付：图片/调色板/直方图/色域/ΔE 联动 UI、帮助目录、键盘/高对比、Headless 测试和第 17 节文档套件。

验收：不靠颜色独占信息；N/A、色域、误差和限制文案准确；View/Document 无颜色算法或文件访问；人工清单记录实际状态。

### G9：本地封板

交付：执行 14.7 全门禁；同步根入口、未来能力清单和公共边界；补齐 G0–G9 实施证据与回滚记录。

验收：实际测试数、零跳过、未执行事项可追踪；无 AIFLOW、Windows CI、发布脚本或发布完成声明。

## 16. 预计代码与测试落点

### 16.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Domain/Imaging/
│  ├─ SrgbColorSpace.cs
│  ├─ CieLabColorSpace.cs
│  ├─ HsvColorSpace.cs
│  └─ CieDeltaE.cs
├─ Domain/ColorTransfer/
│  ├─ ColorTransferModels.cs
│  ├─ SrgbGamutMapper.cs
│  ├─ ColorDistributionAnalyzer.cs
│  ├─ RgbColorAggregator.cs
│  ├─ DominantColorClusterer.cs
│  ├─ PaletteSorter.cs
│  ├─ LabStatisticsTransfer.cs
│  ├─ FixedPaletteRemapper.cs
│  └─ PerceptualDifferenceAnalyzer.cs
├─ Application/ColorTransfer/
│  ├─ ColorTransferContracts.cs
│  ├─ ColorTransferSession.cs
│  ├─ ColorAnalysisUseCases.cs
│  ├─ ColorOperationUseCases.cs
│  └─ ColorExportUseCases.cs
├─ Infrastructure/ColorTransfer/
│  └─ ColorTransferReportSerializer.cs
└─ Features/PaletteColorTransfer/
   ├─ PaletteColorTransferDocument.cs
   ├─ PaletteColorTransferView.axaml
   ├─ PaletteColorTransferView.axaml.cs
   ├─ PaletteStripControl.cs
   ├─ ColorHistogramControl.cs
   ├─ ColorDistributionPlaneControl.cs
   ├─ PerceptualDifferenceControl.cs
   └─ PaletteColorTransferHelpCatalog.cs
```

文件名可按职责小幅调整，但不得把转换、统计、聚类、迁移、量化、报告和 Document 堆进单一巨型类；
也不得反向拆成大量只转发一个方法且没有边界价值的接口。

### 16.2 测试

```text
tests/ImageLabPlugin.Tests/
├─ ColorSpaceAndDeltaETests.cs
├─ ColorDistributionTests.cs
├─ DominantColorPaletteTests.cs
├─ LabStatisticsTransferTests.cs
├─ FixedPaletteRemappingTests.cs
├─ ColorTransferApplicationTests.cs
├─ PaletteColorTransferDocumentTests.cs
├─ PaletteColorTransferArchitectureTests.cs
└─ PaletteColorTransferViewTests.cs
```

测试可因规模合并或拆分，但第 14 节每项门禁必须有清晰可查的归属。

## 17. 专用文档与同步范围

实施过程中已按现有能力目录惯例，在 `docs/design/palette-and-color-transfer/` 同步：

- `README.md`：能力入口、阅读顺序、当前状态与解释边界；
- `user-manual.md`：面向新手解释目标/参考、主色、迁移、固定调色板、直方图、色域和 ΔE；
- `guide.md`：精确描述参数、状态、生命周期、导出与扩展边界；
- `mathematical-principles.md`：sRGB、XYZ D65、CIELAB、HSV、k-means、统计迁移、JSD、ΔE76/ΔE00；
- `report-schema.md`：JSON/CSV 字段、版本、N/A、协议 ID 与隐私边界；
- `testing.md`：命令、实际测试数、Golden 来源、已证明与未证明事项；
- `implementation.md`：本文，持续反映计划与实际状态；
- `history/README.md` 与 G0–G9：实际实施证据，不替代当前指南。

还需同步仓库 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md`、
`docs/design/shared/image-domain-boundaries.md` 和必要的项目/窗口职责说明。文档跟随每个 G 包更新，不允许 G9 才一次性补写。

规划阶段公共入口只能标注“规划中”；生产闭环和全部本地门禁完成后才可写“开发实现与本地自动门禁完成”。

## 18. 有限人工验收清单

1. 载入含红绿蓝和灰阶色块的目标图，核对 RGB/HSV/Lab 数值、Hue N/A、直方图和 a*-b* 位置；
2. 载入含全透明隐藏 RGB、半透明和不透明像素的 PNG，核对 Alpha 权重与透明字节保持；
3. 对同一图片重复提取 k=2、6、12，确认颜色、占比和顺序可复现；
4. 切换占比/L*/Hue 排序，确认色块顺序改变但 cluster identity 与重映射结果不变；
5. 从目标和参考分别冻结 palette，确认来源、fingerprint、过期状态和调色板表格；
6. 使用不同尺寸目标/参考执行完整 Lab 迁移，确认不发生参考图缩放或像素对齐；
7. 比较 strength 0、0.5、1，确认 0 逐字节等于目标且结果每次从原目标计算；
8. 切换保留目标 L*，核对 L* 探针和亮度直方图不被统计迁移；
9. 使用单色目标/参考触发零方差路径，确认有可见诊断且没有 NaN/崩溃；
10. 使用高饱和参考触发 sRGB 色域映射，核对映射数、映射距离和统计残差；
11. 执行固定调色板重映射，确认结果颜色只来自冻结 palette，并观察 ΔE00、PSNR/SSIM 和热力图；
12. 快速换图、改配方、取消运行并关闭 Document，旧结果不得闪回、覆盖或被导出；
13. 保存/恢复快照，确认像素、palette 和结果未持久化，且不会自动读取路径或运行；
14. 同时打开两个实例，确认目标、参考、palette、结果和取消互不影响；
15. 在键盘、高对比和无颜色辨识条件下完成主要流程并读懂等价数值表；
16. 导出 PNG/JSON/CSV，核对 PNG Alpha/尺寸、报告协议/N/A/隐私和失败后的原子性。

人工清单在 Standalone 中只证明开发期交互，不证明真实 Host、ZIP、安装或发布行为。未执行项必须在 G9 记录为延期。

## 19. 回滚与兼容策略

1. 可先从 Module 隐藏第十五个贡献，同时保留 Document 类型和稳定 ID 供开发期快照安全识别；
2. 再移除 Feature View/Document 与专用应用用例注册；
3. 再移除报告、Session、迁移和重映射协调；
4. 最后移除只被本工具使用的颜色领域；若 sRGB/Lab/ΔE 已被其他能力复用，则保留共享原语；
5. 不修改或回滚既有 `PixelImage`、YCbCr、Image Compare 指标、SVD 或其他工具的稳定行为；
6. 已导出的 PNG/JSON/CSV 是用户文件，回滚不得删除或覆盖；
7. 开发期 schema/协议变化使用显式版本和迁移分支测试，不通过捕获所有异常并返回空结果来“兼容”。

## 20. 完成定义

只有同时满足以下条件，才可把状态改为“开发实现与本地自动门禁完成”：

- G0–G9 均有实际历史记录，待办没有预先勾选；
- RGB/HSV/Lab 分布、主色提取、统计迁移和固定调色板重映射形成完整可解释闭环；
- 颜色协议、Alpha 权重、聚类确定性、零方差、色域映射和 ΔE 语义有 Golden 测试；
- 目标/参考可不同尺寸，结果保持目标尺寸/Alpha，输入对象不变，取消无半结果；
- 迁移前后直方图、二维色域、统计残差、ΔE00 和量化误差可见且定义准确；
- SOLID 分层、朴素模式、窄接口、Session 所有权、取消和 generation 都有自动测试；
- 新代码中文注释覆盖颜色数学、单位、设计思路、数值风险、资源所有权和解释边界；
- Debug/Release locked 本地门禁零失败、零跳过、零警告，实际总数大于 479；
- 专用文档、公共入口、未来能力状态与共享边界同步；
- 生产代码和贡献清单中没有 AIFLOW、Workflow Action、Workbench Command 或通用 DAG；
- 没有新增 Windows CI，也没有声称完成真实 Host、ZIP、安装/卸载或发布验收。

## 21. 发布阶段明确延期

以下内容不属于本轮开发完成条件，准备正式发布时再按 `docs/design/shared/deployment-and-release.md` 执行：

- Windows CI 与目标平台矩阵；
- 正式 ZIP、manifest、哈希、依赖闭包和可复现打包；
- 真实 Host Catalog/Dock、多实例恢复、安装、升级、卸载和回滚；
- 不同 Windows 版本、DPI、主题、GPU 和权限环境；
- 授权自然图片数据集上的聚类稳定性、颜色迁移主观评测和 ΔE 阈值解释研究；
- 16 MP 目标+参考的长时间内存、取消、资源泄漏和多实例压力；
- 色彩管理、ICC/广色域/HDR、安全评审、发布说明和对外兼容承诺。

本地 Release 配置只表示第二编译配置回归，不等于发布。V1 即使通过全部开发门禁，也只能描述为
“基于固定 sRGB D65 协议的颜色统计与教学实验工具”，不能宣传为专业色彩管理系统或自动美化器。

## 22. 研究依据与实现校准

实施 G0/G1 时应把以下一手标准或公开参考数据固化为测试来源说明，而不是只凭实现者记忆抄公式：

- IEC 61966-2-1 的 sRGB 传递函数和原色/白点定义；
- CIE 15 的 XYZ/CIELAB 定义与 D65 参考白；
- Sharma、Wu、Dalal 的 CIEDE2000 补充测试数据；
- 颜色迁移经典文献仅作为方法背景；V1 必须准确标注自己使用 CIELAB 独立通道均值/标准差，不能冒充不同颜色空间或更复杂联合分布方法。

外部资料只校准数学。最终颜色常量、容差、Alpha 规则、聚类确定性、色域映射、中文解释和产品结论，
仍以本文冻结的 V1 协议及仓库 Golden 测试为准。

## 23. 实际实施记录（2026-08-31）

### 23.1 已落地

- `Domain/Imaging` 新增 sRGB/linear RGB/XYZ D65、CIELAB、HSV 和 ΔE76/CIEDE2000 单责服务；
- `Domain/ColorTransfer` 新增固定内存统计、二维密度、JSD、32³ 聚合、确定性 k-means、显示排序、色域映射、
  独立通道统计迁移、固定调色板重映射、ΔE00 汇总和像素探针；
- `Application/ColorTransfer` 新增 scoped Session 与准备、分析、冻结、迁移、重映射、PNG/报告导出窄用例；
- `Infrastructure/ColorTransfer` 新增严格 JSON/CSV serializer，图片 codec 与原子写入继续直接复用；
- `Features/PaletteColorTransfer` 新增第十五个 Persistable Document、编译绑定 View 和四个专用可视控件；
- Module、DI、Standalone、根入口、公共边界和专用文档套件均已同步；
- 测试从 479 增至 520，覆盖 Golden、Alpha、Hue N/A、固定数组、确定性、迁移、重映射、报告、快照、
  Scoped 隔离、架构与 Headless 加载。

### 23.2 朴素实现取舍

实现没有为只有一个实现的数学服务创建接口或工厂；唯一接口边界位于应用用例、codec、文件对话框、原子写入和报告
serializer。调色板排序为普通 sealed 服务，统计迁移与重映射也保留为两个直接用例，没有引入通用 `IColorOperation`
路由、Mediator、事件总线、Repository 或算法注册中心。

UI 的 V1 图形投影保持有界：当前直接显示目标 R 直方图、目标 Lab a*-b* 密度和目标—结果 ΔE00 分布，所有完整
RGB/HSV/Lab 固定数组仍保存在领域结果并通过等价文字说明。未为图表建立通用框架。

### 23.3 实跑证据与延期

`history/g9-local-sealing.md` 记录实际门禁：Debug/Release 均 0 警告、0 错误，520/520 通过、0 失败、0 跳过。
第 18 节完整人工清单、真实 Host、ZIP、安装/升级/卸载、Windows CI、16MP 长时间压力和发布评审均未执行，
继续按第 21 节延期。生产代码与贡献清单没有 AIFLOW、Workflow Action 或 Workbench Command。
