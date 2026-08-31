# ImageLabPlugin V1 Seam Carving／内容感知缩放设计与实施计划

> 计划状态：生产实现与本地自动门禁已完成；发布门禁延期<br>
> 基线日期：2026-08-31<br>
> 产品名称：Seam Carving／内容感知缩放<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 起始证据：520/520；完成证据：587/587，净增 67，Debug/Release 0 警告、0 错误、0 失败、0 跳过<br>
> 核心路线：固定 Sobel 亮度能量 + 有符号区域偏置 + 确定性动态规划 + 逐缝删除/预规划插入 + 有界播放 + 双线性/双三次参考对照<br>
> 首要规定：SOLID 是所有实现取舍的第一约束；设计模式只用于真实变化点并保持朴素；新增生产代码使用详细中文注释解释公式、坐标、边界、所有权、取消和设计思路；不使用 AIFLOW；不新增 Windows CI；本阶段不执行 ZIP、真实 Host、安装或任何发布门禁

本文是 ImageLab 第十五项产品能力、第十六个多实例 Persistable Document 的实施基线与落地记录。产品通过能量图、
最小能量缝、区域偏置和逐步播放解释“内容感知缩放”怎样选择像素路径，并用普通重采样提供诚实对照。

它不是通用图片编辑器，也不承诺理解人物、文字或物体语义。V1 的“保护”和“优先删除”是用户显式绘制的
能量偏置，不是语义分割；PSNR、SSIM 和差异图只描述两种算法结果的差异，不证明其中一种审美上更好。

## 0. 实际落地摘要

G0–G9 已按冻结顺序完成。稳定 ID 采用候选值；建议预算全部按第 8.1 节冻结；PNG 导出复用现有编码与原子写入，
本轮没有增加强制回读，以避免重复既有编解码闭环。普通重采样保留在 Seam Carving 领域，没有改动 Robustness 协议。
笔划按归一化点列确定性重放，单条最多 2,048 点，快照仍受 512 笔划和 128 KiB 双门禁。

实现额外采用一个 Document 内 `SemaphoreSlim` 串行闸门，确保非线程安全 Session 不会被载入、预览、单步和播放并发访问；
它是生命周期保护，不是事件总线或通用调度框架。区域预览增加洋红/黄绿相反斜纹，完成结果自动显示干净像素。
详细用户入口、schema、测试和阶段证据见本目录其他文档。真实 Host、ZIP、Windows CI、安装和发布验收仍未执行。

## 1. 决策摘要

### 1.1 产品形态

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Seam Carving／内容感知缩放` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID | `myavalonia.plugin.image.lab.document.seam-carving`；已在 G7 写入 `PluginIds`，一经发布不得改名 |
| 显示名称 | `内容感知缩放` |
| 显示分类 | `图像分析` |
| 输入 | 用户显式选择的一张非预乘 RGBA8888 图片 |
| 输出 | 与目标宽高严格一致的 RGBA8888 图片；不覆盖源文件 |
| 缩放方向 | 宽度、高度或两者；每一步只处理一条垂直缝或水平缝 |
| 能量 | 固定白底 Alpha 合成后的 BT.601 亮度；3×3 Sobel；clamp-to-edge 边界；归一化到 `[0,1]` |
| 区域约束 | 单一三态栅格：普通、保护、优先删除；保护和删除不能在同一像素同时生效 |
| 最小缝 | 双精度动态规划；8 邻接中的三前驱；固定 tie-break；不使用随机数 |
| 缩小 | 每一步重新计算能量和最小缝，再删除一条缝 |
| 放大 | 按轴分批在影子副本上寻找互不重复的缝，再在真实工作图上逐条插入并播放 |
| 双轴顺序 | 默认先处理绝对变化比例更大的轴；相等时先宽后高；用户可显式选择宽优先或高优先 |
| 播放 | 预览下一缝、单步、播放、暂停、取消、重置；不常驻全部 RGBA 帧 |
| 普通对照 | 像素中心对齐的双线性或 Catmull–Rom 双三次，用户一次选择一种，结果与 Seam 输出同尺寸 |
| 导出 | 当前完整结果 PNG；版本化 JSON/CSV 实验报告；不导出未完成或过期结果 |
| 外部依赖 | V1 不新增 NuGet、原生库、GPU、OpenCV 或图表框架 |
| 模式使用 | 不可变值对象、普通 sealed 数值服务、两个参考重采样 Strategy、窄用例、构造注入 |
| 明确排除 | AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP、真实 Host 与发布门禁 |

### 1.2 用户闭环

```text
显式选择图片
    ↓
查看原图、Sobel 能量图和资源预算估算
    ↓
按需绘制“保护”“优先删除”或“擦除”笔划
    ↓
设置目标宽高、双轴顺序和普通缩放对照算法
    ↓
生成有界执行计划；若超预算则在分配大数组前结构化阻断
    ↓
查看下一条缝及其能量、区域命中数和当前尺寸
    ↓
单步执行，或播放／暂停／取消；每一步都能观察图像、能量和缝
    ↓
联动比较 Seam Carving 与普通缩放，不把差异指标解释成审美评分
    ↓
按需导出完整 PNG 或不含像素、绝对路径和蒙版栅格的 JSON/CSV 报告
```

### 1.3 固定实施顺序

1. G0 冻结产品语义、数值协议、Golden 样本、资源公式和起始门禁；
2. G1 完成亮度投影、Sobel 能量、能量显示和区域偏置；
3. G2 完成确定性动态规划、垂直/水平缝验证与逐缝删除；
4. G3 完成保护/优先删除蒙版、插入缝规划、坐标映射和 Alpha 安全插值；
5. G4 完成双轴计划器、内存/工作量预算、逐步播放状态机和取消；
6. G5 完成双线性/双三次普通对照、差异与质量诊断；
7. G6 完成 Session、窄用例、PNG/JSON/CSV 导出和严格序列化；
8. G7 接入第十六个 Persistable Document、快照、DI、Module 和 Standalone；
9. G8 完成可访问 UI、专用文档和有限人工验收；
10. G9 复跑 Debug/Release 全量本地门禁，完成本地开发封板。

不得先在 View 或 Document 中写 Sobel、动态规划或像素搬移循环，再把它称为领域实现。能量公式、边界、
tie-break、蒙版偏置、插入坐标、Alpha 插值和资源公式必须先在 Domain 中冻结并通过自动测试。

## 2. 当前项目事实与复用边界

### 2.1 已验证基线

仓库当前具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集，以及只供本地开发的 `ImageLabPlugin.Standalone`；
- 实施前为十四项能力、十五个 Document；实施后为十五项能力、十六个 Persistable Document；仍没有 Tool、Workflow Action 或 Workbench Command；
- 自有非预乘 RGBA8888 `PixelImage`、`ImageSize.MaximumPixelCount = 16_000_000` 和既有解码输入上限；
- `ImageAreaResampler`、`ImageAnalysisProxyProjector`、双线性扰动采样和完整参考质量分析；
- `FullReferenceQualityAnalyzer` 的 MAE、RMSE、PSNR-Y/RGB、全局 SSIM-Y 与 Alpha 统计；
- PNG/JPEG 解码、PNG 编码、原子写入、文件对话框、Document Scope、取消、generation 和 Bitmap 释放惯例；
- 2026-08-31 实施前为 520/520；实施后 locked restore、Debug/Release 0 警告/0 错误、两配置 587/587 且 0 跳过。

520/520 是实施前起点，不是 Seam Carving 的完成证据。任何后续阶段都必须在阶段历史中记录新增、删除、跳过、
放宽及实跑结果；不得用文档中的预期数字代替真实命令输出。

### 2.2 必须直接复用

- 图片解码、PNG 编码、原子发布和文件对话框继续使用现有端口，不复制文件 IO；
- 完整工作图继续使用 `PixelImage`，不建立第二种 RGBA 图片容器；
- 输入尺寸和最终输出尺寸继续通过 `ImageSize` 校验，所有乘法使用 checked 或 long；
- Seam 与普通缩放结果同尺寸后，差异和质量统计复用 `FullReferenceQualityAnalyzer` 与既有差异投影语义；
- Standalone 必须从真实 Module/DI 解析真实 Document 和 View，不复制演示业务；
- 无状态能量、路径、像素变换和参考重采样服务登记为 singleton；图片、蒙版、计划、当前帧、Bitmap 和取消源归 Document Scope 独占。

### 2.3 允许的共享改进

现有双线性采样位于 Robustness 的扰动实现中，不应让新领域依赖 Robustness。G5 可以把稳定的像素中心逆向采样
提取到 `Domain/Imaging` 的窄服务，并以回归测试证明 Robustness 输出未改变。双三次只在有第二个真实消费者前保留在
Seam Carving 领域，不提前建立通用图像处理框架。

推荐共享边界：

```text
ImagePixelSampler
  只负责 clamp 边界、非预乘 RGBA 的双线性取样和冻结舍入规则

FullReferenceQualityAnalyzer
  只比较同尺寸 PixelImage；不知道 Seam、目标尺寸或“更好”结论
```

如果提取共享采样会扩大 G5 风险，可以先在 Seam Carving 内实现并用等价回归锁定；不得为了形式复用而破坏
Robustness 已有协议。

### 2.4 禁止的错误复用

- 不让新 Document 调用 Robustness、Convolution 或 Image Compare Document；只复用稳定的无状态领域服务；
- 不把 `ImageAreaResampler` 的面积缩小冒充双线性或双三次对照；
- 不把 Convolution Playground 的展示结果或 ViewModel 当作 Sobel 能量输入；Sobel 数值核心应独立且可测试；
- 不在 Avalonia 控件、Converter、code-behind 或 Document 中实现动态规划、缝删除/插入或蒙版变形；
- 不建立通用 Pipeline、Mediator、Event Bus、Repository、反射算法目录、DAG、脚本层或插件内插件系统；
- 不为只有一个实现的 Sobel、路径查找器、删除器、插入规划器建立接口和工厂；
- 不把分析代理上找到的缝静默映射到完整图并宣称为精确 Seam Carving；V1 要么精确处理当前工作图，要么阻断。

## 3. V1 范围与明确非目标

### 3.1 V1 必须完成

- 单图显式输入，支持目标宽度、目标高度或两者改变；输出尺寸必须逐项精确；
- 显示归一化 Sobel 能量图、区域偏置后的有效能量图，以及线性/对数两种只影响显示的映射；
- 垂直缝和水平缝的最小累计能量动态规划、固定 tie-break 与数值诊断；
- 删除缝和插入缝；宽高不变时不运行，输出为独立克隆；
- 下一缝叠加显示，删除缝使用高对比红色，插入缝使用高对比青色，并提供非颜色图例；
- 保护、优先删除、擦除三种画笔，笔径有界，笔划数量和快照大小有界；
- 显示每条缝命中的保护像素、优先删除像素、基础能量、偏置能量和累计能量；
- 单步、播放、暂停、取消、重置；播放速度只控制 UI 节拍，不改变算法结果；
- 双线性或 Catmull–Rom 双三次普通缩放对照；两种方法使用同一目标尺寸；
- Seam/普通结果的同步视图、分割线/并排模式、差异图及同尺寸质量统计；
- 预执行资源估算、运行时预算计数、每行/每阶段取消、generation 防迟到和关闭释放；
- 完整尺寸结果 PNG、版本化 JSON/CSV 报告、轻量快照和中文错误/限制说明；
- 多实例隔离、参数变化使计划/结果过期、恢复不自动读取图片或运行算法；
- Debug/Release locked restore、warn-as-error build、全部自动测试、0 跳过和文档同步。

### 3.2 V1 明确不实现

- 自动人物/物体/人脸/文字检测、语义分割、AI 蒙版、GrabCut 或深度学习能量；
- Forward Energy、熵、显著性、HOG、神经网络或用户脚本能量；V1 只冻结 Sobel；
- 多尺寸实时自由拖拽、无界目标尺寸、一次运行跨越超预算的大图或极端缝数；
- 多图批处理、文件夹扫描、覆盖源文件、JPEG/WebP/AVIF 结果导出；
- 二维运输图（optimal transport map）、全局最优双轴顺序或把固定顺序宣传为全局最优；
- 对象移除后的纹理修复、Poisson/inpainting、自动补洞或内容生成；
- 任意多层撤销/重做全部 RGBA 帧；V1 使用“重置到输入并按计划重播”；
- 在快照或报告中保存完整 RGBA、能量矩阵、累计代价矩阵、逐帧图片或完整蒙版栅格；
- GPU、SIMD 专项版本、unsafe、原生代码或新增第三方图像库；先以可验证的托管实现建立正确性基线；
- AIFLOW、工作流节点、Workflow Action、Workbench Command、脚本或宏；
- Windows CI、真实 Host、ZIP、安装/升级/卸载和任何发布门禁。

### 3.3 解释边界

- 低 Sobel 能量只表示当前亮度梯度较小，不表示该像素在语义上不重要；
- 保护区域是很强的有限惩罚，不是数学上的绝对不可删除；目标尺寸过小或保护范围过大时仍可能被穿过；
- 优先删除区域是很强的有限奖励，不保证完整对象消失，也不执行删除后的内容修复；
- 插入缝通过邻域插值产生新像素，不会生成真实的新纹理；放大过多会出现拉伸和重复；
- 双线性/双三次保持规则网格，Seam Carving 改变空间对应关系；两者的像素差异不等于质量排名；
- 固定双轴顺序是确定性产品选择，不声称得到二维全局最优解；
- 当前工作图若超过预算会被明确阻断，不静默降低分辨率、减少缝数或改变目标尺寸。

## 4. 像素、亮度与 Sobel 能量协议

### 4.1 RGBA 与透明像素

- 输入和输出均为非预乘 RGBA8888；删除缝时四通道逐字节搬移；
- 计算能量前先把像素合成到固定白底，避免 `A=0` 下不可见 RGB 影响路径：

```text
Cvisual = (A / 255) × C + (1 - A / 255) × 255
Y = 0.299 × Rvisual + 0.587 × Gvisual + 0.114 × Bvisual
```

- 白底和 BT.601 全范围协议 ID 冻结为 `seam-energy-bt601-white-matte-sobel-v1`；
- 插入像素时先把相邻颜色转为预乘 sRGB，分别平均预乘颜色与 Alpha，再在 Alpha 非零时反预乘；
- 相邻像素都全透明时，新像素 RGBA 固定为 `(0,0,0,0)`，不传播隐藏 RGB；
- Alpha 与颜色按同一几何位置插入，最终字节使用 `MidpointRounding.ToEven` 并 clamp 到 `[0,255]`。

### 4.2 Sobel 公式

固定 3×3 Sobel 核：

```text
Gx = [-1 0 1; -2 0 2; -1 0 1] * Y
Gy = [-1 -2 -1; 0 0 0; 1 2 1] * Y
Ebase = sqrt(Gx² + Gy²) / (4 × 255 × sqrt(2))
```

- 图片边界使用 clamp-to-edge；不补零、不循环、不镜像；
- `Ebase` 计算为 double，并因浮点误差最终 clamp 到 `[0,1]`；
- 1×N、N×1 和 1×1 都必须有定义；无法再沿某轴删除时返回结构化阻断；
- 能量显示的线性/`log1p` 映射只产生预览字节，不改变动态规划输入；
- 统计记录最小、最大、均值、P50/P95 和非有限值计数；任何非有限数使本步失败，不能替换为零继续。

### 4.3 区域偏置

蒙版值固定为 `Normal=0`、`Protect=1`、`PreferRemoval=2`。同一像素只能有一种状态；后画笔划覆盖先画笔划，
擦除恢复为 Normal。有效能量为：

```text
Eeffective = Ebase
           + 1000 × I(mask == Protect)
           - 1000 × I(mask == PreferRemoval)
```

固定权重使用户意图明显强于普通 `[0,1]` 梯度，同时仍保留“必要时可穿过”的有限语义。V1 不开放权重滑块，
避免用户把数值调成非有限、难复现或误以为硬约束。报告必须同时保存基础能量与偏置后能量，不能只显示被修改的图。

## 5. 最小能量缝与确定性动态规划

### 5.1 垂直缝

垂直缝为每一行一个 x 坐标，满足 `|x[y]-x[y-1]| <= 1`。累计代价：

```text
M(0,x) = Eeffective(0,x)
M(y,x) = Eeffective(y,x) + min(M(y-1,x-1), M(y-1,x), M(y-1,x+1))
```

越界前驱不存在，不用无穷之外的哨兵参与普通加法。每个单元只保存累计 double 和一个 `sbyte` 前驱偏移，
回溯从最后一行最小端点开始。

### 5.2 水平缝

水平缝为每一列一个 y 坐标，满足 `|y[x]-y[x-1]| <= 1`。实现可以用统一的“主轴/次轴”索引器复用循环，
但不得为了复用而复制转置整图。水平和垂直路径必须分别有可读的测试入口与中文坐标说明。

### 5.3 tie-break 与验证

- 前驱代价相等时选择次轴坐标较小者；终点相等时也选择较小坐标；
- 相等判定使用精确 double 比较，因为输入和运算顺序已冻结；不得引入依赖机器的随机扰动；
- `SeamPath` 必须验证长度、坐标范围、邻接约束、方向和图片尺寸版本；
- 应用过期尺寸上的路径必须失败，不能尽力套用；
- 累计代价和回溯路径须用穷举小矩阵 Golden 测试证明全局最小，并用重复运行证明确定性。

## 6. 缝删除、插入与蒙版同步

### 6.1 删除

- 删除垂直缝后宽度减一，删除水平缝后高度减一；
- 每行或每列使用切片复制，避免为每个像素创建对象；
- RGBA 与三态蒙版使用同一路径同步删除；
- 每次删除后重新计算完整能量图，不在 V1 引入局部增量优化；正确、可取消和可验证优先；
- 维度已经为 1 时不得继续删除该方向，并在计划阶段提前阻断。

### 6.2 插入规划

直接插入后再找下一缝会反复选择新产生的低能量副本。V1 对同一轴的一批插入采用标准的影子删除规划：

1. 建立当前图的影子工作副本和“当前位置→批次起点坐标”映射；
2. 在影子副本上逐条找缝并删除，记录每条缝在批次起点上的坐标；
3. 批次大小受资源预算约束，且不得超过当前可删除的次轴尺寸减一；
4. 在真实工作图上按记录坐标逐条插入；每行/列根据已插入位置修正偏移；
5. 一个批次完成后，如仍需插入，再以新工作图重新规划下一批。

记录的是坐标路径，不保存影子 RGBA 帧。批次计划必须携带源尺寸、方向、顺序和 fingerprint，防止套用到错误图片。

### 6.3 插入像素与蒙版

- 在缝的右侧（垂直）或下方（水平）插入新像素；边界缝使用当前像素与唯一相邻像素插值；
- 插入坐标约定必须集中在 `SeamInserter`，不能由 View 猜测叠加位置；
- 新蒙版像素：任一邻居为 Protect 则 Protect；否则任一邻居为 PreferRemoval 则 PreferRemoval；否则 Normal；
- 路径显示标记“将插入的位置”，而不是把源像素误标为即将删除；
- 插入后必须验证尺寸、RGBA 长度、蒙版长度和 `ImageSize.MaximumPixelCount`。

## 7. 双轴计划与逐步播放

### 7.1 操作计划

`SeamResizePlan` 是不可变意图，至少包含：输入 fingerprint、输入/目标尺寸、宽高增减数、轴顺序、普通对照算法、
估算单元访问数、估算峰值字节、蒙版 stroke fingerprint 和协议版本。它不包含图片、能量矩阵或全部路径。

双轴顺序有三种：

- `Auto`：先处理绝对变化比例较大的轴；相等时先宽；
- `WidthFirst`：完成全部宽度步骤后处理高度；
- `HeightFirst`：完成全部高度步骤后处理宽度。

V1 不实现二维运输图。选择顺序改变结果，UI 和报告必须显式显示；参数或笔划变化立即使旧计划和结果过期。

### 7.2 播放状态

状态保持简单且互斥：

```text
Empty → Ready → Planning → Paused ↔ Playing → Completed
                    ↘ Canceled
                    ↘ Faulted
参数或蒙版变化：Paused/Completed → Stale → 重新 Planning
重置：任意非 Empty 状态 → Ready
```

- `预览下一缝`只完成当前步骤的能量和路径，不修改图片；
- `单步`应用已预览路径，然后准备下一步；
- `播放`重复同一条单步用例，并按 50/100/250/500 ms UI 节拍提交轻量预览；
- 播放速度不进入算法 fingerprint；无论速度如何，最终路径和像素必须逐字节一致；
- `暂停`不取消当前不可分割的小阶段，只阻止下一步启动；`取消`立即触发关联 token；
- 不保留所有历史帧；只保留输入、当前图、当前蒙版、当前能量、下一缝、普通对照和必要的插入批次坐标。

### 7.3 Document 并发规则

- 载入、规划、单步/播放、普通对照、导出分别使用明确的取消源或一个受 Session 统一管理的操作取消源；
- 新载入、目标变化、蒙版变化、重置和关闭都递增 generation，并拒绝迟到结果；
- 后台线程只处理领域数据，Avalonia Bitmap 创建和属性提交回 UI 调度器；
- `Dispose` 必须取消并释放所有 token source、当前/旧 Bitmap、计时器和 Session；
- 不使用 `async void` 承载业务异常；命令统一捕获并转换为中文结构化状态。

## 8. 资源、内存和执行时间边界

### 8.1 双重预算

`ImageSize` 的 16,000,000 像素上限只保护单张图片，不能代表 Seam Carving 可安全执行。V1 另设专用预算，
G0 通过真实基准校准后冻结；在校准完成前建议保守默认：

| 预算 | 建议冻结值 | 计算含义 |
| --- | ---: | --- |
| 最大工作图像素 | 2,000,000 | 当前任一步的宽×高 |
| 单次最大总缝数 | 256 | `abs(Δwidth)+abs(Δheight)` |
| 单轴最大变化比例 | 25% | 相对输入该轴；防止极端重复/扭曲 |
| 最大估算单元访问 | 160,000,000 | 各步骤当前像素数之和；插入影子规划也计入 |
| 最大插入路径坐标 | 8,000,000 个 int | 所有未消费批次路径的坐标总数 |
| 最大笔划数 | 512 | 保护、删除和擦除操作合计 |
| 最大快照 Payload | 128 KiB UTF-8 | 超出时拒绝保存并提示合并/清理笔划 |

这些是产品安全边界而非性能承诺。G0 若调整数值，必须以测试机、图片尺寸、缝数、峰值内存和耗时证据更新本表；
后续不得静默提高。超过任一预算必须在分配大数组和启动后台计算前给出实际值、上限和可行建议。

### 8.2 峰值内存估算

计划器必须按最坏同时存活对象估算，而不是只计算 RGBA：

```text
source/current/next RGBA       = pixelCount × 4 × 同时副本数
mask                           = pixelCount × 1
luma/energy/cumulative         = pixelCount × 8 × 同时 double 平面数
predecessor                    = pixelCount × 1
insertion coordinate map       = pixelCount × 4（仅插入规划）
planned seam coordinates       = seamCount × seamLength × 4
preview bitmap                 = 按实际像素格式与行跨度估算
safety margin                  = 上述受控缓冲之和的 25%
```

实现应通过顺序阶段和池化前的明确所有权减少同时副本；V1 不因追求池化而泄漏跨 Session 缓冲。若使用 `ArrayPool<T>`，
必须封装租借/归还、清零敏感区、异常和取消路径，并有专门测试；否则优先普通数组和更保守预算。

### 8.3 取消检查

- 亮度、Sobel、DP、RGBA 搬移、蒙版搬移、参考缩放和预览投影至少每行/列检查一次 token；
- 每条缝的“能量→DP→回溯→应用”阶段之间再次检查；
- 插入影子批次每找到一条缝后检查；
- 序列化和编码沿用已有异步端口；取消后不得发布临时文件；
- 不设置依赖机器速度的硬超时杀线程；使用可预测工作量预算防止不可接受任务进入执行。

## 9. 普通缩放对照与诊断

### 9.1 参考重采样 Strategy

普通对照是一个真实且有限的变化点，允许使用朴素 Strategy：

```text
IReferenceImageResampler
  StableId
  Resize(PixelImage source, ImageSize target, CancellationToken token)

BilinearReferenceResampler
BicubicReferenceResampler
```

只有这两个实现，不增加工厂层；DI 注入 `IEnumerable<IReferenceImageResampler>` 并在用例构造时固化为只读表，应用用例按
稳定 ID 选择并对重复/未知 ID 失败。双线性使用像素中心逆向映射；双三次使用 Catmull–Rom 核 `a=-0.5`、4×4 邻域和
clamp-to-edge。两者都先插值 `[0,1]` Alpha 与预乘 sRGB；双三次加权后把 Alpha clamp 到 `[0,1]`，把每个预乘颜色
clamp 到 `[0,Alpha]`，再反预乘并按第 4.1 节舍入，避免透明边缘色晕和核过冲生成非法 RGBA。

### 9.2 可比较与不可比较的结论

Seam 结果与普通结果尺寸相同，因此可以报告：

- RGB/Alpha MAE、RMSE、最大误差和改变像素数；
- PSNR-Y、PSNR-RGB、全局 SSIM-Y；
- 绝对差异与伪彩热力图；
- 对应像素探针；
- 两种方法耗时、峰值估算、实际缝数和取消状态。

这些指标衡量“两个结果彼此多不同”，没有无失真 ground truth。报告字段必须命名为 `seamVsReference`，UI 使用
“算法间差异”而非“质量提升”。原图与结果尺寸不同，不能直接调用同尺寸质量分析器，也不能静默把结果缩回去制造分数。

## 10. SOLID 分层与朴素模式

### 10.1 分层职责

```text
Domain/SeamCarving
  数值协议、能量、蒙版、路径、DP、删除、插入、资源估算、参考重采样
  只依赖 Domain/Imaging；不知道文件、Avalonia、Document、DI 或 JSON

Application/SeamCarving
  Session、执行计划、准备/预览/单步/播放协调、比较、导出端口
  组合领域服务；不实现公式和像素循环

Infrastructure/SeamCarving
  严格 JSON/CSV DTO 与序列化、文件对话框适配
  不反向定义领域模型

Features/SeamCarving
  Document 状态、命令、取消/generation、Bitmap 生命周期、View 和专用绘制控件
  不实现 Sobel、DP、插入/删除或普通缩放
```

### 10.2 SOLID 门禁

- SRP：能量、路径查找、路径应用、插入规划、资源估算、参考重采样、报告和 UI 各有单一职责；
- OCP：普通对照通过两个窄 Strategy 扩展；核心 Sobel V1 不为假想算法建立万能接口；
- LSP：两个参考重采样器满足相同尺寸、取消、Alpha、舍入和错误契约；契约测试对两者共同执行；
- ISP：准备、规划、预览、单步、比较和导出使用窄用例接口，不建立巨型 `ISeamCarvingService`；
- DIP：Document 只依赖应用用例与 SDK/端口，不 new 编解码器、文件写入器或数值服务；
- 依赖方向测试扫描 Domain 不得引用 `Application`、`Infrastructure`、`Features`、Avalonia 或 Plugin SDK；
- 架构测试检查 Document/AXAML code-behind 不出现 Sobel 核、DP 矩阵或逐像素业务循环。

### 10.3 允许与禁止的模式

允许：

- Strategy：仅用于双线性/双三次两个真实可替换对照算法；
- Session：持有一次 Document 实例的图片、蒙版、计划和当前步骤所有权；
- 不可变 Value Object：尺寸、路径、能量摘要、预算、计划、步骤结果和报告；
- 构造注入：注入无状态服务和窄端口；
- 简单状态枚举：表达播放生命周期。

禁止：

- 为每个 sealed 服务制造接口、Abstract Factory、Builder、Command Bus 或 Visitor；
- 用 Service Locator 从 `IServiceProvider` 动态取算法；
- 用事件总线同步同一个 Document 内本可直接调用的状态；
- 用继承层级表示水平/垂直或删除/插入；优先枚举、值对象和小函数；
- 为“未来可能有更多能量模型”先建立注册中心或插件协议。

## 11. 建议领域模型与应用用例

### 11.1 核心模型

```text
SeamOrientation                 Vertical / Horizontal
SeamOperation                   Remove / Insert
SeamMaskValue                   Normal / Protect / PreferRemoval
SeamAxisOrder                   Auto / WidthFirst / HeightFirst
ReferenceResizeAlgorithm        Bilinear / BicubicCatmullRom

SeamEnergyMap                   尺寸、基础能量、有效能量、摘要、协议 ID
SeamPath                        方向、尺寸、逐主轴坐标、基础/有效累计能量、命中数
SeamMask                        尺寸、紧凑 byte 栅格、stroke fingerprint
SeamBrushStroke                 工具、归一化点列、半径、顺序
SeamResizeRequest               目标尺寸、轴顺序、对照算法
SeamResourceEstimate            单元访问、峰值字节、路径坐标数、阻断原因
SeamResizePlan                  冻结请求、fingerprint、步骤总数和预算
SeamStepPreview                 当前尺寸、步骤序号、能量图、下一缝和诊断
SeamStepResult                  新图片、新蒙版、已应用路径和累计诊断
SeamComparison                  Seam/普通结果、差异投影和质量摘要
```

大数组应由 sealed class 独占并只暴露只读视图；不要把可变 `double[]`、`byte[]` 或 `int[]` 直接交给 View。
Value Object 构造时验证尺寸与长度，fingerprint 使用冻结字段和行优先字节，不使用进程随机 HashCode。

### 11.2 无状态领域服务

```text
SeamLumaProjector               白底 Alpha + BT.601
SobelEnergyCalculator           基础/偏置能量与摘要
MinimumEnergySeamFinder         垂直/水平 DP 和回溯
SeamRemover                     RGBA/蒙版同步删除
SeamInsertionPlanner            影子删除、原坐标映射和有界批次
SeamInserter                    Alpha 安全插值与蒙版同步插入
SeamMaskRasterizer              确定性硬边圆笔刷和 stroke replay
SeamResourceEstimator           计划前工作量/峰值内存估算
BilinearReferenceResampler      普通对照 Strategy
BicubicReferenceResampler       普通对照 Strategy
```

类名是建议落点，G0 可在不改变职责的前提下微调；不得把它们合并成一个数千行 Engine。

### 11.3 窄应用用例

```text
IPrepareSeamCarvingSessionUseCase   解码、校验、初始化 Session 和原图预览
IEditSeamMaskUseCase                栅格化有界笔划并使计划过期
IPlanSeamResizeUseCase              校验目标、轴顺序、预算并创建计划
IPreviewNextSeamUseCase             计算当前能量和下一路径，不修改工作图
IApplySeamStepUseCase               应用且只应用一个已验证步骤
IRunSeamPlaybackUseCase             在取消/暂停边界协调重复单步
ICompareSeamResizeUseCase           生成所选普通结果与算法间诊断
IExportSeamResultUseCase            编码并原子发布当前 PNG
IExportSeamReportUseCase            严格 JSON/CSV 报告
```

播放用例可以接收进度回调提交轻量步骤摘要，但不能向 Domain 注入 Dispatcher，也不能在回调中暴露可变缓冲。

### 11.4 Session 所有权

`SeamCarvingSession` 为 scoped、非线程安全且仅供所属 Document 串行使用，拥有：

- 输入 `PixelImage`、当前 `PixelImage` 和当前 `SeamMask`；
- 输入 fingerprint、mask fingerprint、当前尺寸版本和 generation；
- 当前计划、当前步骤、下一缝预览、插入批次和普通对照；
- 输入/当前/能量/缝/蒙版预览所需的领域数据，不拥有 Avalonia Bitmap；
- 显式 `Reset`、`InvalidatePlan` 和 `Dispose` 语义。

更换图片必须创建新 Session 并释放旧状态。参数和蒙版改变不能偷偷修改已冻结计划；必须失效并重新规划。

## 12. Document、UI 与交互计划

### 12.1 Document 生命周期

- 新建时为空，不自动打开文件对话框；
- 恢复时只恢复轻量意图，不自动读取路径、创建 Session、计算能量或开始播放；
- 载入成功后保存源路径供当前会话显示，但报告默认不写绝对路径；
- `IsDirty` 由轻量参数、笔划和是否存在未保存结果共同决定，加载预览本身不伪造保存完成；
- 目标、顺序、对照算法或笔划变化使计划和结果标记过期，并保留明确提示；
- 只有 Completed 且 fingerprint 匹配当前输入/参数/蒙版的结果可以导出。

### 12.2 建议布局

```text
┌ 顶部命令：载入｜目标宽×高｜轴顺序｜生成计划｜重置｜导出 ┐
├ 左侧：原图/当前图画布 ───────┬ 中部：能量图 + 下一缝 ──────┤
│ 保护/优先删除/擦除笔刷       │ 基础/偏置切换、图例、像素探针 │
├ 底部播放：上一步说明｜单步｜播放/暂停｜取消｜进度/尺寸 ────┤
├ 对照：Seam ｜ 双线性/双三次 ｜ 分割/并排 ｜ 差异热力图 ────┤
└ 右侧诊断：资源预算、缝能量、区域命中、指标、限制与状态 ───┘
```

V1 不实现任意回退一步；“重置”回到输入并可按确定计划重播。按钮和状态文字必须避免让用户误以为暂停等于取消，
或让“下一缝预览”看起来已经修改图片。

### 12.3 专用控件

- `SeamOverlayCanvas`：绘制当前图片、三态蒙版、笔刷光标和下一缝；负责坐标换算，不负责改蒙版；
- `EnergyMapControl`：只消费已投影字节与摘要，提供线性/对数显示和非颜色图例；
- `SeamComparisonControl`：并排或分割显示两个同尺寸结果，复用同步缩放/平移语义；
- 控件依赖属性输入不可变快照，不持有 Session、算法服务或文件路径；
- Headless 测试必须能加载 View、绑定 DataContext、切换核心状态且无动态资源异常。

### 12.4 可访问性与防误解

- 保护区域除绿色外使用斜线纹理，优先删除区域除橙色外使用点纹理，缝另用实线；
- 所有颜色图例同时有文字、形状和数值，不能只靠红/绿区分；
- 焦点顺序、AutomationProperties.Name、键盘单步/播放/暂停和按钮禁用原因必须明确；
- 当前尺寸、目标尺寸、步骤 `n/total`、方向、操作和取消状态用文本实时显示；
- 资源阻断显示“实际值 / 上限 / 建议”，不能只弹“内存不足”；
- 结果区固定显示“内容感知不等于语义理解；保护/删除是有限能量偏置”。

## 13. 持久化、报告与导出

### 13.1 Document 快照

快照 schema 建议为 `image-lab-seam-carving-document-v1`，只保存：

- 源路径字符串、目标宽高、轴顺序、普通对照算法和播放速度；
- 最多 512 条归一化笔划：工具、笔径、点列和顺序；
- 能量显示模式、画布叠加开关和当前 UI 选项；
- 协议版本与资源预算版本。

快照不保存图片像素、蒙版栅格、能量/DP 数组、路径、逐帧结果、Bitmap、Session 或取消状态。恢复后显示“参数已恢复，
请重新载入图片”；只有用户再次显式载入且尺寸匹配时才重放笔划。尺寸不匹配时保留笔划意图但要求用户确认清空或重映射，
不得静默应用到另一尺寸。

### 13.2 PNG 导出

- 只导出当前完成且未过期的完整结果；不覆盖源文件；
- 使用现有 `IImageCodec` 编码与 `IAtomicFileWriter` 原子发布；
- 编码失败、取消或写入失败不得遗留 `.tmp`；
- 导出后可选择执行一次真实 PNG 回读并验证尺寸与 RGBA；是否纳入 V1 在 G6 冻结，若采用必须有自动测试；
- 不提供 JPEG，避免有损编码把算法结果与编码损失混为一谈。

### 13.3 JSON/CSV 报告

报告 schema 建议为 `image-lab-seam-carving-report-v1`，至少包括：

- schema、生成时间、应用版本、能量/插值/预算协议 ID；
- 输入/目标/最终尺寸、输入 fingerprint，不含绝对路径和像素；
- 轴顺序、每轴删除/插入数、实际步骤数、完成/取消/失败状态；
- 蒙版普通/保护/优先删除像素数和笔划摘要，不含完整笔划坐标与栅格；
- 每步或有界抽样步骤的方向、操作、基础/有效累计能量、区域命中和尺寸；
- 为防止 256 步报告无界膨胀，完整逐步记录最多 256 条；V1 总缝数上限与其一致；
- 资源估算与实际计数：峰值估算、单元访问、批次数和取消检查点；
- 所选普通对照、`seamVsReference` 指标和明确的非质量排名说明；
- 警告、预算接近、保护区被穿过、优先删除剩余及解释限制。

JSON 使用固定 camelCase、严格枚举、拒绝未知字段/重复字段/非有限 double/越界数值；CSV UTF-8 BOM、RFC 4180 转义、
InvariantCulture 小数点和固定列序。序列化 DTO 位于 Infrastructure，不把领域模型直接交给反射序列化。

## 14. 中文注释与设计说明要求

新增生产代码的注释必须详细但有信息量，重点说明“为什么”和不易从语法看出的契约：

- `PixelImage` 非预乘语义、白底 Alpha 合成理由和透明隐藏 RGB 的处理；
- Sobel 核、归一化分母、边界规则、能量显示与算法能量的区别；
- 保护/删除是有限偏置而非硬保证，以及固定 `±1000` 的量纲；
- DP 主轴/次轴坐标、前驱表、tie-break、回溯和为何不转置整图；
- 删除/插入的确切坐标、插入批次原坐标映射及偏移修正；
- Alpha 安全插值、全透明结果和舍入规则；
- 资源公式中每个数组的元素类型、同时存活数量和安全余量；
- 取消检查点、generation 防迟到、Session/Bitmap/数组所有权和释放顺序；
- 指标为何只能表达算法间差异，不能得出审美或语义质量结论；
- 朴素模式的取舍：为何只有普通对照使用 Strategy，其他单实现保持 sealed 服务。

以下注释不合格：逐行翻译代码、只写“计算能量”“查找最短路径”、复制方法名、用 TODO 代替协议、用英文缩写而不解释。
每个核心领域文件顶部应有中文设计说明；公式、坐标和危险边界应就近注释。公开/内部核心类型与非直观参数使用中文 XML 文档。

## 15. 单元测试、集成测试与质量门禁

### 15.1 亮度、Sobel 与显示 Golden

- 白、黑、灰、原色、半透明和全透明隐藏 RGB 的亮度 Golden；
- 常量图能量全零；水平/垂直阶跃的 Gx/Gy 方向与归一化值；
- 1×1、1×N、N×1、2×2 和边界 clamp；
- 线性/对数显示不改变领域能量或路径；
- 区域偏置的 `+1000/-1000`、互斥覆盖和非有限拒绝；
- 重复运行逐 double/逐字节相等。

### 15.2 动态规划与路径

- 手算小矩阵的垂直/水平累计值与路径；
- 对 2×2 至受控 5×5 小矩阵穷举所有合法缝，证明 DP 结果等于全局最小；
- 平局前驱和终点选择较小坐标；
- 路径长度、范围、邻接、方向和尺寸版本非法时结构化失败；
- 保护区绕行、删除区优先、不可避免穿越时命中数准确；
- 取消在能量、DP 和回溯边界可观察。

### 15.3 删除、插入与蒙版

- 彩色坐标图片删除垂直/水平缝后的逐字节 Golden；
- 宽/高为 1 的阻断、目标不变克隆、最终尺寸精确；
- 蒙版与 RGBA 使用同一路径同步变形；
- 多条插入缝不重复、原坐标映射和每行偏移正确；
- 左/右/上/下边界插入、Alpha 0/半透明/不透明插值 Golden；
- 插入保护/删除蒙版传播规则；
- 删除再插入不宣称可逆，但必须尺寸恢复、确定且无越界。

### 15.4 计划、预算与播放

- Auto/WidthFirst/HeightFirst 顺序、相同比例宽优先；
- 缝数、变化比例、单元访问、路径坐标和峰值内存估算的 checked Golden；
- 2,000,000 像素、256 缝、25%、160,000,000 单元等边界内/外；最终冻结值以 G0 为准；
- 阻断发生在大数组分配和后台启动前，错误包含实际值与上限；
- 预览不修改图片，单步只改一缝，播放/单步最终逐字节相同；
- 暂停不启动下一步，取消不提交迟到结果，重置恢复输入；
- 播放速度不改变算法 fingerprint 或最终像素；
- 参数、笔划、换图、关闭和 generation 使旧计划/结果失效。

### 15.5 普通对照与指标

- 双线性恒定图、角点、中心、缩小、放大和目标 1×1 Golden；
- Catmull–Rom 权重和为 1、常量保持、边界 clamp 与 byte Golden；
- 两个 Strategy 共用的尺寸、取消、Alpha、舍入和非法输入契约测试；
- Seam 与普通结果尺寸严格相同后才能比较；尺寸不同时结构化拒绝；
- 相同图片差异为零、PSNR 无穷语义沿用既有契约、热力图尺寸正确；
- UI/报告使用 `seamVsReference` 和“算法间差异”，不出现“提升率”“最佳质量”。

### 15.6 用例、持久化、Document 与 UI

- Session 换图、Reset、Invalidate、Dispose 和多实例隔离；
- 快照最多 512 笔划、128 KiB、无 RGBA/energy/cumulative/mask raster/path；恢复不自动 IO 或执行；
- JSON/CSV schema、严格枚举、未知/重复字段、非有限数、BOM、转义、文化区域和隐私；
- PNG 只允许 Completed 且未过期结果，取消不发布临时文件；
- 第十六个稳定 Persistable Document、Scoped Document、singleton 无状态服务和 ID 唯一；
- Headless View、专用控件、关键绑定、Automation 名称、纹理图例和窄窗口布局；
- 架构依赖方向、核心中文注释、NuGet 锁文件不新增依赖、无 AIFLOW/Windows workflow；
- Standalone 从真实 Module 解析真实 Document/View，不复制业务。

### 15.7 测试数据与性能门禁

- 所有算法测试使用代码构造的微型矩阵、条纹、棋盘、透明边缘、低能量走廊和保护/删除区域；
- Golden 数据注明来源与手算方法，不下载不明版权图片，不依赖网络；
- 资源门禁断言数组长度、步骤数、预算公式和分配前阻断，不使用依赖机器速度的毫秒阈值；
- 增加一个显式、默认可运行的中等尺寸压力用例，验证取消和有界峰值模型，但不把 16 MP 长测伪装成单元测试；
- 不允许 `[Fact(Skip=...)]`、条件跳过、删除历史测试、放宽断言或只跑新测试文件；
- G9 必须记录实施前后总数、净增数、0 失败、0 跳过、0 警告和 0 错误。

### 15.8 本地开发门禁命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

这些是本地开发门禁，不是 Windows CI 或发布门禁。本阶段禁止新增 GitHub Actions/Azure Pipelines Windows job，
也不执行 `dotnet publish`、插件 ZIP、真实 Host、安装、升级、卸载或发布验收。发布时再按公共发布文档单独授权和执行。

## 16. G0–G9 交付与验收

### G0：产品、数值与资源基线

- 冻结第 1–8 节协议、稳定 ID 候选、预算值和 Golden 样本；
- 实跑起始 locked restore、Debug/Release build/test；
- 建立 `history/g0-product-math-and-baseline.md`，记录机器、命令、520/520 起点与预算校准；
- 验收：所有开放问题归零，计划不依赖 AIFLOW、Windows CI 或发布流程。

### G1：亮度、Sobel、蒙版偏置与投影

- 实现 `SeamLumaProjector`、`SobelEnergyCalculator`、能量摘要和预览投影；
- 完成 Alpha、边界、公式和区域偏置 Golden；
- 验收：Domain 不依赖 Avalonia，显示映射不改变路径输入。

### G2：动态规划与删除

- 实现 `SeamPath`、`MinimumEnergySeamFinder` 和 `SeamRemover`；
- 完成穷举最优性、tie-break、水平/垂直、非法路径和删除 Golden；
- 验收：同输入重复结果完全一致，每行可取消。

### G3：蒙版编辑与插入

- 实现 stroke 模型/栅格化、同步蒙版变形、影子删除规划和 Alpha 安全插入；
- 完成批次坐标、边界、透明像素、保护/删除传播和预算测试；
- 验收：多缝插入无重复选择陷阱，最终尺寸精确。

### G4：计划、预算与播放

- 实现双轴计划、资源估算、Session、预览/单步/播放和状态机；
- 完成暂停、取消、重置、generation、过期和播放速度等价测试；
- 验收：不保存全部帧，超预算在分配前阻断。

### G5：普通缩放与比较

- 实现双线性/双三次两个窄 Strategy 和 Alpha 契约；
- 复用质量/差异服务并固定 `seamVsReference` 解释；
- 若提取共享双线性，必须用 Robustness 回归证明既有输出未变；
- 验收：两种输出同尺寸，UI/报告不作质量排名。

### G6：用例、报告与导出

- 完成所有窄用例、严格 JSON/CSV、PNG 原子导出和失败清理；
- 完成快照 512 笔划/128 KiB 边界、隐私、非有限数和取消测试；
- 验收：Domain 不直接序列化，报告不含绝对路径/像素/完整蒙版。

### G7：Document 生命周期与组合

- 增加 `PluginIds.SeamCarvingDocument`、服务登记、Persistable Document 和 Standalone 入口；
- 更新组合测试为十六个 Document，证明 scoped 隔离与 singleton 算法复用；
- 完成恢复不自动 IO、关闭释放、迟到拒绝和 Bitmap 生命周期；
- 验收：Module 不增加 Tool、Workflow Action 或 Workbench Command。

### G8：UI、解释与专用文档

- 完成 View、三个专用控件、键盘/可访问性、中文限制与人工检查；
- 建立本专题统一文档目录和阶段历史；同步根 README、docs 索引和未来能力状态；
- 验收：Headless 加载通过，窄窗口可用，红绿之外仍可辨认。

### G9：本地封板

- 复跑第 15.8 节全部本地命令，确认 Debug/Release 0 警告/0 错误、全部测试通过、0 跳过；
- 复核锁文件、依赖、中文注释、架构扫描、文档链接和 git diff；
- 记录已证明/未证明事项和所有延期发布项；
- 验收：只声明“生产实现与本地自动门禁完成”，不声明发布完成。

## 17. 预计代码与测试落点

### 17.1 生产代码

```text
src/ImageLabPlugin.Plugin/
  Domain/SeamCarving/
    SeamCarvingModels.cs
    SeamLumaProjector.cs
    SobelEnergyCalculator.cs
    SeamMaskRasterizer.cs
    MinimumEnergySeamFinder.cs
    SeamRemover.cs
    SeamInsertionPlanner.cs
    SeamInserter.cs
    SeamResourceEstimator.cs
    ReferenceImageResamplers.cs
  Application/SeamCarving/
    SeamCarvingContracts.cs
    SeamCarvingSession.cs
    SeamCarvingUseCases.cs
  Infrastructure/SeamCarving/
    SeamCarvingReportSerializer.cs
  Features/SeamCarving/
    SeamCarvingDocument.cs
    SeamCarvingView.axaml
    SeamCarvingView.axaml.cs
    SeamOverlayCanvas.cs
    EnergyMapControl.cs
    SeamComparisonControl.cs
  Constants/PluginIds.cs
  Plugin/ImageLabPluginServices.cs
  Plugin/ImageLabPluginModule.cs

src/ImageLabPlugin.Standalone/
  MainWindow.axaml
  MainWindow.axaml.cs
```

文件可在 G0/G1 按职责合并，但核心数值、应用协调和 UI 不能跨层混写。若单文件明显承担两个变化原因，应优先拆分；
不得为了与清单逐字一致制造空壳文件。

### 17.2 测试

```text
tests/ImageLabPlugin.Tests/
  SeamEnergyTests.cs
  MinimumEnergySeamTests.cs
  SeamRemovalAndInsertionTests.cs
  SeamMaskTests.cs
  SeamPlanningAndBudgetTests.cs
  SeamPlaybackUseCaseTests.cs
  SeamReferenceResizeTests.cs
  SeamReportAndArchitectureTests.cs
  SeamCarvingDocumentTests.cs
  SeamCarvingViewTests.cs

  CompositionAndPersistenceTests.cs       # 更新为十六个 Document 与快照边界
  ImageCodecAndUseCaseTests.cs             # 真实 View/Standalone 对象图回归
  RobustnessOperatorTests.cs               # 仅在共享双线性提取时增加协议回归
```

测试按职责组织，不要求一个类对应一个文件。每个阶段先增加失败测试，再实现；不得在 G9 才集中补门禁。

## 18. 专用文档与同步范围

实施时按现有能力惯例建立并维护：

```text
docs/design/seam-carving/
  README.md
  user-manual.md
  guide.md
  mathematical-principles.md
  report-schema.md
  testing.md
  implementation.md
  history/
    README.md
    g0-product-math-and-baseline.md
    ...
    g9-local-sealing.md
```

职责固定：

- `README.md`：状态、阅读顺序、能力和解释边界；
- `user-manual.md`：从载入、画区域、预览缝到播放/导出的新手步骤；
- `guide.md`：参数、状态、预算、取消、快照和错误语义；
- `mathematical-principles.md`：Alpha/亮度、Sobel、DP、插入规划、双线性/双三次；
- `report-schema.md`：严格 JSON/CSV、字段、N/A、隐私和版本；
- `testing.md`：每阶段真实命令、总数、性能边界、已证明和未证明；
- `implementation.md`：本冻结设计与实际落地差异；
- `history/`：每个 Gate 的证据，不作为当前用户入口。

同步更新 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md` 和公共边界文档中确实受影响的
条目。实施阶段已在代码和门禁实际完成后创建说明书、测试证据和历史，没有用计划数字冒充实跑结果。

## 19. 有限人工验收清单

- 载入低纹理背景+高对比主体图片，缩窄时下一缝优先走低能量背景；
- 保护主体后重播，相同目标下缝明显绕行，并显示保护命中为 0 或解释不可避免命中；
- 标记优先删除条带，缝首先穿过该区域；移除范围不足时不宣称对象已删除；
- 分别执行宽度删除、高度删除、宽度插入、高度插入和双轴混合；最终尺寸准确；
- 单步与播放得到逐字节相同结果；暂停、继续、取消、重置状态清楚；
- 插入透明边缘不出现由隐藏 RGB 引起的明显彩边；
- 切换双线性/双三次，普通对照变化但 Seam 结果不被重算；
- 能量线性/对数显示变化不影响下一缝；
- 超像素、超缝数、超比例、超访问量和超快照大小显示实际值/上限/建议；
- 两个 Document 并行使用互不污染；关闭播放中的 Document 后无迟到 UI 更新；
- 导出 PNG 尺寸正确且不覆盖源；JSON/CSV 不含绝对路径、像素和完整蒙版；
- 100%、125%、150% DPI 和窄窗口下主要命令可访问，键盘焦点和纹理图例可辨认。

人工验收不替代自动测试，也不把自然图片主观观感写成算法正确性证明。

## 20. 回滚与兼容策略

- 新能力使用独立稳定 Document ID、独立快照/report schema 和独立目录，不修改既有文档快照；
- G1–G6 在接入 Module 前可独立回滚；G7 已整体登记第十六个 Document，不能保留指向不存在 View 的半成品入口；
- 一旦 G7 接入，若组合失败，整体移除新注册和 ID 引用，不保留指向不存在 View 的半成品入口；
- 若共享双线性提取导致 Robustness 回归，回退共享提取，Seam 内保留窄实现，不能改变既有扰动协议；
- schema 只向后新增可选字段；改变能量、tie-break、Alpha、插入或预算语义必须升级协议 ID/schema；
- 未完成运行、缓存、像素和 Bitmap 从不进入持久化，因此恢复不依赖内部数组布局。

## 21. 完成定义

只有同时满足以下条件，V1 才能标记为“生产实现与本地自动门禁完成”：

- 第 3.1 节全部能力落地，第 3.2 节非目标未被偷渡；
- SOLID 依赖方向、朴素模式和单一职责通过架构测试与人工复核；
- 核心生产代码具有详细中文设计注释，公式、坐标、边界、所有权和取消无含糊处；
- Sobel、DP、删除、插入、蒙版、预算、播放、普通对照和报告均有确定性自动测试；
- 超预算在分配前结构化阻断，取消/关闭无迟到提交，不常驻全部帧；
- 第十六个 Persistable Document、快照、多实例、DI、Standalone 和 Headless View 全部通过；
- 专用文档、总索引、未来能力状态和阶段历史同步；
- Debug/Release locked restore/build/test 实跑全绿、0 跳过、0 警告、0 错误；
- 文档明确列出未执行的真实 Host、ZIP、Windows CI 和发布验收。

计划文档完成、Demo 能运行或少量样例视觉良好，都不能单独满足完成定义。

## 22. 发布阶段明确延期

以下事项本轮不做，也不进入本地完成声明：

- Windows CI runner 和任何新 workflow；
- `dotnet publish`、插件 ZIP、manifest/产物审计；
- 真实 MyAvaloniaManagement Host 加载、停靠、布局恢复和多窗口验证；
- 安装、升级、卸载、回滚和签名；
- 16 MP 长时间压力、不同 GPU/DPI/系统区域和低内存机器矩阵；
- 发布安全审查、发布说明和正式兼容性声明。

进入发布阶段时，必须由用户另行授权，再按 `docs/design/shared/deployment-and-release.md` 执行。不得把本计划的
Debug/Release 本地构建误写成发布门禁已完成。

## 23. G0 前必须关闭的校准项

以下不是产品方向开放项，而是必须用基准和 Golden 证据冻结的数值：

1. 专用最大工作像素、最大单元访问和峰值字节上限是否采用第 8.1 节建议值；
2. 插入批次的最大缝数，以及路径坐标上限如何同时约束横/竖两种方向；
3. Catmull–Rom Alpha 安全采样的逐字节 Golden 与现有双线性回归边界；
4. PNG 导出是否强制真实回读自检；
5. 笔划点列的采样/简化规则与 128 KiB 快照上限；
6. 中等尺寸压力用例的固定尺寸和工作量，使其稳定验证取消但不采用毫秒断言；
7. 能量图预览最大边和 Bitmap 同时存活数量，以纳入峰值估算。

G0 必须把校准结论、测试机事实、命令与调整原因写入阶段历史；未关闭前不得进入 UI 实现。
