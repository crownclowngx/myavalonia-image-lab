# ImageLabPlugin V1 Poisson Blending／梯度域融合设计与实施计划

> 计划状态：V1 生产实现与本地自动门禁已完成；不代表发布完成<br>
> 基线日期：2026-08-31<br>
> 产品名称：Poisson Blending／梯度域融合<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 起始证据：locked restore 成功；Debug 0 警告、0 错误；587/587 测试通过、0 失败、0 跳过<br>
> 核心路线：二值源遮罩 + 整数目标偏移 + 线性 sRGB + 4 邻域离散泊松方程 + 确定性红黑 Gauss–Seidel + 有界收敛观察 + 直接 Alpha 对照<br>
> 首要规定：SOLID 是所有实现取舍的第一约束；设计模式只用于真实变化点并保持朴素；新增生产代码必须使用详细中文注释解释公式、坐标、边界、所有权、取消和设计思路；不使用 AIFLOW；不新增 Windows CI；本阶段不执行 ZIP、真实 Host、安装或任何发布门禁

本文是 ImageLab 第十六项产品能力、第十七个多实例 Persistable Document 的实施基线与落地记录。产品让用户从源图中
显式选择一个区域，把它平移到目标图中的整数位置，并比较直接 Alpha 合成与梯度域融合在边界颜色、亮度和纹理上的差异。

它是可解释的教学与实验工具，不是 Photoshop/OpenCV 的完全兼容替代品，也不是自动抠图、语义分割、图像配准或
内容生成工具。V1 不自动判断“应该融合什么”，不承诺任意遮罩都能得到自然结果；收敛只证明离散方程残差达到阈值，
不证明主观视觉质量更好。

## 0. 计划摘要

### 0.1 当前结论

- 当前仓库已有十六项能力、十七个 Persistable Document，本能力已进入生产代码、Module 和 Standalone；
- 起始基线于 2026-08-31 实跑：locked restore 成功，Debug build 0 警告/0 错误，587/587、0 跳过；最终 Debug/Release 为 629/629；
- V1 使用两张用户显式选择的图片：源图提供被选择区域和梯度，目标图提供放置位置与 Dirichlet 边界；
- 源区域由二值画笔/橡皮和矩形选择形成，目标位置只允许整数像素平移，不做缩放、旋转或透视；
- 数值计算在线性 sRGB 中进行，输入/输出仍使用现有非预乘 RGBA8888 `PixelImage`；
- V1 要求源遮罩及一像素源邻域、映射后的目标区域及一像素目标邻域均完全不透明；不满足时在求解前结构化阻断；
- 三种模式为普通克隆、混合梯度和单色融合；直接 Alpha 合成始终作为同位置、同遮罩的诚实对照；
- 求解器采用确定性红黑 Gauss–Seidel，固定遍历、停止条件和快照规则；不把 UI 刷新节拍混入算法；
- 只保留当前迭代解、残差序列和少量有界预览，不保存全部完整尺寸迭代帧；
- G0–G9 只执行本地开发门禁；Windows CI、真实 Host、ZIP、安装和发布验收全部延期。

### 0.2 固定实施顺序

1. G0 冻结产品语义、颜色/Alpha 协议、离散方程、Golden 样本和资源预算；
2. G1 完成源遮罩、坐标映射、二值区域拓扑与输入验证；
3. G2 完成三种 guidance field Strategy、散度/RHS 和可视化投影；
4. G3 完成确定性红黑 Gauss–Seidel、残差、停止原因、取消和收敛快照；
5. G4 完成线性光直接 Alpha 对照、结果合成、差异与质量诊断；
6. G5 完成 Session、窄用例、预算、generation 和生命周期；
7. G6 完成严格报告、PNG/JSON/CSV 导出和轻量快照；
8. G7 接入第十七个 Persistable Document、DI、Module 和 Standalone；
9. G8 完成可访问 UI、专用文档和有限人工验收；
10. G9 复跑 Debug/Release 全量本地门禁并完成本地开发封板。

不得先在 View、Control、Document 或 code-behind 中写遮罩栅格化、梯度选择、Poisson RHS 或迭代循环，再把它称为
领域实现。公式、坐标、颜色空间、边界条件、停止条件和资源公式必须先在 Domain 中冻结并通过自动测试。

## 1. 产品形态与用户闭环

### 1.1 产品决策

| 决策 | V1 固定结论 |
| --- | --- |
| 产品名称 | `Poisson Blending／梯度域融合` |
| Host 形态 | 多实例 `Persistable Document`，不是 singleton Tool |
| 稳定 ID 候选 | `myavalonia.plugin.image.lab.document.poisson-blending`；只在 G7 实际接入后成为持久身份 |
| 显示名称 | `梯度域融合` |
| 显示分类 | `图像分析` |
| 输入 | 一张源图、一张目标图，均由用户显式选择，解码为现有非预乘 RGBA8888 `PixelImage` |
| 源区域 | 二值遮罩；矩形选择、添加画笔、擦除画笔、清空；笔划用归一化源坐标保存 |
| 目标位置 | 整数像素偏移 `(dx, dy)`；可拖动，也可精确输入；不缩放、不旋转 |
| 输出 | 与目标图尺寸严格一致的新 RGBA8888 图片；不覆盖源图或目标图 |
| 求解域 | 映射到目标图的二值区域 `Ω`；4 邻域；Dirichlet 边界取目标图颜色 |
| 工作颜色 | IEC 61966-2-1 sRGB 解码到线性 RGB double；完成后编码回 sRGB byte |
| Alpha | V1 求解域及一像素 halo 必须完全不透明；结果保留目标 Alpha，域内自然为 255 |
| 求解器 | 固定顺序红黑 Gauss–Seidel；目标颜色初始化；双残差停止；有界最大迭代 |
| 模式 | 普通克隆、混合梯度、单色融合 |
| 对照 | 同遮罩、同偏移、线性光直接 Alpha 合成；不把对照称为“错误算法” |
| 可视化 | 源/目标/遮罩、guidance 梯度、散度/RHS、当前解、残差热图、收敛曲线 |
| 导出 | 完成且未过期的 Poisson/Alpha PNG；版本化 JSON/CSV 实验报告 |
| 外部依赖 | V1 不新增 NuGet、OpenCV、原生库、GPU 或图表框架 |
| 设计模式 | 三种 guidance 使用一个窄 Strategy；其余使用不可变值对象、sealed 服务、窄用例和构造注入 |
| 明确排除 | AIFLOW、Workflow Action、Workbench Command、Windows CI、ZIP、真实 Host 与发布门禁 |

### 1.2 用户闭环

```text
显式选择源图和目标图
    ↓
在源图上用矩形或画笔建立二值遮罩，并查看像素数、连通分量和边界
    ↓
把遮罩轮廓拖到目标图，或输入精确整数偏移
    ↓
选择普通克隆、混合梯度或单色融合
    ↓
预检透明度、边界、空洞、未知量、内存和工作量；超预算在分配前阻断
    ↓
查看源/目标 guidance、散度和初始残差
    ↓
单步迭代，或运行／暂停／取消；观察残差曲线、热图和有界快照
    ↓
比较直接 Alpha 与梯度域结果，检查边界 guidance 误差、裁剪数和停止原因
    ↓
导出完整 PNG 或不含原图像素、绝对路径和完整遮罩栅格的 JSON/CSV 报告
```

### 1.3 前置稳定性要求

本能力只能在以下现有边界不被破坏的前提下实施：

- 图片选择、解码上限、PNG 编码和原子写入继续由现有端口负责；
- Seam Carving 已验证的归一化笔划、Bitmap 生命周期、Document Scope、取消和 generation 惯例可作为参考；
- `PixelImage`、`ImageSize`、`SrgbColorSpace`、差异投影和 `FullReferenceQualityAnalyzer` 仍是共享事实来源；
- 任何共享提取都必须用既有能力回归测试证明协议未变化；不得为了“复用”直接依赖另一个 Document 或 ViewModel。

## 2. 当前项目事实与复用边界

### 2.1 已验证基线

实施起点具备：

- 唯一真实插件程序集 `ImageLabPlugin.Plugin` 和只用于本地开发的 `ImageLabPlugin.Standalone`；
- 十五项产品能力、十六个多实例 Persistable Document；没有 Tool、AIFLOW、Workflow Action 或 Workbench Command；
- `Domain`、`Application`、`Infrastructure`、`Features` 和 `Plugin` 的既有依赖方向；
- 非预乘 RGBA8888 `PixelImage`、`ImageSize.MaximumPixelCount = 16_000_000` 和 checked 尺寸校验；
- sRGB/颜色、图片代理、PNG/JPEG 解码、PNG 编码、原子写入、文件对话框与完整参考比较服务；
- Seam Carving 的二值/三态遮罩经验、归一化点列、资源预算、逐步播放和专用控件经验；
- 2026-08-31 实跑 locked restore、Debug warn-as-error build 和 Debug test：0 警告、0 错误、587/587、0 跳过。

587/587 是本计划起点，不是 Poisson Blending 的完成证据。后续每个 Gate 必须记录真实新增测试、总数、失败、跳过、
警告和错误；不得预填完成测试数，也不得把自然图片观感代替数值证据。

### 2.2 必须直接复用

- 源图/目标图载入、PNG 导出、原子发布和文件对话框复用现有应用端口；
- 完整图片继续使用 `PixelImage`，不建立第二套 RGBA 容器或 Bitmap 作为领域模型；
- sRGB byte 与线性 RGB 的转换优先复用 `SrgbColorSpace`；若现有 API 不足，只增加与颜色空间职责一致的窄方法；
- Poisson 与 Alpha 输出同尺寸后，复用既有完整参考质量和差异投影；
- Standalone 必须经真实 Module/DI 解析真实 Document 和 View，不复制演示业务；
- 无状态数学服务为 singleton；源图、目标图、遮罩、问题、当前解、残差、Bitmap 和取消源归各 Document Scope 独占。

### 2.3 允许的共享改进

归一化笔划坐标、像素坐标换算和 RGBA 预览投影若已出现第二个真实消费者，可提取到 `Domain/Imaging` 的窄模型。
提取必须保持 Seam Carving 的序列化、栅格化和测试结果不变；否则 Poisson 先保留自己的小型实现。

建议共享边界只有：

```text
SrgbColorSpace
  负责 sRGB 编解码，不知道遮罩、Poisson 或 Document

FullReferenceQualityAnalyzer
  只比较同尺寸 PixelImage，不知道哪种融合“更自然”

IImageDecoder / IImageEncoder / IAtomicFileWriter
  只处理 IO，不理解迭代、梯度或融合模式
```

### 2.4 禁止的错误复用

- 不让新 Document 调用 Seam Carving、Image Compare 或 Color Transfer Document；
- 不复用 View、ViewModel、Control 或快照类型作为领域输入；
- 不把卷积实验台的展示梯度当作 Poisson guidance；数值协议应独立、明确且可测试；
- 不在 UI 层构造稀疏方程、计算散度、选择混合梯度或执行迭代；
- 不建立通用矩阵框架、通用 PDE 框架、反射算法目录、Mediator、Event Bus、Repository、DAG 或脚本层；
- 不为只有一个实现的遮罩验证器、问题构造器、求解器或投影器建立接口与工厂；
- 不复制整幅稀疏矩阵；V1 使用遮罩索引和邻接关系隐式计算；
- 不静默缩小图片、降采样遮罩、减少迭代或改变容差来绕过预算。

## 3. V1 范围、非目标与解释边界

### 3.1 V1 必须完成

- 两张显式图片输入；源/目标可交换，但交换会清除旧问题和结果；
- 矩形选择、添加画笔、擦除画笔、清空遮罩；显示轮廓、面积、包围盒、连通分量和孔洞数；
- 目标轮廓拖动和精确 `(dx, dy)` 输入；拖动结束才重建问题，拖动中只画轻量轮廓；
- 映射前预检源一像素 halo、目标一像素 halo、完全不透明、非空、尺寸和预算；
- 普通克隆、混合梯度和单色融合三种模式；模式切换使旧问题/结果过期；
- 显示源梯度、目标梯度、选定 guidance、散度/RHS 和残差热图；显示投影不能改变数值输入；
- 初始状态、单次 sweep、连续运行、暂停、继续、取消、重置；
- 显示迭代数、RMS 残差、最大绝对残差、相对下降、停止原因和裁剪计数；
- 直接 Alpha 与 Poisson 的同步视图、并排/分割线、差异图、边界 guidance RMSE 和完整参考统计；
- 超预算前置阻断、运行时计数、每 sweep/行取消、generation 防迟到和关闭释放；
- 完整尺寸 Poisson/Alpha PNG、版本化 JSON/CSV 报告、轻量 Document 快照；
- 多实例隔离、恢复不自动读取图片或求解、结果过期规则和中文限制说明；
- Debug/Release locked restore、warn-as-error build、全部自动测试、0 跳过和文档同步。

### 3.2 V1 明确不实现

- AI 抠图、语义分割、GrabCut、智能选区、自动目标检测或自动推荐落点；
- AIFLOW、Workflow Action、Workbench Command、脚本、宏或批处理；
- 源区域缩放、旋转、镜像、透视、非刚性变形或自动图像配准；
- 羽化/软遮罩参与 Poisson 方程、透明梯度域融合、求解 Alpha 通道或保留隐藏 RGB；
- 源/目标求解 halo 含半透明像素；V1 必须明确阻断并建议先展平到不透明背景；
- 8 邻域、各向异性权重、屏蔽泊松、周期边界、Neumann 边界或多重网格；
- GPU、SIMD 专项版本、unsafe、原生库、OpenCV 或新增第三方求解器；
- 稀疏矩阵显式组装、LU/Cholesky、共轭梯度、多重网格或算法自动切换；
- 无界迭代、保存全部完整尺寸迭代帧、撤销每个 sweep 或把残差曲线写入 Document 快照；
- 多图批处理、覆盖输入文件、JPEG/WebP/AVIF 结果导出；
- Windows CI、真实 Host、ZIP、安装/升级/卸载、签名和发布门禁。

### 3.3 解释边界

- 泊松融合尽量匹配区域内部梯度，并由目标边界决定整体颜色常量；它不会复制源图绝对颜色；
- 普通克隆可能发生整体色偏，尤其当目标边界与源区域平均亮度差异很大；这是算法语义，不一定是缺陷；
- 混合梯度按每条边选择更强的整 RGB 梯度，可保留目标纹理，也可能引入不希望的目标边缘；
- 单色融合只迁移源亮度细节并尽量保留目标色彩倾向，不等于把源图简单灰度化；
- 多连通分量和孔洞在数学上允许，但会增加解释复杂度；UI 必须显示拓扑统计；
- 达到残差阈值只表示离散线性系统收敛；没有达到阈值时可显示预览，但不得标记为完成或允许正式导出；
- 直接 Alpha 和 Poisson 优化目标不同，PSNR/SSIM/MAE 只能表达二者差异，不能证明哪一个主观上更好；
- 输出裁剪到可表示色域会破坏精确方程关系，必须记录裁剪像素/通道数并显示警告。

## 4. 坐标、遮罩与有效区域协议

### 4.1 坐标定义

- 源像素坐标为 `(sx, sy)`，左上角是 `(0,0)`，x 向右、y 向下；
- 目标偏移 `(dx, dy)` 定义为源坐标到目标坐标的平移：`tx = sx + dx`、`ty = sy + dy`；
- 遮罩只存在于源图坐标系，`Ωs = { p | mask[p] = 1 }`；目标求解域为 `Ωt = Ωs + (dx,dy)`；
- View 的缩放、滚动、DPI 和 letterbox 不进入领域坐标；专用控件必须把指针位置明确换算为归一化源坐标；
- 所有位置最终量化为整数像素，使用固定的 `MidpointRounding.ToEven`；拖动预览与提交坐标必须一致。

### 4.2 二值遮罩

- `0` 表示域外，`1` 表示域内；V1 不允许 0 到 1 之间的软权重；
- 矩形选择写入闭开区间 `[left,right) × [top,bottom)`；宽或高为零时不产生选择；
- 添加/擦除画笔使用归一化中心点列和归一化半径，栅格化规则、圆盘覆盖和边界 clamp 必须冻结；
- 后画笔划覆盖先画笔划；清空移除全部笔划；不保存完整遮罩栅格；
- 建议上限为 512 条笔划、每条 2,048 点、序列化后 128 KiB；G0 用真实快照校准并冻结；
- 遮罩 fingerprint 必须包含源图 fingerprint、笔划、矩形、栅格协议和源尺寸。

### 4.3 halo 与映射预检

对每个 `p ∈ Ωs` 及其 4 邻域 `N4(p)`：

- `p` 与 `N4(p)` 都必须落在源图内，因此遮罩不能触碰源图最外一圈；
- 映射后的 `p+d` 与 `N4(p+d)` 都必须落在目标图内，因此目标轮廓不能触碰目标图最外一圈；
- 上述源/目标像素 Alpha 必须全部等于 255；任一不满足时，不分配求解数组并返回坐标、实际值和建议；
- 遮罩至少包含 1 个像素；单像素、小连通分量、带孔区域均有定义并必须测试；
- 4 邻域连通分量与孔洞只作为诊断，不自动修改用户遮罩。

这一限制故意把 V1 的数学问题限定为不透明 RGB Dirichlet 问题。以后若支持透明度，应建立独立协议与测试，
不能在现有求解器中悄悄加入 Alpha 特例。

## 5. 颜色空间与像素协议

### 5.1 线性 sRGB

每个不透明 RGB byte 先归一化到 `[0,1]`，再使用标准 sRGB 分段传递函数解码为线性值：

```text
Cs = byte / 255

若 Cs <= 0.04045：
  Clinear = Cs / 12.92
否则：
  Clinear = ((Cs + 0.055) / 1.055) ^ 2.4
```

求解结束后使用精确反函数编码回 sRGB，clamp 到 `[0,1]`，乘 255 并按 `ToEven` 舍入。协议 ID 候选：
`poisson-linear-srgb-dirichlet-rbgs-v1`。

- 计算全程使用 `double`；不得在每次 sweep 中量化为 byte；
- 非有限输入、RHS、解或残差立即失败，不能替换为 0 继续；
- 域外输出逐字节复制目标图；域内 RGB 来自求解结果，Alpha 保持目标的 255；
- 报告记录求解前/后的最小最大值、低端/高端裁剪通道数和涉及像素数；
- 预览 Bitmap 只能消费投影后的 byte，不得反向成为下一次迭代输入。

### 5.2 单色亮度

单色模式使用线性 BT.709 亮度：

```text
Y = 0.2126 Rlinear + 0.7152 Glinear + 0.0722 Blinear
```

求解得到 `Ysolve` 后，对目标像素增加同一个亮度差：

```text
delta = Ysolve - Ytarget
Rout = Rtarget + delta
Gout = Gtarget + delta
Bout = Btarget + delta
```

因为三个权重之和为 1，上式在未裁剪时精确产生目标亮度 `Ysolve`，同时保留目标 RGB 通道差。发生 gamut clamp 时必须计数，
不得把裁剪后的结果描述为严格保色。

## 6. 离散泊松问题

### 6.1 优化目标

令 `EΩ` 为“至少一个端点位于 `Ωt`”的无向 4 邻边集合。每条边只计一次，并固定从坐标较小端指向较大端，
寻找输出 `f`：

```text
min Σ({p,q}∈EΩ) |(f_p - f_q) - v_pq|²
```

其中 `v_pq` 是与固定边方向一致的 guidance。域外相邻像素固定为目标图 `t_q`，形成 Dirichlet 边界。
求每个未知量的偏导后，同一条内部边会分别进入两个端点的像素方程，但在能量目标中没有被重复加权。

### 6.2 线性方程

对每个未知像素和每个求解通道：

```text
4 f_p - Σ(q∈N4(p)∩Ωt) f_q
  = Σ(q∈N4(p)) v_pq + Σ(q∈N4(p)\Ωt) t_q
```

- V1 通过 halo 预检保证每个像素都有四个有效邻居，因此对角系数固定为 4；
- 不显式构造 `N×N` 稀疏矩阵；问题对象保存紧凑域索引、每个未知的四邻接索引/边界值和 RHS；
- 对 RGB 模式有三个 RHS/解通道，对单色模式只有一个亮度 RHS/解通道；
- 邻边按 `左、右、上、下` 固定顺序累加，避免遍历顺序随集合实现变化；
- 同一内部边会进入两个端点各自的像素方程，这是离散公式的一部分；问题构造器不得把其中一个端点漏掉。

### 6.3 问题 fingerprint

`PoissonProblemFingerprint` 至少包含：

- 源图/目标图内容 fingerprint 与尺寸；
- 遮罩 fingerprint、目标偏移和拓扑摘要；
- guidance 模式、颜色协议、邻域协议和边界协议；
- 容差、最大迭代、快照策略不进入方程 fingerprint，但进入运行 fingerprint；
- 任何输入、遮罩、偏移或模式变化都使问题、迭代和结果过期。

## 7. 三种 guidance Strategy

三种模式是唯一真实算法变化点，使用一个窄接口 `IPoissonGuidanceStrategy`。接口只接收一条有向邻边的源/目标
线性颜色并返回 guidance，不知道 UI、Session、迭代器或文件。通过 `Mode` 稳定枚举选择 Strategy；不建立抽象工厂。

### 7.1 普通克隆

对每个 RGB 通道：

```text
v_pq = S_p - S_q
```

- `S` 取源图对应坐标；目标只提供边界；
- 有向反边满足 `v_qp = -v_pq`，使用 Golden 测试验证；
- 适合源区域内部纹理明确、希望由目标边界吸收整体颜色差的场景。

### 7.2 混合梯度

先计算整条 RGB 边的平方模：

```text
gs = S_p - S_q
gt = T_p - T_q

若 dot(gs, gs) >= dot(gt, gt)：v_pq = gs
否则：                         v_pq = gt
```

- 选择单位是整 RGB 向量，不是三个通道分别择强，避免同一条边拼出原图中不存在的颜色方向；
- 完全相等时固定选择源梯度；不得使用 epsilon、随机数或平台相关排序；
- `T_p/T_q` 使用映射后目标坐标；
- 报告记录选择源/目标梯度的有向边数量和比例，不据此判断质量。

### 7.3 单色融合

使用源图线性 BT.709 亮度构造标量 guidance：

```text
v_pq = Ysource_p - Ysource_q
```

边界值使用目标亮度。求解完成后按第 5.2 节把亮度差施加到目标 RGB。它保留的是目标通道差，不是严格的 Lab/HSV 色相；
文档和 UI 必须使用“保留目标色彩倾向”，不得写“绝对保色”。

## 8. 确定性迭代求解与收敛

### 8.1 红黑 Gauss–Seidel

V1 不引入通用线性代数库。对遮罩域按目标坐标棋盘着色：`color = (tx + ty) & 1`。每个完整 sweep：

1. 按目标 y、再按目标 x 的固定顺序更新红色未知；
2. 按同样顺序更新黑色未知；
3. 计算全域残差、停止条件和可选轻量快照；
4. 检查取消、运行预算和 generation；
5. 只有完成整个 sweep 后才向 UI 提交一致状态。

单点更新：

```text
f_p = (rhs_p + Σ(q∈N4(p)∩Ωt) f_q) / 4
```

- 初值固定为映射后目标颜色/亮度；
- 松弛因子固定为 1.0，不开放 SOR 参数，避免调参改变协议；
- 同一输入、参数和运行模式必须逐 double 得到相同残差序列，最终 byte 逐项相同；
- 单线程数值核心先建立正确性基线；V1 不做并行 sweep。

### 8.2 残差与停止条件

每个未知、每个求解通道的残差：

```text
r_p = rhs_p - (4 f_p - Σ(q∈N4(p)∩Ωt) f_q)

rms = sqrt(Σ r_p² / (unknownCount × channelCount))
maxAbs = max |r_p|
relativeRms = rms / max(initialRms, 1e-15)
```

建议默认值由 G0 Golden/基准最终冻结：

| 参数 | 候选默认 | 候选范围 |
| --- | ---: | ---: |
| RMS 绝对容差 | `1e-6` | `[1e-8, 1e-3]` |
| 最大绝对残差容差 | `1e-5` | `[1e-7, 1e-2]` |
| 最大迭代 | `800` | `[1, 2,000]` |
| 预览提交间隔 | `10` sweep | `1/5/10/25/50` |

只有 `rms <= rmsTolerance` 且 `maxAbs <= maxTolerance` 才标记 `Converged`。停止原因固定为：

- `Converged`：两个残差阈值同时满足；
- `IterationLimit`：达到最大迭代，结果可查看但不可作为“已收敛结果”正式导出；
- `Canceled`：用户取消或 Document 关闭；
- `BudgetExceeded`：动态工作量超出冻结预算；
- `NonFinite`：RHS、解或残差出现 NaN/Infinity；
- `Stale`：输入/参数 generation 已变化，迟到结果丢弃；
- `Faulted`：其他结构化失败。

### 8.3 单步、连续运行和快照

- “单步”严格执行一个完整红黑 sweep；不把半个颜色阶段显示为一轮；
- “运行”重复同一个 sweep 用例，暂停只阻止下一 sweep，取消在行和 sweep 边界检查；
- UI 提交间隔只影响观察频率，不改变 residual 计算、停止判断或最终结果；
- 残差数值可每 sweep 保存一条，但最大 2,001 条；完整尺寸 RGB 迭代帧只保留当前解；
- 预览代理采用最大边候选 1,024，最多保留 32 个检查点，按 `0,1,2,4,8,...` 和最终状态采样；
- 达到 32 个时保留首帧、最近帧、最优残差帧和对数均匀历史，不常驻全部 Bitmap；
- Document 快照不保存迭代解、RHS、残差数组或预览帧。

## 9. 梯度、散度与收敛可视化

### 9.1 guidance field

- 在源/目标代理上显示水平与垂直 guidance 的强度热图；
- 箭头只在有界网格上采样，长度使用归一化显示，颜色/纹理区分源梯度和目标梯度；
- 混合模式显示每条采样边来自源还是目标，不只使用红/绿颜色；
- 单色模式显示标量亮度梯度，并明确没有三个独立 RGB guidance；
- 显示归一化仅影响投影，不改变原始 double。

### 9.2 RHS 与残差

- RHS/散度热图使用对称零中心映射，正负值同时用颜色、符号和纹理图例表达；
- 残差热图按当前最大绝对残差归一化，并同时显示固定阈值刻度，避免每帧自动拉伸造成“看起来没变化”；
- 收敛曲线纵轴为 log10 RMS，横轴为 sweep；零残差使用明确的下限显示，不传入 `log10(0)`；
- 图表控件只消费不可变、有限长度 DTO，不引用 Session 的可变数组；
- 鼠标探针显示源/目标坐标、mask、source/target/guidance、RHS、当前解和残差，但不持有大图副本。

## 10. 直接 Alpha 合成与对比诊断

### 10.1 直接 Alpha 基线

V1 的有效求解区域要求源像素不透明，因此同遮罩的直接 Alpha 对照等价于硬克隆，但仍按通用线性光公式实现并测试：

```text
a = mask × sourceAlpha
Cout = a × Csource_linear + (1-a) × Ctarget_linear
```

- 域外逐字节复制目标；域内 Alpha 遵守现有非预乘输出契约；在 V1 预检下为 255；
- 公式单独放在 `DirectAlphaCompositor`，不知道 Poisson 求解器；
- UI 使用“直接 Alpha 对照”，不使用“原始错误结果”或暗示它一定更差；
- 未来软遮罩只能扩展对照，不得静默改变现有 Poisson 域定义。

### 10.2 对比指标

除复用的 MAE/RMSE/PSNR/SSIM 与差异图外，增加 Poisson 专用解释性诊断：

- `boundaryGuidanceRmse`：跨越遮罩边界的输出梯度与选定 guidance 的线性 RGB RMSE；
- `interiorGradientRmse`：输出梯度与选定 guidance 的 RMSE；
- `residualRms/maxAbs`：离散方程残差；
- `clippedChannelCount/clippedPixelCount`：sRGB 输出裁剪；
- `mixedSourceEdgeRatio`：仅混合梯度模式有效；其他模式为 N/A，不写伪造的 0；
- `iterationCount/stopReason`：求解过程事实。

边界 guidance RMSE 小不代表整体自然，完整参考指标高也不代表主观更好。报告和 UI 禁止出现“质量提升率”“最佳融合”或
“自动自然”等没有依据的结论。

## 11. 资源、内存与执行边界

### 11.1 前置预算

现有 16 MP 解码上限不能直接成为 Poisson 求解上限。G0 建议校准并冻结：

| 预算 | 候选上限 | 目的 |
| --- | ---: | --- |
| 源/目标解码 | 沿用各自 16,000,000 像素 | 复用现有输入安全边界 |
| 遮罩未知量 | 500,000 像素 | 控制解/RHS/邻接和每 sweep 工作量 |
| 遮罩包围盒 | 1,000,000 像素 | 控制栅格、索引和投影 |
| 最大迭代 | 2,000 | 保证运行有终点 |
| 标量更新预算 | 180,000,000 | `unknown × channelCount × maxIteration` |
| 峰值托管内存 | 512 MiB | 包含图片、问题、当前解、投影和安全余量 |
| 预览最大边 | 1,024 | 控制 Bitmap 和 UI 提交成本 |
| 预览检查点 | 32 | 禁止保存全部帧 |

预算必须在创建大数组和启动后台任务前计算，所有乘法使用 `checked long`。若用户参数超限，错误至少包含实际值、上限、
主要贡献项和可操作建议；不得静默降低 max iteration 或容差。

### 11.2 峰值内存模型

估算必须逐项覆盖同时存活对象：

```text
源 PixelImage                    = sourcePixels × 4
目标 PixelImage                  = targetPixels × 4
Alpha 对照 + 完成结果            = targetPixels × 4 × 2
包围盒 mask                      = boxPixels × 1
包围盒 unknownIndex              = boxPixels × 4
邻接索引                         = unknown × 4 × 4
RGB 解 + RHS                     = unknown × 3 × 8 × 2
或单色解 + RHS                   = unknown × 1 × 8 × 2
残差序列                         <= 2,001 × 固定记录大小
代理与 Bitmap                    = 按同时存活数量精确计入
安全余量                         = 上述总和 × 冻结比例
```

实际实现若不保存邻接索引或复用缓冲，可降低估算，但文档必须按真实同时存活数量更新。GC 估算不是硬内存承诺，因此还要有
运行时工作量计数和分配失败的结构化处理。

### 11.3 取消与迟到防护

- 遮罩栅格化每若干行检查取消；
- 问题构造、guidance/RHS、红阶段、黑阶段、残差和投影分别检查取消；
- 每行内未知量过多时增加固定块检查，不依赖墙钟；
- Document 为载入、准备、单步、运行、导出使用同一个 `SemaphoreSlim` 串行闸门；
- 每次输入/参数变化递增 generation 并取消旧运行；只有 generation、problem fingerprint 和实例身份都匹配才提交；
- `Dispose` 顺序固定为：阻止新命令、取消、等待/拒绝提交、释放 Bitmap/Session/取消源/闸门；
- 取消、失败或迟到不得发布 PNG/JSON/CSV 临时文件。

## 12. SOLID 分层与朴素设计模式

### 12.1 分层职责

| 层 | 允许职责 | 禁止职责 |
| --- | --- | --- |
| Domain | 坐标/遮罩值对象、颜色数值、guidance、问题构造、迭代、残差、投影数据、预算 | Avalonia、文件 IO、DI、Document、Bitmap |
| Application | Session、用例顺序、状态转换、取消、generation、导出协调 | 重新实现公式、控件布局、直接文件系统 API |
| Infrastructure | 严格 JSON/CSV、现有图片/原子写入适配 | 持有每实例解、决定融合模式、修改方程 |
| Features | Document 状态投影、命令、Avalonia View/Control、Bitmap 生命周期 | 数值循环、隐式 IO、全局可变状态 |
| Plugin/Standalone | DI、descriptor、真实对象图和本地预览 | 复制业务实现、保留跨 Document 会话 |

依赖方向固定为：

```text
Features → Application → Domain
Infrastructure → Application/Domain contracts
Plugin → 全部组合入口
Domain → 不依赖上层
```

### 12.2 SOLID 门禁

- **SRP**：遮罩栅格化、拓扑检查、guidance、RHS、求解、投影、报告和 UI 各自只有一个变化原因；
- **OCP**：三种真实 guidance 变化通过窄 Strategy 扩展；求解器对模式关闭；
- **LSP**：三个 Strategy 遵守同一坐标、颜色、有限数和反向边契约；契约测试覆盖所有实现；
- **ISP**：用例接口按“准备、编辑遮罩、放置、构建问题、单步、运行、比较、导出”拆分，不建立万能服务；
- **DIP**：Application 只依赖图片/写入/报告等端口；Domain 不依赖 DI；Document 通过构造注入用例；
- 架构测试扫描 Domain 对 Avalonia/Infrastructure/Features 的非法引用，扫描 singleton 是否持有图片/Session/Bitmap；
- 人工复核任何超过一个变化原因的类；不能用“Manager/Helper/Service”名称掩盖职责混合。

### 12.3 允许的朴素模式

- Strategy：仅用于三种 guidance，确有并列算法且共享契约；
- Session：集中每 Document 可变状态和所有权，不是全局状态容器；
- 不可变值对象/记录：坐标、偏移、选项、问题摘要、迭代进度、诊断；
- 窄用例：把 UI 命令映射为可测试的应用行为；
- Adapter：仅复用现有图片编解码、原子写入和严格报告边界。

明确禁止为“看起来像设计模式”而增加 Visitor、Command 总线、抽象工厂、Builder 链、Mediator、Event Bus、Repository、
Service Locator 或插件内插件系统。接口必须有真实替换点或层边界；单实现数值类保持 `sealed`。

## 13. 建议领域模型与应用用例

### 13.1 核心模型

```text
PoissonBlendMode
  NormalClone | MixedGradient | Monochrome

ImageOffset
  Dx, Dy

PoissonMaskStroke
  Mode(Add/Erase), RadiusNormalized, PointsNormalized

PoissonMaskDefinition
  Rectangle?, Strokes, RasterProtocolVersion

PoissonMaskTopology
  UnknownCount, BoundingBox, ComponentCount, HoleCount, BoundaryCount

PoissonBlendOptions
  Mode, RmsTolerance, MaxAbsTolerance, MaxIterations, PreviewInterval

PoissonProblem
  Fingerprint, DomainIndex, NeighborMap, Rhs, InitialValues, ChannelCount, Diagnostics

PoissonIterationProgress
  Iteration, Rms, MaxAbs, RelativeRms, BestRms, StopReason?, Preview?

PoissonBlendResult
  ProblemFingerprint, Output, AlphaBaseline, Diagnostics, Convergence
```

大数组不放入普通 record 的结构相等比较，不进入日志或快照。持有数组的类型必须说明所有权、只读承诺和释放/失效时机。

### 13.2 无状态领域服务

```text
PoissonMaskRasterizer
PoissonMaskTopologyAnalyzer
PoissonPlacementValidator
NormalCloneGuidanceStrategy
MixedGradientGuidanceStrategy
MonochromeGuidanceStrategy
PoissonGuidanceCatalog
PoissonProblemBuilder
PoissonRelaxationSolver
PoissonResidualProjector
PoissonGuidanceProjector
DirectAlphaCompositor
PoissonBlendComposer
PoissonBlendDiagnosticsAnalyzer
PoissonResourceEstimator
```

`PoissonGuidanceCatalog` 只把稳定枚举映射到 `IEnumerable<IPoissonGuidanceStrategy>` 中唯一实现；未知/重复模式启动失败，
不做反射扫描。

### 13.3 窄应用用例

```text
IPreparePoissonSessionUseCase
IEditPoissonMaskUseCase
IPlacePoissonRegionUseCase
IBuildPoissonProblemUseCase
IStepPoissonSolverUseCase
IRunPoissonSolverUseCase
IComparePoissonBlendUseCase
IInspectPoissonPointUseCase
IExportPoissonImageUseCase
IExportPoissonReportUseCase
```

- Prepare 只解码/验证两图并创建 Session，不求解；
- Edit/Place 只改变选择意图并使问题/结果过期；
- Build 完成预检、预算、guidance、RHS 和初值，不执行 sweep；
- Step 严格执行一个 sweep；Run 复用同一 Step 核心直到停止；
- Compare 只在收敛且 fingerprint 匹配时计算最终诊断；
- Export 只接受完成、未过期的产物，并通过现有原子写入端口发布。

### 13.4 Session 所有权

`PoissonBlendingSession` 为 scoped、非线程安全对象，由单个 Document 独占，保存：

- 源/目标 `PixelImage`、内容 fingerprint 和非敏感显示名；
- 轻量遮罩定义、栅格、拓扑和目标偏移；
- 当前问题、解缓冲、残差序列、有限代理和完成结果；
- generation、状态、停止原因、取消关联和诊断。

Session 不保存 Avalonia Bitmap、文件对话框、绝对路径或 View 引用。Document 负责串行闸门、Bitmap 和 UI 调度。

## 14. Document、UI 与交互计划

### 14.1 Document 生命周期

建议状态：

```text
Empty
  → ImagesReady
  → MaskReady
  → PlacementReady
  → ProblemReady
  → Paused ↔ Running
  → Converged

任意准备/运行状态 → Canceled | Faulted
输入、遮罩、偏移、模式变化 → Stale → 重新 Build
换图/清空 → ImagesReady 或 Empty
关闭 → Disposed
```

- 恢复快照只恢复文件显示名、遮罩意图、偏移和参数；不自动读盘、不自动构建问题、不自动运行；
- 多实例 Document 的 Session、取消源、Bitmap、偏移和残差互不共享；
- 命令可用性由状态派生，不能依赖按钮是否刚好禁用来维护领域约束；
- `IterationLimit` 与 `Converged` 必须有不同视觉和导出权限。

### 14.2 建议布局

```text
┌ 顶部命令：载入源图 | 载入目标图 | 交换 | 重置 | 导出 ┐
├ 左侧参数 ─────┬ 中央工作区 ───────────────────┬ 右侧诊断 ┤
│ 选择工具      │ 源图/遮罩      目标图/放置轮廓 │ 模式与协议 │
│ 画笔与矩形    │ Alpha 对照     Poisson 当前解  │ 迭代/残差  │
│ 偏移精确输入  │ guidance/RHS/残差标签页         │ 预算/裁剪  │
│ 构建问题      │ 分割线或并排比较                 │ 像素探针   │
│ 单步/运行/暂停│                                  │ 限制说明   │
└ 状态栏：尺寸、未知量、迭代、RMS、停止原因、generation ┘
```

窄窗口下折叠左右栏，中央画布仍可滚动；不得要求固定超宽窗口。

### 14.3 专用控件

- `PoissonSourceMaskCanvas`：源图遮罩绘制、轮廓、画笔纹理、归一化坐标和键盘替代操作；
- `PoissonPlacementCanvas`：目标轮廓拖动、合法/非法位置纹理、整数偏移与精确输入联动；
- `PoissonFieldView`：guidance/RHS/残差热图和稀疏箭头；
- `PoissonConvergenceChart`：有限残差 DTO 的对数曲线、阈值线和停止点；
- `PoissonComparisonControl`：Alpha/Poisson 分割线、并排与差异图。

控件只负责绘制、命中和坐标转换；算法输入由 Document 调用用例生成。所有 Bitmap 都由 Document 创建、替换和释放。

### 14.4 可访问性与防误解

- 所有命令、画笔、模式、图例、曲线和画布有中文 Automation 名称/帮助文本；
- 非法放置不仅用红色，还使用斜纹、图标和文字；源/目标梯度来源不仅用红绿，还使用实线/点纹；
- 提供键盘微调目标位置（1 像素；带修饰键 10 像素）和精确数值输入；
- 收敛曲线同时显示数字，不要求用户只读图；
- 明确显示“当前预览未收敛”“发生色域裁剪”“透明 halo 不支持”等状态；
- DPI 100/125/150%、窄窗口、键盘焦点和高对比主题列入人工验收。

## 15. 持久化、报告与导出

### 15.1 Document 快照

版本化快照建议只保存：

- schema/version、源/目标非敏感显示名与尺寸提示；
- `PoissonMaskDefinition`（受 512 笔划、2,048 点/笔划和 128 KiB 限制）；
- 目标偏移、模式、容差、最大迭代和预览间隔；
- UI 选择的标签页/比较方式等轻量偏好。

明确不保存：绝对路径、原始/目标/结果 RGBA、完整遮罩栅格、问题邻接、RHS、当前解、残差热图、全部迭代帧、Bitmap、
取消源或 Session。恢复后状态为“等待用户重新选择图片”；只有内容 fingerprint 相符才允许用户显式重建。

### 15.2 PNG 导出

- 分别导出 Poisson 结果和直接 Alpha 对照，文件名清楚区分；
- 只有 `Converged`、未过期且完整尺寸结果允许正式导出；`IterationLimit` 只能另存带 `-unconverged-preview` 明示名称，默认禁用；
- 不覆盖源/目标，除非现有原子写入端口已有明确用户确认语义；
- 写入临时文件、编码、可选回读、自检、原子替换和失败清理由既有基础设施负责；
- G0 决定是否沿用 Seam Carving 的“不强制回读”或增加本能力回读，不能在实现中临时决定。

### 15.3 JSON/CSV 报告

报告 schema 候选 `image-lab-poisson-blending-report/v1`，至少包含：

- 协议/应用版本、UTC 时间、模式、颜色/邻域/边界/求解器协议；
- 两图尺寸和内容 fingerprint；不含绝对路径、文件内容或缩略图；
- 遮罩未知量、包围盒、边界、连通分量、孔洞与偏移；不含完整栅格/点列；
- 容差、最大迭代、实际迭代、残差序列摘要、停止原因和运行预算；
- guidance 来源统计、裁剪统计、边界 guidance/内部梯度/完整参考诊断；
- 已收敛、未收敛、取消、过期和 N/A 的严格区分。

JSON 拒绝未知/重复字段、未知枚举、NaN/Infinity、超长字符串、超深结构和尾随垃圾；CSV 固定 UTF-8 BOM、列顺序、
InvariantCulture、转义和 N/A 规则。Domain 不直接引用 JSON/CSV API。

## 16. 中文注释与设计说明要求

新增生产代码必须使用中文详细注释，重点解释“为什么”和“协议是什么”，至少覆盖：

- 源/目标坐标、偏移方向、闭开矩形、归一化点到像素的舍入；
- 二值遮罩、4 邻域、halo 和为何 V1 阻断透明像素；
- sRGB 分段编解码、线性光求解、byte 舍入和 gamut clamp；
- 普通/混合/单色 guidance 公式，混合模式为何整 RGB 向量择强；
- 离散 Poisson 方程、Dirichlet 边界、RHS 各项来源和固定邻边顺序；
- 红黑着色、一次 sweep、初值、残差、双阈值和停止原因；
- 数组元素含义、索引映射、所有权、同时存活对象与预算公式；
- 取消检查点、generation 防迟到、Session/Bitmap/取消源释放顺序；
- 可视化归一化为何不能反馈到数值核心；
- 指标、收敛和视觉质量之间不能互相替代；
- 朴素模式取舍：为什么 guidance 使用 Strategy，其他单实现保持 sealed 服务。

不合格注释包括：逐行翻译代码、只写“计算梯度/求解方程”、复制方法名、用 TODO 代替协议、只给英文缩写、把复杂公式
藏在无注释索引运算中。核心领域文件顶部应有中文设计说明；公开/内部核心类型和非直观参数使用中文 XML 文档。

## 17. 单元测试、集成测试与质量门禁

### 17.1 坐标、遮罩与预检

- 源到目标偏移的正/负方向、整数边界、闭开矩形和 `ToEven` Golden；
- 画笔添加/擦除、边缘 clamp、重复笔划、空遮罩、单像素、多个连通分量和孔洞；
- 4 邻域边界/halo、源或目标越界、源/目标 Alpha 254 与 255 的结构化结果；
- 遮罩 fingerprint 对笔划、矩形、尺寸和协议敏感，对 UI 缩放/DPI 不敏感；
- 512 笔划、2,048 点、128 KiB 的边界内/外测试。

### 17.2 颜色与 guidance Golden

- sRGB 0、阈值两侧、128、255 的线性值与往返 byte Golden；
- 常量源图普通 guidance 为零；水平/垂直阶跃方向和反向边反对称；
- 混合梯度源更强、目标更强、完全相等选源、整 RGB 择强而非逐通道拼接；
- 单色 BT.709 原色权重、亮度梯度、未裁剪时目标通道差保持；
- 三个 Strategy 的有限数、坐标、反向边和取消契约；
- 投影的线性/对数/对称归一化不改变 guidance/RHS。

### 17.3 问题构造与离散方程

- 1 像素、2×2 块、L 形、环形和多分量遮罩的 unknown index/邻接/RHS Golden；
- 常量 source/target 的 RHS 与目标常量解；
- 手算边界值、内部邻居、四边固定累加顺序和三个 RGB 通道；
- 单色只有一个通道，RGB 模式恰为三个通道；
- 不显式分配 `N×N` 矩阵的架构/资源断言；
- 非有限、错误 fingerprint、错误尺寸、过期 placement 必须拒绝。

### 17.4 求解器与收敛

- 手算一个和两个未知量的首个红/黑 sweep；
- 小问题与测试内独立高精度直接解比较，误差与残差均在冻结容差内；
- 常量边界/零 guidance 收敛到常量；已知谐函数和已知合成解恢复；
- 同输入的逐 double 残差序列、迭代数、停止原因和最终 byte 确定；
- RMS/MaxAbs/relative 公式、初始残差为零、`log10(0)` 显示下限；
- 双阈值同时满足、IterationLimit、Canceled、BudgetExceeded、NonFinite、Stale；
- 单步 N 次与连续运行 N 次解/残差完全相同；预览间隔不同最终结果相同；
- 暂停不启动下一 sweep，取消不提交半 sweep 或迟到结果。

### 17.5 合成、裁剪和对比

- 线性光 Alpha 0/0.5/1 Golden；V1 不透明预检下同遮罩硬克隆；
- 域外目标逐字节不变、域内 Alpha 保持 255、目标尺寸不变；
- 单色 delta 应用、上下 gamut clamp、裁剪通道/像素计数；
- Alpha 与 Poisson 相同输入/偏移/遮罩；错误 fingerprint 或尺寸拒绝比较；
- boundary guidance RMSE、interior gradient RMSE、mixed source ratio 和 N/A 语义；
- 相同结果差异为零；UI/报告不出现“最佳”“提升率”或把残差当视觉评分。

### 17.6 预算、用例与生命周期

- unknown、box、channel、iteration、更新量和峰值字节的 checked Golden；
- 候选边界内/外，溢出与超预算在大数组/后台任务前阻断；
- Prepare 不求解，Build 不执行 sweep，Step 只执行一轮，Run 复用同一数值核心；
- 输入/遮罩/偏移/模式/容差变化的失效范围；只改预览间隔不重建方程；
- Session Reset/Invalidate/Dispose、多实例隔离、串行闸门和 generation；
- 关闭运行中 Document 后无迟到 UI 更新、无残留临时文件、Bitmap 正确释放。

### 17.7 持久化、Document、UI 与架构

- 快照版本、大小、非法枚举/数值、无绝对路径/RGBA/mask raster/RHS/解/残差帧；
- 恢复不自动 IO、Build 或 Run；内容 fingerprint 不同必须显式重选；
- JSON/CSV 严格字段、未知/重复字段、非有限数、BOM、转义、文化区域、N/A 与隐私；
- PNG 只导出允许状态，取消/过期/失败不发布；
- 第十七个稳定 Persistable Document、ID 唯一、scoped Session、singleton 无状态服务；
- Headless View、关键绑定、Automation 名称、纹理图例、键盘偏移和窄窗口布局；
- Domain 无 Avalonia/IO，Application 不重写数值，Infrastructure 不持有 Session；
- 核心中文注释、锁文件无新增依赖、无 AIFLOW、无 Windows workflow；
- Standalone 从真实 Module 解析真实 Document/View，不复制业务。

### 17.8 测试数据与性能门禁

- 算法测试使用代码构造的小矩阵、常量、阶跃、棋盘、圆/环/L 形遮罩和已知解，不依赖网络；
- Golden 注明手算/独立参考来源；不得在测试中调用生产算法生成期望值；
- 自然图片只用于有限人工验收，不纳入版权不明的仓库资源；
- 性能门禁断言工作量、数组长度、预算和取消，不使用依赖机器速度的毫秒阈值；
- 增加默认可运行的中等问题压力测试，验证有界内存模型、取消和不保存全部帧；
- 禁止 `[Fact(Skip=...)]`、条件跳过、删除历史测试、放宽旧断言或只跑新增测试；
- G9 记录实施前后总数、净增数、0 失败、0 跳过、0 警告和 0 错误。

### 17.9 本地开发门禁命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

这些是本地开发门禁，不是 Windows CI 或发布门禁。本阶段不新增 GitHub Actions/Azure Pipelines Windows job，不执行
`dotnet publish`、插件 ZIP、真实 Host、安装、升级、卸载或发布验收。发布时须另行授权并按公共发布文档执行。

## 18. G0–G9 交付与验收

### G0：产品、数学、Golden 与资源基线

- 冻结第 1–11 节协议、ID 候选、容差、迭代、预算和 Golden；
- 实跑 locked restore、Debug/Release build/test，建立 `history/g0-product-math-and-baseline.md`；
- 用独立小型参考解校准线性方程、红黑 sweep、残差和停止阈值；
- 验收：开放数值项归零；起始证据真实记录；不依赖 AIFLOW、Windows CI 或发布流程。

### G1：遮罩、拓扑、坐标与放置

- 实现遮罩定义/栅格化、拓扑分析、整数偏移和 placement validator；
- 完成边界、halo、Alpha、连通分量、孔洞、fingerprint 和快照预算测试；
- 验收：UI/DPI 不进入领域坐标；非法问题在分配求解数组前失败。

### G2：颜色、guidance 与问题构造

- 完成线性 sRGB、三种 `IPoissonGuidanceStrategy`、catalog、RHS 和紧凑邻接；
- 完成普通/混合/单色 Golden、反向边、散度和 problem fingerprint；
- 验收：Domain 不依赖 Avalonia；不构造 `N×N` 矩阵；投影不改变 double 输入。

### G3：迭代、残差与观察

- 实现红黑 Gauss–Seidel、双残差停止、单步/连续运行、取消和有限检查点；
- 完成已知解、直接参考、确定性、停止原因、暂停和预览间隔等价测试；
- 验收：不保存全部帧；半 sweep 不提交；未收敛状态不冒充完成。

### G4：结果、Alpha 对照与诊断

- 实现线性光 Alpha 对照、RGB/单色合成、clamp 统计和差异诊断；
- 复用完整参考比较，增加 boundary/gradient/residual 指标；
- 验收：域外目标不变，结果同目标尺寸，所有指标保持解释边界。

### G5：Session、窄用例与资源治理

- 实现 scoped Session、准备/编辑/放置/构建/单步/运行/比较用例；
- 完成预算、串行闸门、generation、取消、换图、重置、关闭和多实例测试；
- 验收：singleton 无状态；每实例资源隔离；超预算前置阻断。

### G6：持久化、报告与导出

- 完成轻量快照、严格 JSON/CSV、Poisson/Alpha PNG 原子导出；
- 完成 schema、隐私、非有限、N/A、取消、过期和失败清理测试；
- 验收：快照/报告不含大数组、绝对路径或图片；Domain 不直接序列化。

### G7：Document 生命周期与组合

- 增加 `PluginIds.PoissonBlendingDocument`、服务登记、Persistable Document 和 Standalone 入口；
- 更新组合测试为十七个 Document，证明 scoped 隔离和 singleton Strategy/求解器复用；
- 完成恢复不自动 IO、关闭释放、迟到拒绝和 Bitmap 生命周期；
- 验收：Module 不增加 Tool、AIFLOW、Workflow Action 或 Workbench Command。

### G8：UI、解释与专用文档

- 完成 View、五个专用控件、键盘/可访问性、中文限制和有限人工验收；
- 建立本专题统一文档目录与阶段历史，同步根 README、docs 索引、设计索引和未来能力状态；
- 验收：Headless 加载通过；窄窗口可用；颜色之外仍能辨认状态。

### G9：本地封板

- 复跑第 17.9 节全部本地命令，确认 Debug/Release 0 警告/0 错误、全部测试通过、0 跳过；
- 复核锁文件、依赖、中文注释、架构扫描、文档链接和 git diff；
- 记录已证明/未证明事项和全部延期发布项；
- 验收：只声明“生产实现与本地自动门禁完成”，不声明发布完成。

## 19. 预计代码与测试落点

### 19.1 生产代码

```text
src/ImageLabPlugin.Plugin/
  Domain/PoissonBlending/
    PoissonBlendModels.cs
    PoissonMaskServices.cs
    PoissonPlacementValidator.cs
    PoissonGuidanceStrategies.cs
    PoissonProblemBuilder.cs
    PoissonRelaxationSolver.cs
    PoissonBlendProjectors.cs
    DirectAlphaCompositor.cs
    PoissonBlendDiagnostics.cs
    PoissonResourceEstimator.cs
  Application/PoissonBlending/
    PoissonBlendingContracts.cs
    PoissonBlendingSession.cs
    PoissonBlendingUseCases.cs
  Infrastructure/PoissonBlending/
    PoissonBlendingReportSerializer.cs
  Features/PoissonBlending/
    PoissonBlendingDocument.cs
    PoissonBlendingView.axaml
    PoissonBlendingView.axaml.cs
    PoissonSourceMaskCanvas.cs
    PoissonPlacementCanvas.cs
    PoissonFieldView.cs
    PoissonConvergenceChart.cs
    PoissonComparisonControl.cs
  Constants/PluginIds.cs
  Plugin/ImageLabPluginServices.cs
  Plugin/ImageLabPluginModule.cs

src/ImageLabPlugin.Standalone/
  MainWindow.axaml
  MainWindow.axaml.cs
```

文件可在 G0/G1 按真实职责合并，但数值、应用协调和 UI 不能跨层混写。若一个文件明显承担两个变化原因，应拆分；
不得为了与清单逐字一致制造空接口、空基类或空壳文件。

### 19.2 测试

```text
tests/ImageLabPlugin.Tests/
  PoissonMaskAndPlacementTests.cs
  PoissonColorAndGuidanceTests.cs
  PoissonProblemBuilderTests.cs
  PoissonRelaxationSolverTests.cs
  PoissonBlendCompositionTests.cs
  PoissonResourceAndUseCaseTests.cs
  PoissonReportAndArchitectureTests.cs
  PoissonBlendingDocumentTests.cs
  PoissonBlendingViewTests.cs

  CompositionAndPersistenceTests.cs   # 更新为十七个 Document 与快照边界
  ImageCodecAndUseCaseTests.cs         # 真实 View/Standalone 对象图回归
  SeamArchitectureTests.cs             # 仅在共享笔划/坐标提取时增加回归
```

测试按职责组织，不要求一个类对应一个文件。每个 Gate 先增加失败测试再实现，不得在 G9 才集中补门禁。

## 20. 专用文档与同步范围

实施时按现有能力惯例建立并维护：

```text
docs/design/poisson-blending/
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

- `README.md`：状态、阅读顺序、能力、前提和解释边界；
- `user-manual.md`：从选两图、画遮罩、放置、观察迭代到比较/导出的新手步骤；
- `guide.md`：模式、参数、状态、预算、取消、快照、错误和透明度限制；
- `mathematical-principles.md`：线性 sRGB、梯度/散度、离散泊松、三种 guidance、红黑迭代和残差；
- `report-schema.md`：严格 JSON/CSV、字段、N/A、隐私和版本；
- `testing.md`：每阶段真实命令、总数、数值/资源边界、已证明和未证明；
- `implementation.md`：本冻结设计、阶段计划与实际落地差异；
- `history/`：每个 Gate 的真实证据，不作为当前用户入口。

G8 同步更新根 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md` 和确实受影响的公共边界。
当前计划阶段只把本文件登记为“规划中”，不得提前把第十六项能力或第十七个 Document 写成已完成。

## 21. 有限人工验收清单

- 选择纹理物体和不同亮度目标背景，普通克隆边界过渡自然且整体颜色受目标边界影响；
- 在带强目标纹理的落点比较普通/混合，混合模式显示部分边来自目标并保留对应纹理；
- 单色模式迁移源亮度细节，目标色彩倾向可辨，发生裁剪时有明确计数；
- 直接 Alpha 边界与 Poisson 边界可同步比较，不给出“哪个一定更好”的结论；
- 矩形、添加、擦除、多分量和孔洞遮罩轮廓/统计正确；
- 拖动、键盘和数值输入得到相同整数偏移；非法 halo 位置立即以纹理和文字提示；
- 单步 N 次与连续 N 次显示相同残差和图像；暂停、继续、取消、重置状态清楚；
- guidance、RHS、残差热图和收敛曲线随迭代更新，但切换显示不改变最终结果；
- 达到迭代上限时明确标记未收敛，默认不能按正式完成结果导出；
- 超未知量、包围盒、迭代、工作量、峰值内存和快照大小显示实际值/上限/建议；
- 两个 Document 并行使用互不污染；关闭运行中的 Document 后无迟到更新；
- 导出 PNG 尺寸等于目标，域外像素不变；JSON/CSV 不含绝对路径、原图、遮罩栅格或迭代帧；
- 100%、125%、150% DPI、窄窗口、键盘与高对比主题下主要命令可访问。

人工验收不替代自动测试，也不把若干自然图片的主观观感写成算法正确性证明。

## 22. 回滚与兼容策略

- 新能力使用独立 Document ID、快照/report schema 和目录，不修改既有 Document 快照；
- G1–G6 在接入 Module 前可独立回滚；G7 接入失败时整体移除注册和 ID 引用，不保留半成品入口；
- 若共享笔划/颜色 API 提取导致 Seam/Color Transfer 回归，回退共享提取，Poisson 保留窄实现；
- 颜色、guidance、邻域、边界、求解、停止或舍入语义改变时必须升级协议 ID/schema；
- schema 只向后新增可选字段；未知枚举和不兼容版本必须明确拒绝；
- 未完成运行、RHS、解、残差、像素和 Bitmap 从不进入持久化，因此恢复不依赖内部数组布局。

## 23. 完成定义

只有同时满足以下条件，V1 才可标记为“生产实现与本地自动门禁完成”：

- 第 3.1 节全部能力落地，第 3.2 节非目标未被偷渡；
- SOLID 依赖方向、朴素模式和单一职责通过架构测试与人工复核；
- 核心生产代码具有详细中文设计注释，公式、坐标、边界、所有权、取消和停止原因无含糊处；
- 遮罩、三种 guidance、RHS、求解、残差、合成、预算、报告和 UI 均有确定性自动测试；
- 独立小问题参考证明方程正确；单步/连续等价；停止/取消/迟到/超预算均可证；
- 不保存全部帧，超预算在分配前阻断，关闭无迟到提交；
- 第十七个 Persistable Document、快照、多实例、DI、Standalone 和 Headless View 通过；
- 专用文档、总索引、未来能力状态和阶段历史同步；
- Debug/Release locked restore/build/test 全绿、0 跳过、0 警告、0 错误；
- 文档明确列出未执行的真实 Host、ZIP、Windows CI 和发布验收。

计划文档完成、Demo 可运行、个别自然图像视觉良好或残差下降，都不能单独满足完成定义。

## 24. 发布阶段明确延期

以下事项本轮不做，也不进入本地完成声明：

- Windows CI runner 和任何新 workflow；
- `dotnet publish`、插件 ZIP、manifest/产物审计；
- 真实 MyAvaloniaManagement Host 加载、停靠、布局恢复和多窗口验证；
- 安装、升级、卸载、回滚和签名；
- 16 MP 长时间压力、不同 GPU/DPI/系统区域和低内存机器矩阵；
- 发布安全审查、发布说明和正式兼容性声明。

进入发布阶段时必须由用户另行授权，再按 `docs/design/shared/deployment-and-release.md` 执行。不得把本计划的本地
Debug/Release 构建误写成发布门禁已完成。

## 25. G0 前必须关闭的校准项

以下不是产品方向开放项，而是进入实现前必须用 Golden 和基准冻结的数值：

1. RMS/MaxAbs 双容差默认值和“两个条件同时满足”的 Golden；
2. 最大 unknown、包围盒、标量更新量、峰值字节和中等压力问题尺寸；
3. 预览最大边、提交间隔、32 个检查点的保留算法和 Bitmap 同时存活数量；
4. 512 笔划、2,048 点/笔划、128 KiB 快照是否沿用 Seam Carving 的冻结值；
5. 线性 sRGB byte 往返、混合梯度平局、单色 gamut clamp 的逐值 Golden；
6. 独立小型直接参考解的实现方式，确保测试期望不调用生产求解器；
7. 标量更新预算是否按 `unknown × channelCount × maxIterations` 计入残差额外遍历；
8. PNG 导出是否强制真实回读自检；
9. 多连通分量/孔洞的最大数量是否需要独立上限；
10. `IterationLimit` 预览是否允许显式另存，以及文件名/报告中的未收敛标识。

G0 必须把校准结论、测试机事实、命令和调整原因写入阶段历史；未关闭前不得进入 UI 实现。
