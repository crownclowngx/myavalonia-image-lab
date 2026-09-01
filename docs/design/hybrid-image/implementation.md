# ImageLabPlugin V1 Hybrid Image／混合图像设计与实施计划

> 计划状态：V1 生产实现与本地自动门禁完成；真实素材人工观察和发布门禁待后续执行
> 基线日期：2026-09-01
> 产品名称：Hybrid Image／混合图像
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3
> 实跑起始证据：locked restore 成功；Debug 构建 0 警告、0 错误；666/666 测试通过、0 失败、0 跳过
> 核心路线：显式双图输入 + 2–8 对控制点相似变换对齐 + 有效交集裁切 + Gaussian 低通／高通 + 有符号分量合成 + 1×／1/2×／1/4×／1/8×确定性预览 + 共享量程频谱 + 重影诊断
> 首要规定：SOLID 是所有实现取舍的第一约束；设计模式只用于真实变化点并保持朴素；新增生产代码必须使用详细中文注释解释设计思路、坐标、滤波公式、所有权、资源、取消和失败边界；不使用 AIFLOW；不新增 Windows CI；本阶段不执行 ZIP、真实 Host、安装、签名或任何发布门禁

本文是 ImageLab 下一项能力的实施基线。实施前仓库已有十七项产品能力、十八个多实例 Persistable Document；
Hybrid Image 已成为第十八项产品能力、第十九个 Persistable Document。未来能力清单中的“17”是候选条目编号，
不是当前产品数或 Document 数。

本产品把图像 A 的低频成分与图像 B 的高频成分组合为一张灰度混合图。近看或以较大尺寸观察时，高频主体 B 更容易被识别；
远看、缩小或降低显示分辨率时，高频逐渐被平均，低频主体 A 更占主导。V1 的目标是让对齐、截止参数、观察尺度、频谱变化和重影
原因可以被验证和解释，不承诺对所有图片、显示器、观看距离或观察者都产生相同的主观效果。

## 0. 计划摘要

### 0.1 当前结论

- V1 新增一个多实例 `Persistable Document`，稳定 ID 候选为 `myavalonia.plugin.image.lab.document.hybrid-image`；
- 图像 A 固定表示低频主体和输出参考坐标系，图像 B 固定表示高频主体；UI、报告和代码不得交换命名语义；
- V1 接受两张用户显式选择的图片，透明区域先在白色背景上合成，再转换为确定性亮度平面；输出固定为不透明灰度 RGBA；
- 对齐使用 2–8 对显式控制点求解“B 到 A”的统一缩放、旋转和平移相似变换；不提供自动人脸、特征点、单应或非刚性配准；
- 结果只取 A 与变换后 B 的有效交集，并要求用户确认有界裁切矩形；不以透明补边或静默拉伸伪造有效对应区域；
- 低通与高通均固定为 Gaussian。低通作用于 A；高通定义为 B 减去 B 的 Gaussian 低通；两者有独立有限截止参数；
- 领域计算使用 double 亮度，组合前保留高频负值；只有最终显示／导出时量化并统计下溢、上溢和裁切比例；
- 1×、1/2×、1/4×、1/8×预览由同一未量化结果通过固定面积平均生成，不使用控件缩放冒充观察尺度；
- 原图、对齐图、低频分量、高频分量、混合结果、频谱、截止环和重影叠加必须由同一 recipe fingerprint 原子提交；
- 重影显示以控制点残差、红／青边缘叠加和 100% 结果为事实；两点精确拟合时明确标注“无法独立验证残差”，不伪造高置信度评分；
- 代理预览最大边固定 1024；显式完整尺寸执行遵守现有图像 16,000,000 像素预算和新能力的滤波工作集预算；
- G0–G9 只执行本地开发门禁；不使用 AIFLOW，不新增 Windows CI，不执行发布门禁。

### 0.2 固定实施顺序

1. G0 冻结产品语义、坐标、Gaussian 公式、Golden、资源预算和专用文档骨架；
2. G1 完成控制点、相似变换、最小二乘求解和确定性指纹；
3. G2 完成 B→A 逆向采样、有效交集、裁切和重影诊断；
4. G3 完成 Gaussian 低通、高通、有符号分量与组合量化；
5. G4 完成多尺度预览、频谱共享量程、截止环和联动诊断；
6. G5 完成 Session、窄用例、取消、generation、资源预算和完整尺寸执行；
7. G6 完成严格 recipe／report、PNG 导出、轻量快照和原子写入；
8. G7 接入第十九个 Persistable Document、DI、Module、Standalone 和可访问 UI；
9. G8 完成全部专用文档、索引同步和有限人工验收；
10. G9 复跑 Debug/Release 全量本地门禁并完成本地开发封板。

不得先在 View、Control、Document 或 code-behind 中编写像素循环、控制点求解、逆向采样、Gaussian 卷积、FFT 或量化逻辑。
对齐、裁切、滤波、组合、尺度和诊断必须先成为无 Avalonia 依赖的领域事实，并由自动测试冻结。

## 1. 产品形态与用户闭环

### 1.1 产品决策

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Hybrid Image／混合图像` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID 候选 | `myavalonia.plugin.image.lab.document.hybrid-image`；只在 G7 实际接入后成为持久身份 |
| 显示名称 | `混合图像` |
| 显示分类 | `图像分析` |
| 图像 A | 低频主体、参考坐标系和默认裁切边界 |
| 图像 B | 高频主体；经相似变换映射到 A 坐标系 |
| 颜色 | V1 固定灰度亮度实验；透明像素在白底合成；输出 Alpha 固定 255 |
| 对齐 | 2–8 对控制点，求解统一缩放、旋转、平移；禁止镜像、剪切、透视和非刚性形变 |
| 滤波 | 固定 Gaussian；A 取低通，B 取 `原亮度 - Gaussian 低通` 的高通 |
| 截止语义 | UI 以 `σ` 像素为主参数，同时显示 50% 幅度截止频率；recipe 保存规范化后的有限 double |
| 合成 | `raw = lowGain × low(A) + highGain × high(B)`；默认两个 gain 均为 1 |
| 观察尺度 | 1×、1/2×、1/4×、1/8×面积平均；四张图来自同一个 raw 结果 |
| 输出 | 有效交集裁切后的不透明灰度 PNG；禁止覆盖任一输入文件 |
| 设计模式 | 外部编解码／文件交互继续用窄端口；固定 Gaussian 不建立 Strategy；其余优先 sealed 服务和值对象 |
| 明确排除 | 自动配准、彩色模式、透视、非刚性变形、AI、AIFLOW、Workflow、Workbench Command、Windows CI 和发布门禁 |

### 1.2 用户闭环

```text
显式选择图像 A（低频主体）和图像 B（高频主体）
    ↓
在同步缩放的两个对齐画布上添加 2–8 对对应控制点
    ↓
求解 B→A 相似变换；检查旋转、缩放、平移、残差和有效交集
    ↓
在红／青边缘叠加与 100% 混合预览中识别错位产生的双边缘／重影
    ↓
确认或收紧有效交集裁切矩形
    ↓
调节 A 低通 σ、B 高通 σ 和两个有限增益
    ↓
联动观察低频／高频分量、共享量程频谱与截止环
    ↓
同时比较 1×、1/2×、1/4×、1/8×；确认主体随尺度切换
    ↓
执行显式完整尺寸结果并导出新 PNG；可选导出 recipe 和脱敏报告
```

### 1.3 产品解释边界

- “远看”由确定性缩小预览近似，不等同于真实视角、视力、屏幕点距或环境光；
- 高频主体不会在近处自动完全遮蔽低频主体，低频主体也不会在远处对所有人都成为唯一可见内容；
- 控制点只表达用户认为应对应的位置；工具不判断两张图片是否在语义上适合混合；
- 两个控制点足以精确确定一个相似变换，但没有冗余点验证拟合误差；至少三个分散点才可把残差作为诊断参考；
- 红／青边缘不重合表示几何或内容边缘不同，不自动等于算法错误；它是解释重影的辅助视图，不是配准真值；
- `σ` 越大，低通越模糊；对 `B-Gaussian(B)` 而言，`σ` 越大则高通截止越低、B 保留的频带越宽。UI 必须以可读文案避免用户把两个方向理解反；
- 高频分量包含负值；灰色可视化只用于观察，不能把显示图重新当作数值输入；
- PSNR/SSIM 不适合给两张不同主体的混合图判定“质量”，V1 不提供误导性的统一质量分数。

## 2. 当前项目事实与复用边界

### 2.1 已验证基线

2026-09-01 在当前工作树实跑：

- `dotnet restore ImageLabPlugin.slnx --locked-mode` 成功；
- Debug `--no-restore -warnaserror` 构建 0 警告、0 错误；
- Debug 测试 666/666 通过、0 失败、0 跳过；
- 实施前 Module 登记十八个 Persistable Document、零个 Tool；实施后登记十九个 Persistable Document、零个 Tool；
- Hybrid Image 生产代码、稳定 ID、Document、View、Standalone 与专用测试已经接入，最终证据见 `testing.md`。

666/666 只是本计划起点。后续每个 Gate 必须填写真实测试总数，不得预填完成数字，不得为了数量拆分无意义测试。

### 2.2 必须直接复用

- `PixelImage`、`ImageSize`、16,000,000 像素输入上限、图片解码、PNG 编码、原子写入和文件对话框惯例；
- `ImageAreaResampler.ResizeToMaximumEdge` 的确定性面积缩小，供 A/B 交互代理使用；四档观察尺度必须从 raw double 生成，由 Hybrid Image 的纯数值投影器负责，不能先量化成 `PixelImage` 再复用；
- `Fft1DTransform`、`Fft2DTransform`、`FrequencySpectrum`、`FrequencyCoordinates`、`SpectrumProjector` 和共享显示量程；
- `ImageDifferenceProxyProjector` 或等价的既有有界差异投影，只在语义确实一致时复用；
- `BorderIndexMapper` 的 `Reflect101` 边界事实；若其可见性不允许跨领域直接依赖，可无行为变化提取到 `Domain/Imaging`；
- Document Scope、取消、generation、stale、Bitmap 释放、快照脱敏和 Standalone 通过真实 Module/DI 解析的既有惯例；
- 现有 strict JSON、有限大小读取、内存回读、PNG 真实回读和原子发布惯例。

### 2.3 允许的共享改进

如果 Hybrid Image 成为第二个需要显式尺寸面积缩放的产品，继续增强 `ImageAreaResampler` 的小尺寸 Golden 与取消测试，
不另建一份产品专用面积缩放器。若多个产品都需要中心化频谱共享量程，允许给现有投影器增加职责一致的窄入口。

`BorderIndexMapper` 当前服务于卷积领域。若 Hybrid Image 的 Gaussian 也采用 `Reflect101`，允许将纯索引映射提取到 Imaging：

- 提取前补齐卷积的负索引、单像素、边界和随机等价回归；
- 原 `Constant/Replicate/Reflect101/Wrap` 行为和异常语义保持不变；
- 共享类型只映射索引，不知道卷积核、Hybrid Image、UI 或 recipe；
- 若提取会扩大 G3 风险，则 Hybrid Image 可保留一个短小且测试充分的私有映射器，不为形式复用破坏职责。

### 2.4 禁止的错误复用

- 不调用 `FrequencyFilterDocument`、`ImageCompareLabDocument`、`SpectralArtDocument` 或它们的 Session/View；
- 不把 Frequency Filter 的完整产品 recipe 改名复制；Hybrid Image 有双输入、对齐、裁切、尺度和重影语义；
- 不把 `FrequencyGainMask` 直接作为空间 Gaussian 的实现，也不为“都叫高低频”强迫两个产品共享应用模型；
- 不让 Domain 引用 Avalonia、Bitmap、文件路径、JSON、Features、DI 或 Host SDK；
- 不在 Document 中保存可变像素数组、亮度平面或 FFT `Complex[]`；
- 不在 View/code-behind 中计算矩阵、采样坐标、卷积核、频率、残差或图像统计；
- 不建立通用图像流水线、节点图、Event Bus、Mediator、Repository、反射算法目录、插件内插件或脚本层；
- 不为只有一个实现的相似变换求解器、Gaussian 滤波器、组合器或诊断器机械创建接口、工厂和多层继承。

## 3. V1 范围与非目标

### 3.1 V1 必须完成

- A/B 两张显式图片、单次解码、角色标签和防止路径混淆；
- 2–8 对控制点的添加、删除、重排、键盘微调和成对完整性校验；
- B→A 相似变换、退化点拒绝、镜像拒绝、有限值和确定性 tie-break；
- 对齐后的 B 逆向双线性采样、有效性遮罩、最大有效交集和用户裁切矩形；
- 同步 pan/zoom、控制点连线、棋盘格／半透明／红青边缘三种对齐检查视图；
- Gaussian 低通 A、Gaussian 低通 B、高通 B、raw 混合和最终量化；
- 低通／高通独立 `σ`、50% 幅度截止换算、两个 gain 和 overlap 警告；
- 1×、1/2×、1/4×、1/8×由同一 raw 结果面积平均生成并一次提交；
- A/B、低/高分量和混合结果的中心化频谱；前后比较必须共享显示量程；
- 截止环、DC、Nyquist、频率读数和当前参数联动；
- 控制点 RMS/max 残差、尺度/旋转/平移、有效覆盖率、raw min/max、裁切数和比例；
- 代理预览、显式完整尺寸结果、取消、generation、防迟到、多实例隔离和关闭释放；
- PNG、版本化 recipe JSON、版本化 JSON/CSV 报告和轻量快照；
- Debug/Release 本地自动门禁、0 跳过、中文详细注释和文档同步。

### 3.2 V1 明确不实现

- 自动检测眼睛、人脸、SIFT/ORB 特征、光流、AI 对齐或推荐控制点；
- 仿射剪切、透视单应、薄板样条、网格形变、非刚性配准或镜像；
- 彩色 Hybrid Image、独立 RGB 截止、颜色迁移、色差校正或 Alpha 保留；
- Ideal、Butterworth、任意手绘遮罩、方向滤波、Gabor 或小波混合；
- 自动搜索“最佳”截止／gain、感知实验统计、眼动、显示器校准或距离换算承诺；
- 视频、实时摄像头、批处理、目录扫描、工作流、宏、脚本或命令行导出；
- AIFLOW、Workflow Action、Workbench Command 或新增 Tool；
- 超预算时静默缩小后再放大并冒充完整尺寸结果；
- 覆盖任一输入、JPEG 输出、云上传、遥测或报告中保存绝对路径；
- 新增 Windows CI、ZIP、签名、真实 Host 安装和任何发布门禁。

## 4. 输入、亮度与尺寸协议

### 4.1 输入角色

- A 与 B 的角色是 recipe 的稳定组成，交换按钮必须显式交换图片、控制点端点、变换方向和相关状态；
- 替换任一输入立即使对齐、滤波结果、频谱和导出资格 stale；
- 两张图允许原始尺寸和宽高比不同，但必须通过对齐和交集裁切形成同一输出栅格；
- 文件路径只属于当前 Document 会话，不进入领域指纹、recipe、报告或快照；
- 同一路径可以被用户选为 A/B，但 UI 必须提示它更适合验证滤波而不是双主体演示。

### 4.2 亮度协议

V1 固定输出灰度，避免颜色通道带来的相位、色边和“低频颜色属于谁”的额外产品语义：

1. RGBA 先在白色 sRGB 背景上按 Alpha 合成；
2. 合成后的 R/G/B 以既有 `ImageChannelConverter` 的 Y 语义转为 `[0,255]` double；
3. 滤波前规范化到 `[0,1]`；
4. 高频在 double 中保留正负，不提前偏移到灰色或 byte；
5. 最终 raw 乘 255、ToEven 舍入并裁切到 `[0,255]`，R=G=B，A=255。

若现有 Y 转换与上述白底 Alpha 合成顺序不兼容，G0 必须用透明像素 Golden 冻结唯一顺序。不得因 UI 预览便利改变领域数值。

### 4.3 代理与完整尺寸

- 交互代理保持 A 的坐标系，A 最大边缩到 1024，B 和全部控制点按各自原图归一化坐标映射；
- 对齐求解使用归一化控制点再投影到当前目标尺寸，保证 512/1024/完整尺寸的几何语义一致；
- 完整尺寸以 A 的原始像素栅格为参考，B 直接从原图逆向采样，不能把代理结果放大；
- 输出尺寸等于用户确认的 A 坐标交集裁切矩形，不承诺等于任一输入原始尺寸；
- 任一尺寸乘法、缓冲长度、核半径和总工作集估计使用 checked，并在大数组分配前失败。

## 5. 控制点与相似变换

### 5.1 控制点模型

`HybridAlignmentPointPair` 是不可变值对象：

```text
Id                    会话内稳定的有界整数
PointA                A 图归一化坐标 [0,1]×[0,1]
PointB                B 图归一化坐标 [0,1]×[0,1]
Enabled               V1 不保存半对；删除时整对删除
```

- 数量必须为 2–8；少于 2 对不能求解，多于 8 对拒绝；
- 坐标必须有限并位于闭区间；离散仅发生在显示与采样边界，求解全程使用 double；
- 每张图上任意两点的基线不得短于该图对角线的 2%，避免缩放和角度对噪声极端敏感；
- 规范指纹按 `Id` 排序后写入 ToString 无关的二进制事实，不依赖当前文化或 JSON 属性顺序；
- UI 未完成的一端只保存在 Presentation 草稿，不得进入 Domain、快照或求解器。

### 5.2 B→A 相似变换

采用无镜像二维相似变换：

```text
[xa]   [ cosθ -sinθ ] [xb]   [tx]
[ya] = s [ sinθ  cosθ ] [yb] + [ty]
```

实现用中心化点集的闭式最小二乘求解 `s、θ、tx、ty`，禁止通过通用矩阵库、反射或迭代优化引入不必要复杂度。

- `s` 必须有限且位于 `[0.1,10]`；旋转规范化到 `[-180°,180°)`；
- 协方差近零、点退化、镜像最优但无镜像解、非有限结果和超范围缩放结构化拒绝；
- 两对点时给出精确拟合并将残差状态标为 `NotIndependentlyValidated`；
- 三对及以上报告像素 RMS、最大残差和以 A 对角线归一化的残差比例；
- 求解器不得改变输入点顺序或静默丢弃“离群点”；V1 不做 RANSAC；
- 逆变换必须由解析式构造并用 round-trip Golden 验证，不在每个像素上重复求逆。

### 5.3 对齐交互

- 两张源图画布共享缩放档位和逻辑中心，但各自保留 letterbox；
- 点击 A 点后必须完成对应 B 点，或显式取消草稿；
- 控制点按编号和颜色匹配，键盘方向键微调当前点，Shift 使用较大步长；
- “交换 A/B”是显式命令，执行后重新求解反向变换并使所有下游结果 stale；
- 任何自动吸附只允许视觉网格，不允许根据图像内容偷偷移动点。

## 6. 逆向采样、有效交集与重影

### 6.1 B→A 逆向采样

输出的每个 A 坐标 `(xa,ya)` 通过预计算逆变换得到 B 浮点坐标 `(xb,yb)`，再用确定性双线性采样取得 B 亮度：

- 像素中心约定为 `(x+0.5,y+0.5)`；归一化点和像素中心之间的换算必须写中文注释与 Golden；
- 四个邻点都位于 B 图时样本有效；越界不使用 Clamp/Wrap/Reflect 伪造内容；
- 权重累加顺序固定，最终 double 不提前量化；
- 采样器每行检查取消；源 B 和输出缓冲均不原地修改；
- 整数平移、90° 旋转、统一缩放、亚像素平移和边界点必须有 Golden。

### 6.2 有效交集与裁切

- A 的完整矩形与变换后 B 的有效四边形形成几何有效区；
- V1 先按双线性采样所需四邻点生成二值有效掩码，再用逐行直方图与单调栈求最大面积轴对齐整数矩形；面积相同时依次选择更靠上、更靠左、更矮、更窄的矩形，形成确定性默认裁切；
- 若最大矩形任一边小于 32 或面积低于 A 代理的 10%，阻断并解释“有效交集不足”；
- 裁切使用左闭右开整数边界，修改控制点、输入或变换会重新验证；
- recipe 保存归一化到 A 的裁切矩形，不保存代理像素坐标；
- UI 同时显示 A 边界、变换后 B 四边形、最大有效矩形和用户裁切矩形。

用户可以向默认矩形内收紧裁切，但不能扩到无效像素。实现不得用变换后 B 的外接边界框冒充有效交集，也不得只检查四角后忽略
矩形内部的无效样本。最大矩形算法属于纯几何/栅格事实，必须以小掩码 Golden、随机暴力对照和稳定 tie-break 测试冻结。

### 6.3 重影诊断

V1 不把不同主体的全图像素差当作“错位分数”。只提供可解释事实：

- 控制点残差：2 点为不可独立验证，3–8 点报告 RMS/max；
- 参数事实：统一缩放、旋转、平移、有效覆盖率和最短控制点基线；
- 边缘叠加：A 的 Sobel-Y 边缘映射为红色，变换后 B 的边缘映射为青色；共同强边缘趋近白色；
- 100% 混合：错误对齐会直接形成双眼、双轮廓等双边缘；
- 可选“仅看高频 B”与“仅看低频 A”用于区分几何重影和截止选择；
- 诊断文案必须说明内容本就不同的边缘不会重合，不能把颜色差异解释为自动判定失败。

## 7. Gaussian 低通、高通与合成

### 7.1 固定响应

V1 只有 Gaussian，不建立滤镜目录或 Strategy。空间核为：

```text
g(x) = exp(-x² / (2σ²))
radius = ceil(3σ)
kernel[x] = g(x) / sum(g)
```

二维滤波采用先水平、后垂直的可分离卷积，边界固定 `Reflect101`。核半径、系数和归一化都由一个 sealed 领域服务负责。

对应连续频率幅度响应为：

```text
L(f) = exp(-2π²σ²f²)
f50 = sqrt(ln 2) / (sqrt(2)πσ) cycles/pixel
```

UI 以 `σ` 为可编辑主参数，同时显示 `f50 × min(outputWidth,outputHeight)` 的“每幅图周期数”解释值。离散截断核的真实响应会与连续公式略有差异，
频谱截止环只作为理论参考；测试必须用离散冲激响应验证实际行为，不把公式显示值冒充精确测量。

### 7.2 参数边界

| 参数 | V1 范围 | 默认值 | 语义 |
| --- | --- | --- | --- |
| `LowSigmaPixels` | `[0.8,32]` | `8` | A 的低通；越大越模糊、保留频率越低 |
| `HighSigmaPixels` | `[0.8,32]` | `6` | 从 B 中减去的低通；越大时只移除更低频部分 |
| `LowGain` | `[0,2]` | `1` | A 低频分量权重 |
| `HighGain` | `[0,2]` | `1` | B 高频分量权重 |

- 所有参数必须有限，Recipe 构造时校验；
- 任一 gain 为 0 必须精确消除对应分量，不继续执行不必要滤波；
- 两个 gain 都为 0 是合法黑图诊断，但 UI 明确提示；
- 核最大长度 193；未来扩大 `σ` 必须先更新计算预算和 Golden；
- `σ` 变化只使滤波及下游 stale，不重新解码或求解控制点。

### 7.3 分量与组合

```text
LowA(x,y)  = Gaussian(A, LowSigmaPixels)
HighB(x,y) = BAligned(x,y) - Gaussian(BAligned, HighSigmaPixels)
Raw(x,y)   = LowGain × LowA(x,y) + HighGain × HighB(x,y)
```

- `LowA` 理论范围接近 `[0,1]`；`HighB` 有正有负且均值接近 0；
- 不给 `HighB` 加 0.5 后再参与组合；0.5 只可作为有符号分量预览的显示中性灰；
- 不执行每图 min/max 自动拉伸、直方图均衡或“看起来更好”的隐藏归一化；
- 最终量化分别统计 `Raw<0`、`Raw>1`、总裁切像素、比例、raw min/max/mean；
- 源 A、源 B、对齐结果、低频、高频和 raw 均由不可变结果或会话所有，外部不能修改内部数组；
- 任何 NaN/Infinity、核和偏离 1 超阈值、输出尺寸不符或高通源被修改均结构化失败。

### 7.4 计算策略与预算

V1 首选确定性的可分离 Gaussian，以 `O(width×height×radius)` 换取清晰边界语义。G3 必须用基准样本确认 1024 代理交互可接受。

- 自动预览采用 200 ms 防抖；拖动控制点过程中只刷新几何叠加，提交后才重算像素；
- 若 `radius × pixelCount` 超过固定工作量预算，自动预览阻断并要求降低代理最大边或 `σ`；
- 完整尺寸必须显式点击，估算通过后执行；不得在 UI 线程执行卷积；
- 若 G3 实测证明完整尺寸直接卷积不可接受，只允许在保持同一离散 Gaussian、边界和 Golden 的前提下优化滑窗／递推实现；
- 不得悄悄改用不同 FFT 边界、近似 box blur 或代理放大结果来通过性能门禁。

## 8. 多尺度观察与频谱联动

### 8.1 观察尺度

- 1× 是最终量化图的原始输出像素；
- 1/2、1/4、1/8 由同一 raw double 先做面积平均，再统一量化，避免 byte 量化后缩小引入额外误差；
- 不足 8 像素的边按至少 1 像素处理；目标尺寸使用明确整数规则和 Golden；
- 四个尺度一次生成并携带同一 recipe fingerprint、generation 和结果时间；
- UI 可把四格切换为单格放大，但不能用视觉缩放生成新的“实验尺度”；
- 导出默认只导出 1×；报告记录四档尺寸和每档亮度统计，不默认导出四张 PNG。

### 8.2 频谱

频谱诊断固定使用灰度通道并至少提供：

- 对齐裁切后的 A 原始频谱；
- A 低频分量频谱；
- 对齐裁切后的 B 原始频谱；
- B 高频分量频谱；
- raw 混合结果频谱；
- 1/2、1/4、1/8 选择尺度的结果频谱按需生成。

所有同屏频谱必须使用共同的 `SpectrumDisplayScale`。低频和高频响应的 50% 理论截止以同心环叠加；悬停显示中心化 `(fx,fy)`、半径、
当前低通/高通响应和对应分量幅度。频谱显示使用有界代理，完整尺寸结果不为每个尺度长期缓存一份 2048² `Complex[]`。

### 8.3 原子联动

- 参数改变立即标记旧低频、高频、混合、多尺度和频谱 stale；
- 新结果只有全部必需分量与预览成功且 generation 匹配时一次替换；
- 取消、异常或迟到完成保留最后有效结果，但 UI 明确标记它对应旧 fingerprint，禁止导出；
- 切换频谱页签可以惰性生成显示 Bitmap，但数值来源必须是当前结果，不能重新采用不同 recipe；
- 频谱、截止环、空间图和摘要中的尺寸、σ、gain、控制点指纹必须一致并由测试验证。

## 9. Application、Session 与生命周期概览

本节冻结 G5 的所有权、用例和生命周期边界；只有 G5 实际完成后，才在 `history/g5-session-use-cases-and-resources.md`
记录真实实现、测试数量和资源证据。

### 9.1 窄用例

- `PrepareHybridInputsUseCase`：单次解码 A/B、白底合成、代理和输入指纹；
- `SolveHybridAlignmentUseCase`：验证控制点、求解变换、计算有效交集和几何诊断；
- `RenderHybridPreviewUseCase`：代理逆向采样、裁切、滤波、组合、多尺度和频谱事实；
- `RenderHybridFullSizeUseCase`：从原始输入和同一规范化 recipe 执行完整尺寸结果；
- `ExportHybridImageUseCase`：编码、内存回读、真实 PNG 回读、事实复核和原子发布；
- recipe/report 的读写保持专用 serializer 和有限读取边界。

用例可按职责合并短小 DTO，但不得合并成一个带文件对话框、图像数学、状态和 JSON 的万能 `HybridImageService`。

### 9.2 Session

一个 scoped Document 独占一个 `HybridImageSession`：

- 两张完整源 `PixelImage` 与两个输入指纹；
- 两张最大边 1024 的代理及尺寸映射；
- 当前已验证的对齐解、裁切和 recipe；
- 最后有效代理结果与可选完整尺寸结果；
- 不保存 Avalonia Bitmap、文件对话框、绝对路径、JSON DTO 或全局缓存。

无状态求解器、采样器、Gaussian、组合器、频谱投影器和 serializer 可注册 singleton；Session、Document、结果和 Bitmap 必须 scoped／实例所有。

### 9.3 取消与迟到结果

- 解码／准备、对齐提交、代理渲染、完整尺寸和导出使用明确的取消范围；
- 修改任一输入、控制点、裁切、σ 或 gain 都推进 generation 并使下游 stale；
- 每行采样、卷积、面积缩放、FFT、投影、编码和报告循环都检查 `CancellationToken`；
- 候选 Session/Result/Bitmap 在未提交、迟到、关闭或异常路径释放；
- 关闭时先阻止新提交，再推进 generation、取消、等待串行闸门退出并释放全部实例资源；
- 捕获仅用于把预期取消转成状态；其他异常不吞掉，也不把堆栈直接展示给用户。

## 10. 持久化、导出与隐私

### 10.1 Recipe

G6 建立独立协议 `hybrid-image-v1`，至少保存：

- schema/version/protocol；
- A/B 角色和各自不含路径的内容指纹；
- 2–8 对归一化控制点和顺序；
- 规范化裁切矩形；
- `LowSigmaPixels`、`HighSigmaPixels`、`LowGain`、`HighGain`；
- 灰度、白底、Gaussian、Reflect101、双线性、ToEven 和尺度档位等固定枚举事实；
- 代理尺寸与完整尺寸请求不是算法身份，不允许 recipe 声称已生成结果。

严格读取拒绝未知 schema、未知枚举、重复属性、缺失字段、非有限数、越界点、超长文本和尾随内容。Recipe 不嵌入图片像素或绝对路径；
导入后必须由用户重新选择 A/B，并以内容指纹匹配，不能自动读取旧路径。

### 10.2 Report

报告记录输入尺寸/指纹、对齐参数、控制点残差状态、有效覆盖、裁切、滤波参数、理论 f50、gain、raw 范围、裁切统计、尺度尺寸、
频谱摘要、运行时长、实现版本和结果 fingerprint。不得记录绝对路径、原图像素、控制点截图或用户目录。

### 10.3 PNG 导出

- 只允许导出与当前输入、对齐、裁切和参数 fingerprint 一致的完整尺寸成功结果；
- 目标不得与 A 或 B 规范化绝对路径相同；
- PNG 编码后先内存回读，核对尺寸、RGBA、灰度、Alpha=255 和结果 fingerprint；
- 原子写入后再从真实目标回读一次；失败不得删除或替换内存中的最后有效结果；
- V1 不导出 JPEG，避免有损重编码改变高频主体后仍被报告为同一实验事实。

### 10.4 快照

快照只保存 UI 参数、控制点、裁切、页签和可选脱敏路径提示；不保存原图、亮度数组、频谱、结果 PNG 或绝对路径。
恢复后状态固定为“需要重新选择两张输入”，不自动访问磁盘、不自动执行对齐或滤波。

## 11. UI 与可访问性

### 11.1 布局

建议使用四段式布局：

1. 输入与角色：A/B 路径、选择、交换、尺寸、指纹状态；
2. 对齐工作区：同步双画布、点对列表、变换摘要、有效交集和重影页签；
3. 混合参数：两个 σ、f50、gain、裁切、自动预览和显式完整尺寸；
4. 结果工作区：分量、四尺度、频谱和报告／导出。

窄窗口允许纵向滚动，不用固定像素高度把命令或状态挤出可视区。大图 Bitmap 必须替换即释放。

### 11.2 交互与键盘

- 所有画布操作都有列表/数值替代入口；
- 控制点可用键盘选择、移动、删除；颜色之外还使用编号和线型区分；
- 同步 zoom 提供明确百分比，重置视图不改变领域参数；
- 滑块同时有 NumericUpDown，显示范围、步长和当前值；
- 自动预览可关闭；关闭后参数变化只标 stale，用户点击“重新生成”；
- Busy 时只禁用冲突命令，取消和查看最后结果仍可用；
- 状态文案区分“尚未执行”“已过期”“已取消”“代理完成”“完整尺寸完成”和“导出完成”。

### 11.3 解释性文案

- A：远看／缩小时主要可见的低频主体；
- B：近看／放大时更明显的高频主体；
- `σ` 大表示更强的空间模糊，不把“截止更低/更高”的方向只留给用户猜测；
- 两点对齐没有独立残差验证；三点以上的低残差也不保证主体语义匹配；
- 红青边缘叠加是观察工具，不是自动配准结论；
- 高频分量预览的中性灰和放大倍数必须显式显示，不得冒充最终像素。

## 12. SOLID 与朴素设计规定

### 12.1 单一职责

- 值对象只验证并持有控制点、变换、裁切、recipe、指标；
- 求解器只求相似变换，不采样图片；
- warp 只把 B 采样到 A，不做滤波；
- Gaussian 只生成核和滤波平面，不决定产品角色；
- 组合器只组合与量化，不生成 Bitmap；
- 诊断器只计算事实，不弹窗、不写文件；
- Application 只协调顺序、取消、资源和端口；
- Document 只管理命令、状态、generation、Bitmap 和生命周期；
- View 只绑定和转交坐标，不包含业务循环。

### 12.2 开闭与依赖倒置

- 外部图片解码、文件选择、文本读取、原子写入继续通过既有窄端口；
- Gaussian 是 V1 固定协议，不为假想滤波器创建 `IHybridFilter`；未来真的增加 Butterworth 时再基于已证明变化点重构；
- 相似变换固定，不建立 `IAlignmentStrategy`；未来透视不是简单替换实现，而是新 recipe/schema 与资源语义；
- Domain 只依赖 Domain；Application 依赖 Domain 与端口；Infrastructure 实现端口；Features 依赖应用用例；
- Host SDK 只出现在组合和 Document/View 边界。

### 12.3 接口隔离与里氏替换

- 新文件对话框若需要，定义只包含 Hybrid Image PNG/recipe/report 意图的 `IHybridImageFileDialog`；
- 不扩大 `IImageCodec` 使其理解 Hybrid recipe；
- 测试替身必须遵守取消、所有权和异常合同，不能返回生产实现永远不会返回的共享可变数组；
- 不通过继承 `FrequencyFilterDocument` 或 `ImageCompareLabDocument` 复用 UI；组合共享服务而非继承产品。

### 12.4 明确禁止的炫技

- 不增加抽象工厂、Builder 流水线、Visitor、Mediator、Event Sourcing、CQRS、服务定位器或反射注册；
- 不为每个 DTO 创建 Mapper 接口；简单、纯粹且测试清楚的显式转换可以直接写；
- 不用基类统一所有 Document 生命周期；继续采用清晰的实例所有权和小型私有帮助方法；
- 不为消除少量语义不同的重复建立万能图像处理框架；
- 设计模式必须在评审中回答“真实变化点是什么、减少了哪一种耦合”，答不出则删除。

## 13. 中文注释规定

- 所有新增生产代码注释使用中文；
- 核心类 XML `<remarks>` 解释设计目的、输入输出、坐标、所有权、线程和取消边界；
- 控制点归一化、像素中心、B→A/逆变换、最小二乘、退化判断必须说明公式和“为什么”；
- Gaussian 核、3σ 截断、Reflect101、f50 换算、高频负值和最终裁切必须说明数值意图；
- 多尺度预览必须说明为何从 raw double 面积平均，而不是缩放已量化 Bitmap；
- 频谱共享量程、截止环理论/离散差异和惰性生成必须说明事实边界；
- generation、stale、候选结果提交、Dispose 和导出回读必须说明资源与竞态设计；
- 不给简单属性、赋值、显而易见的空判断和普通循环堆砌“设置 X”“遍历 Y”式注释；
- 注释与代码冲突视为门禁失败，必须与测试和文档同包更新；
- 复杂公式旁优先给短推导或不变量，不复制本文整段内容制造噪声。

## 14. 测试策略与本地门禁

### 14.1 数值 Golden

- 控制点：两点平移/旋转/缩放、三点过定、8 点带确定噪声、退化、镜像、超范围和文化无关指纹；
- 变换：正逆 round-trip、像素中心、整数/亚像素坐标、90°、负角度和边界；
- warp：双线性 2×2 Golden、越界无效、有效遮罩、源不变和预取消；
- 交集：相同尺寸、平移、旋转、缩放、极小交集、左闭右开和规范化裁切往返；
- Gaussian：核奇数长度、对称、和为 1、冲激响应、常量不变、Reflect101、σ 边界和取消；
- 高频：常量严格为 0（浮点容差内）、低频趋势被移除、正负值保留、源不变；
- 合成：gain 0/1/2、raw 公式、NaN 拒绝、ToEven、上下溢裁切和统计；
- 尺度：1/2、1/4、1/8 面积平均 Golden、奇数尺寸和 raw-before-byte 顺序；
- 频谱：DC、低/高能量迁移、共轭、共享量程、截止环半径和选择尺度 fingerprint。

### 14.2 对齐与重影测试

- 2 点状态必须为 `NotIndependentlyValidated`，不能显示伪造的 0 误差通过徽章；
- 3–8 点 RMS/max 由实际重投影计算，点顺序变化不改变求解；
- 红／青边缘投影固定颜色、阈值、透明度和尺寸；
- 人工构造错位边缘在叠加图中形成分离红/青线，正确对齐趋向共同亮边；
- 不同内容即使残差为 0 也不得生成“图片已正确配准”的领域结论；
- 控制点、输入或裁切变化后旧重影图与混合图都必须 stale。

### 14.3 Application 与生命周期测试

- A/B 各只解码一次；替换任一输入释放旧 Session；
- 代理和完整尺寸从各自原始源生成，完整尺寸不放大代理；
- 预取消、处理中取消、忽略取消的假服务迟到、异常和关闭路径；
- generation、引用身份和 recipe fingerprint 三重提交校验；
- 多 Document Scope 完全隔离；singleton 服务不缓存图片或 Session；
- 预算在大数组分配前阻断；取消/失败不破坏最后有效结果；
- Bitmap 替换、页签惰性频谱、关闭和迟到候选全部释放；
- 快照恢复不读文件、不解码、不求解、不滤波。

### 14.4 文件与协议测试

- strict recipe JSON round-trip、属性乱序、重复/未知/缺失字段、未知 schema、非有限值、超限点对和尾随内容；
- recipe/report 尺寸上限、UTF-8、跨文化小数、指纹稳定和绝对路径脱敏；
- 禁止覆盖 A/B，目标同路径的大小写/规范化/符号链接语义按现有平台边界测试；
- PNG 编码后内存回读与真实目标回读的尺寸、灰度、Alpha 和 fingerprint；
- 原子写入失败不留下半文件，不清除内存结果；
- stale、代理结果、过期完整结果和回读不一致均禁止导出。

### 14.5 UI、组合与架构测试

- Module 接入后固定十九个 Persistable Document、零 Tool，旧十八个 ID 与顺序不变；
- Stable ID、Descriptor、DI 生命周期、Standalone 通过真实 Module 解析；
- AXAML 编译绑定、命令、Busy/CanExecute、两点警告、stale 和错误文案；
- 双画布坐标映射、letterbox、DPI、同步 zoom、键盘点移动和重置视图；
- 依赖方向扫描：Domain 无 Avalonia/IO/JSON/Features/Host；Document/View 无 FFT、卷积和像素循环；
- NuGet 白名单不变；无 AIFLOW、Workflow、Workbench Command、Windows CI 和发布文件；
- 新增核心生产代码存在有价值的中文设计注释，禁止用测试扫描“注释数量”替代人工评审。

### 14.6 性质与随机测试

- 固定种子的合法相似变换 round-trip 和控制点重投影；
- 随机常量平面经 Gaussian 保持常量；
- 随机平面高通均值接近 0 且 `low + high` 重构 B（同一 σ、容差内）；
- 随机参数下所有结果有限，源缓冲不变，统计计数等于像素数；
- 四个尺度尺寸单调不增并共享 fingerprint；
- 随机取消点不会提交部分结果或泄漏实例资源。

### 14.7 性能与资源门禁

- 1024 代理默认参数的 prepare/alignment/render 分阶段计时；
- 最大核、最大代理、8 点、五张频谱页签的最坏组合不超过固定工作量/内存预算；
- 完整尺寸在执行前显示估算并允许取消；测试验证阻断发生在大缓冲分配前；
- 同时存活的全尺寸亮度平面、临时卷积缓冲、raw、量化图和频谱数量有代码审查清单；
- 自动测试可以证明所有权和预算分支，不宣称已完成外部 profiler、所有设备性能或真实 Host 内存测量。

### 14.8 本地命令

每个 Gate 至少执行相关过滤测试；G9 执行完整本地命令：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Release --no-build --no-restore
git diff --check
```

要求 0 失败、0 跳过、0 警告。只有实施后真实执行的结果可以写入 `testing.md` 与历史记录。
本计划不新增也不执行 Windows CI，不执行真实 Host、ZIP、安装、签名或发布门禁。

## 15. G0–G9 Gate 与验收

### G0：产品、数学与基线

- 冻结 A/B 角色、灰度/白底、控制点、相似变换、裁切、Gaussian、高频、组合和尺度语义；
- 建立小尺寸手算 Golden 与透明、边界、常量、冲激、错位样本；
- 记录 666/666 起始基线、资源预算、依赖和不新增 NuGet 约束；
- 建立 `docs/design/hybrid-image/` 文档骨架，但不伪造完成历史。

验收：所有公开词语和公式只有一个含义；G0 起始基线与后续实现证据分开记录。

### G1：领域模型与相似变换

- 实现点对、变换、recipe 基础值对象和求解器；
- 覆盖 2–8 点、退化、镜像、范围、正逆和随机性质；
- 生产代码中文注释解释中心化闭式求解和失败条件。

验收：无 Avalonia/IO/JSON；所有数值测试通过；不建立策略工厂。

### G2：Warp、交集与重影

- 实现逆向双线性采样、有效遮罩、裁切和边缘叠加；
- 冻结像素中心、左闭右开、越界和有效覆盖事实；
- 自动测试显示构造错位确实产生双边缘解释图。

验收：源图不变、取消可达、重影诊断不冒充自动正确性判断。

### G3：Gaussian 与组合

- 实现有限核、Reflect101、可分离卷积、高通、raw 合成和量化；
- 验证常量、冲激、低+高重构、裁切与统计；
- 运行 1024 代理和最大核基准，冻结工作量预算或如实调整范围。

验收：所有结果有限、确定性；边界和性能没有静默降级。

### G4：尺度、频谱与联动

- 实现 raw double 多尺度、共享量程频谱、截止环和惰性投影；
- 保证同一结果 fingerprint、generation 和参数原子联动；
- 完成频谱、DC、响应与空间分量 Golden。

验收：四尺度不是控件缩放；同屏频谱不各自自动拉伸。

### G5：Session、用例与资源

- 完成输入准备、对齐、代理渲染、完整尺寸和导出前置窄用例；
- 建立 Session 所有权、generation、stale、取消、迟到拒绝和工作集预算；
- 确认完整尺寸直接消费原始输入与规范化 recipe，不放大代理；
- 补齐 Application、生命周期、资源和多 Scope 测试。

验收：所有失败路径保留最后有效结果；资源有唯一所有者；文档状态只按真实证据更新。

### G6：协议、报告与导出

- 建立 strict recipe/report schema、有限读取、脱敏和跨文化序列化；
- 完成 PNG 内存回读、目标回读、禁止覆盖和原子发布；
- 快照不保存像素、频谱或绝对路径，恢复不自动执行。

验收：只有当前完整尺寸结果可导出；协议拒绝歧义输入和半成功文件。

### G7：Document、UI 与组合

- 接入稳定 ID、第十九个 Document、DI、真实 Module 和 Standalone；
- 完成双画布、控制点、裁切、参数、四尺度、频谱、重影和状态交互；
- 完成 Headless View、编译绑定、坐标、键盘、生命周期和架构测试。

验收：Document/View 不含数学循环；旧十八个 Document 不变；零 Tool。

### G8：专用文档与人工验收

- 完成 README、guide、user-manual、mathematical-principles、testing、recipe/report schema 和 G0–G9 实际历史；
- 同步根 README、`docs/README.md`、`docs/design/README.md`、未来能力和公共图像领域边界；
- 人工检查人脸/建筑/文字三类配对、正确/错误对齐、四尺度、截止、重影、频谱和导出；
- 如实记录没有执行 Windows CI、真实 Host、ZIP、安装、签名和发布门禁。

验收：普通用户、开发者和维护者都有单一入口，文档不超出证据。

### G9：本地开发封板

- 执行第 14.8 节 Debug/Release 全量命令；
- 记录实际测试数量、警告、失败、跳过、耗时和环境；
- 复核 SOLID、朴素模式、中文注释、资源、取消、隐私和旧能力回归；
- 检查无 AIFLOW、Windows CI、发布文件和无关改动；
- 只有全部通过后把状态改为“V1 本地开发封板”。

验收：全部本地门禁通过，或如实保留未完成状态与阻断；不得用计划勾选代替证据。

## 16. 预计代码、测试与文档落点

### 16.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Application/
│  └─ HybridImage/
│     ├─ HybridImageContracts.cs
│     ├─ HybridImageSession.cs
│     └─ HybridImageUseCases.cs
├─ Constants/
│  └─ PluginIds.cs
├─ Domain/
│  └─ HybridImage/
│     ├─ HybridAlignment.cs
│     ├─ SimilarityTransformSolver.cs
│     ├─ AlignedImageSampler.cs
│     ├─ HybridCropValidator.cs
│     ├─ GaussianPlaneFilter.cs
│     ├─ HybridImageComposer.cs
│     ├─ HybridScaleProjector.cs
│     └─ HybridImageDiagnostics.cs
├─ Features/
│  └─ HybridImage/
│     ├─ HybridImageDocument.cs
│     ├─ HybridImageView.axaml
│     ├─ HybridImageView.axaml.cs
│     ├─ HybridAlignmentCanvas.cs
│     └─ HybridImageCoordinateMapper.cs
├─ Infrastructure/
│  └─ Persistence/
│     └─ HybridImageSerializers.cs
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs
```

这是职责落点，不要求为了文件数机械拆分。紧密且短小的值对象可以合并；Domain、用例、Session、Document、View 和文件 IO
不得重新塞入一个大类。

### 16.2 测试

建议按职责新增：

- `HybridAlignmentDomainTests.cs`；
- `HybridWarpAndCropTests.cs`；
- `HybridFilterAndCompositionTests.cs`；
- `HybridScaleAndSpectrumTests.cs`；
- `HybridImageApplicationTests.cs`；
- `HybridImageDocumentTests.cs`；
- `HybridImageViewTests.cs`；
- `HybridImageArchitectureTests.cs`；
- 对 `SpectrumDomainTests`、`ConvolutionDomainTests`、`ImageCodecAndUseCaseTests`、
  `CompositionAndPersistenceTests` 做必要的增量回归。

### 16.3 专用文档

```text
docs/design/hybrid-image/
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
   └─ g0-... 至 g9-...                    # 只在对应阶段完成后写实际记录
```

实施已按阶段创建完整专用文档组，并只在对应代码与测试落地后填写 G0–G9 历史；
测试命令与未执行的人工/发布事项以 `testing.md` 为准，不把计划门禁写成已通过证据。

## 17. 人工验收场景

### 17.1 人脸或头像

1. 为两张正面头像分别标记双眼和嘴角 4 对点；
2. 检查 3 点以上残差、红青眼眶边缘和有效交集；
3. 故意把一个点移动 4–8 像素，确认 100% 结果出现可解释的双眼／双轮廓；
4. 恢复点位并调节两个 σ，确认重影和频率选择可以被区分；
5. 比较 1× 与 1/8×，记录主体切换是否明显，不把主观观察写成自动保证。

### 17.2 建筑或物体

1. 使用角点、窗框或物体轮廓设置 3–6 对分散控制点；
2. 检查统一相似变换无法处理透视时的残差和局限提示；
3. 确认产品不会静默切换到仿射或透视；
4. 检查低频大轮廓与高频纹理在频谱和空间分量中一致；
5. 收紧裁切并确认所有尺度和导出尺寸同步变化。

### 17.3 截止与尺度

1. 将 A/B 设为同一测试图，验证低频与同 σ 高频在容差内可重构；
2. 增大 Low σ，确认 A 更模糊、低频谱更集中；
3. 增大 High σ，确认 B 高频包含更宽的非 DC 频带；
4. 将 HighGain 设为 0，四尺度都只剩 A 低频；
5. 将 LowGain 设为 0，确认输出裁切统计和中性高频显示语义清楚。

### 17.4 生命周期与边界

1. 打开两个实例，使用不同图片和控制点，确认状态完全隔离；
2. 快速拖动点和参数，最终只提交最后 generation；
3. 运行中取消或关闭，确认没有迟到 Bitmap、结果或导出；
4. 选择超预算图片／σ，确认在大缓冲分配前阻断；
5. 导出失败后重试，确认内存完整结果仍可用且旧文件不被破坏；
6. 快照恢复后确认不自动读取 A/B、不对齐、不滤波。

### 17.5 Standalone 边界

Standalone 可以证明：

- Module、DI、View、编译绑定、命令和插件内部对象图可工作；
- 多 Document Scope、控制点、参数、取消、导出和 Bitmap 生命周期；
- 主要交互在本地 Avalonia 窗口可用。

Standalone 不能证明：

- 真实 Host Catalog、Dock、布局恢复和插件卸载；
- AssemblyLoadContext 与发布依赖闭包；
- 正式 ZIP、Windows CI、签名或目标用户设备性能；
- 所有图片对、显示距离和观察者都产生强烈主体切换。

## 18. 风险与对策

| 风险 | 对策 |
| --- | --- |
| 两张图对齐不足形成双边缘 | 2–8 点、3 点以上残差、红青边缘、100% 预览和明确人工责任 |
| 两点残差为 0 被误解为完美 | 固定状态 `NotIndependentlyValidated`，不显示通过徽章 |
| 透视/表情差异无法由相似变换处理 | 明确 V1 边界，显示残差，不静默升级模型 |
| 截止方向难懂 | σ 主参数 + f50 解释 + 分量/频谱/四尺度同时显示 |
| 高频裁切造成亮暗饱和 | 不自动归一化，报告 raw 范围与上下溢比例，允许调低 gain |
| 边界卷积产生假轮廓 | 固定 Reflect101、交集裁切、边界 Golden 和中文注释 |
| 大 σ 直接卷积过慢 | 1024 代理、防抖、工作量前置预算和显式完整尺寸执行 |
| 多频谱造成内存峰值 | 惰性投影、只缓存 Bitmap/摘要、不长期持有多份 Complex[] |
| 不同尺度由 UI 缩放导致平台差异 | 从 raw double 使用固定面积平均生成真实像素 |
| 为复用污染现有 Frequency Filter | 只复用纯数学/图像事实；产品 Session、recipe、Document 完全隔离 |
| 快速交互提交旧结果 | generation + 引用身份 + fingerprint 三重校验 |
| 文档提前宣称完成 | 计划和证据分离，history/testing 只写真实执行结果 |

## 19. 兼容、迁移与回滚

### 19.1 兼容规则

- 现有十八个 Document ID、快照 schema、显示顺序和行为不变；
- Spectrum Inspector、Frequency Filter、Convolution Playground、Image Compare 和 Spectral Art 的数值协议不变；
- 新 Hybrid Image ID 在 G7 首次登记后不得更改；
- recipe schema 1 发布后，控制点坐标、相似变换、Gaussian、边界、采样、量化或尺度发生语义变化时必须升级版本；
- 本计划不授权新增 NuGet；如实施需要依赖变化，必须先单独记录理由、许可、锁文件和插件依赖风险。

### 19.2 回滚顺序

若某阶段无法达到门禁：

1. 不登记或移除尚未稳定的 Module/Standalone 入口；
2. 移除 Document、View 和应用用例；
3. 只有通过独立测试且不改变既有消费者时，才保留共享的边界映射或频谱投影改进；
4. 对卷积边界映射的共享重构必须整体完成或整体回滚；
5. 不回退、改名或放宽任何现有能力；
6. 文档如实记录失败阶段、原因和保留内容，不把部分完成写成 V1 封板。

### 19.3 数据迁移

当前没有 Hybrid Image 快照或 recipe 需要迁移。开发期 schema 变更可清除仅供开发的样本；首次发布后必须保留旧 schema 的
显式读取路径或给出结构化不兼容提示，不能静默用新坐标、滤波或量化公式解释旧 recipe。

## 20. V1 开发封板检查清单

以下按真实代码和自动证据更新；未完成项继续保留未勾选。

### 产品与协议

- [x] A/B 角色、灰度/白底、控制点、对齐、裁切和 recipe 语义已冻结；
- [x] 2–8 点相似变换及两点不可独立验证状态可解释；
- [x] Gaussian 低通 A、高通 B 和组合公式唯一且确定；
- [x] 1×/1/2×/1/4×/1/8×来自同一 raw 结果；
- [x] 频谱与空间域结果使用同一 fingerprint 原子联动；
- [x] 产品不宣称自动配准、感知保证或所有观看条件有效。

### 数值与资源

- [x] 控制点、变换、warp、交集、Gaussian、高通、合成和尺度有 Golden；
- [x] 常量、冲激、同图重构、边界、随机性质与取消门禁通过；
- [x] raw min/max、裁切、频谱量程和截止环事实准确；
- [x] 代理/完整尺寸不混淆，完整结果不由代理放大；
- [x] 工作量和内存在大分配前阻断，超预算不静默降级；
- [x] 频谱惰性生成且不长期缓存多份完整 Complex[]。

### 架构与生命周期

- [x] Domain、Application、Infrastructure、Features 依赖方向正确；
- [x] Document/View 不含矩阵、采样、卷积、FFT、像素和 JSON 业务；
- [x] 设计模式仅用于真实外部边界，没有模式炫技；
- [x] Session、Result、Bitmap、取消源和临时缓冲所有权明确；
- [x] 多 Scope、generation、stale、迟到、关闭和导出有测试；
- [x] Module 为十九个 Persistable Document、零 Tool；
- [x] 不使用 AIFLOW、Workflow Action 或 Workbench Command。

### 测试与文档

- [x] 起始 666 个测试全部保持通过，最终真实数量已记录；
- [x] 新增领域、应用、文件、Document、UI、组合和架构测试；
- [x] Debug/Release locked 本地门禁 0 失败、0 跳过、0 警告；
- [x] 专用 README、指南、说明书、数学、测试、schema 和 G0–G9 实际历史齐全；
- [x] 根索引、未来能力和共享边界已同步；
- [ ] 详细中文注释已经人工评审并与实现一致；
- [x] 文档明确未执行 Windows CI、真实 Host、ZIP、签名和发布门禁。

只有上述项目以真实代码、自动测试、人工检查和文档证据全部完成后，才能把本文状态改为“V1 本地开发封板”。
