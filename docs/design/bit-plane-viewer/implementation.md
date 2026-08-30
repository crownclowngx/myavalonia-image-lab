# ImageLabPlugin V1 位平面观察器实施计划

> 计划状态：开发实现与本地自动门禁完成；有限人工验收与发布阶段延期<br>
> 基线日期：2026-08-30<br>
> 产品名称：Bit Plane Viewer／位平面观察器<br>
> 技术基线：.NET 10、C# 14、Avalonia 12、Managed Plugin SDK 3.3<br>
> 起始自动基线：2026-08-30 实际复跑 Debug 149/149 通过、零跳过<br>
> 完成自动证据：2026-08-30 locked restore；Debug/Release build 零警告零错误；两配置 191/191 通过、零跳过<br>
> 核心路线：原始 8 位样本拆位 + 单位平面黑白投影 + 8 位掩码组合 + 高/低位预设 + 原通道重建 + 像素探针与位统计<br>
> 实施原则：SOLID 是首要规定；设计模式只服务真实边界；中文注释详细解释数值、所有权和取舍；先冻结位语义，再实现纯领域算法、用例、Document 与界面

| 实施包 | 当前状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 已完成 | 冻结产品范围、位序、Y/Alpha、显示、重建、资源和验收语义 | `history/g0-product-and-bit-baseline.md` |
| G1 | 已完成 | 建立 8 位掩码、通道样本、位统计和 Golden Vector | `history/g1-bit-domain-foundation.md` |
| G2 | 已完成 | 完成单个位平面、多位组合、高低位预设及有界投影 | `history/g2-plane-and-mask-projection.md` |
| G3 | 已完成 | 完成 R/G/B/Alpha/Y 重建、像素探针和完整尺寸 PNG 导出 | `history/g3-reconstruction-and-export.md` |
| G4 | 已完成 | 建立窄应用用例、Session、取消、迟到结果和资源预算 | `history/g4-application-use-cases.md` |
| G5 | 已完成 | 完成第七个 Persistable Document、快照、Scope 与生命周期 | `history/g5-document-lifecycle.md` |
| G6 | 已完成（自动证据） | 完成教学型联动 UI、键盘访问、Standalone 与 Headless View 门禁 | `history/g6-ui-and-explanation.md` |
| G7 | 已完成（本地自动门禁） | 完成本地双配置门禁、专用文档和开发阶段封板 | `history/g7-local-sealing.md` |

本文定义 ImageLab 的下一项产品能力和第七个 Persistable Document。它把用户显式选择图片的 R、G、B、Alpha 或 Y
通道按 8 位整数拆成 bit 7（MSB）到 bit 0（LSB），帮助用户观察不同位对轮廓、纹理、颜色精度和细小扰动的贡献。

本工具只提供可解释的信号观察与确定性重建。低位看起来杂乱不等于存在 LSB 隐写，位分布均衡也不能证明图片被篡改；
V1 不自动给出“隐写/非隐写”判断，不写入秘密数据，不扫描目录，也不读取现有水印协议的载荷映射。

本文是实施阶段的唯一总计划。每个 G 包完成后，必须新建对应历史记录，写明实际修改、测试证据、性能数据、偏差、
遗留风险和回滚方式。尚未执行的项目不得勾选，起始 149 项测试不得冒充位平面功能的完成证据。

当前阶段只执行本地开发门禁；不使用 AIFLOW，不登记 Workflow Action 或 Workbench Command，不新增 Windows CI，
不执行 ZIP、真实 Host、安装/卸载和正式发布门禁。

## 1. V1 用户闭环与固定实施顺序

### 1.1 用户闭环

```text
显式选择一张 PNG 或 JPEG 图片
    ↓
解码原始 RGBA8888 像素，显示尺寸、Alpha 状态和资源提示
    ↓
选择 R、G、B、Alpha 或 Y 通道
    ↓
查看 bit 7（MSB）到 bit 0（LSB）的 1/0 数量、占比和权重
    ↓
选择一个位，显示不透明黑白单位平面
    ↓
勾选多个位，或使用“仅高位”“仅低位”“全部”“清空”预设
    ↓
联动显示组合通道图和只保留所选位后的重建图
    ↓
点击预览，查看原始 RGBA、通道字节、8 位二进制、掩码和重建值
    ↓
按需把当前完整尺寸重建结果原子导出为 PNG
```

### 1.2 固定实施顺序

1. G0 先冻结 bit 0/7、掩码、Y 量化、Alpha 显示、预览采样和重建语义；没有 Golden Vector 时不得写 UI；
2. G1 用纯领域测试证明通道字节、位提取、位计数和掩码正确；
3. G2 只做观察投影，不把显示归一化混入真实重建数据；
4. G3 证明五通道重建逐像素正确、未选通道不变，再允许完整尺寸导出；
5. G4 用窄用例协调解码、分析、投影和导出，Document 不写逐像素循环；
6. G5 让 scoped Document 管理路径、选择、取消、generation、Revision 和 Bitmap 生命周期；
7. G6 最后实现联动布局、说明、快捷键和无障碍状态；
8. G7 执行 locked restore、Debug/Release warn-as-error build/test 并同步专用文档，不执行发布门禁。

### 1.3 V1 决策摘要

| 主题 | V1 决策 |
| --- | --- |
| 输入 | 一张用户显式选择的 PNG/JPEG；沿用 64 MiB 编码、16,000,000 像素上限 |
| 样本精度 | 固定 8 位无符号整数，不泛化到 10/12/16 位 |
| 通道 | R、G、B、Alpha、Y；不加入 Cb/Cr |
| 位序 | bit 7 是 MSB，权重 128；bit 0 是 LSB，权重 1 |
| 单位平面显示 | 0 显示黑色，1 显示白色，显示图 Alpha 恒为 255 |
| 多位组合 | `kept = channelByte & mask`；按 0–255 真实量级显示，不偷偷拉伸 |
| 仅高位 | 选择 bit 7 到用户指定最低保留位，含两端 |
| 仅低位 | 选择 bit 0 到用户指定最高保留位，含两端 |
| RGB 重建 | 只替换所选颜色通道，另外两个颜色通道和 Alpha 逐字节不变 |
| Alpha 重建 | 只替换 Alpha，RGB 逐字节不变；另用棋盘背景说明透明效果 |
| Y 重建 | Y 先量化到 byte，再按掩码保留；保留源 Cb/Cr 和 Alpha，逆变换裁切并报告 |
| 预览 | 最大边 1024，按原像素最近邻取样；不先抗混叠缩放再拆位 |
| 导出 | 当前重建结果按原始尺寸输出 PNG；不导出 JPEG，避免有损编码改写低位 |
| 结论 | 只显示位事实和解释，不判断是否存在隐写或传感器异常 |
| 集成 | 第七个 Persistable Document；不是 singleton Tool，不使用 AIFLOW |

## 2. 当前工程基线与缺口

### 2.1 可直接复用的事实

当前仓库已经具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集，以及复用同一 Module 和 DI 入口的 Standalone；
- 六个 Persistable Document、独立 Scope、`IDocumentLifetime`、快照、取消和 generation 门禁惯例；
- 自有 RGBA8888 `PixelImage`、`ImageSize`、16,000,000 像素和 64 MiB 编码输入上限；
- Avalonia PNG/JPEG 正式解码、PNG 编码、`IImageFileDialog` 和 `IAtomicFileWriter`；
- `ColorSpaceConverter` 的 BT.601 全范围 Y 公式，以及保持 Cb/Cr 和 Alpha 的 Y 重建基础；
- `ImagePreviewProjector` 的最近邻有界预览思想；
- xUnit、Avalonia Headless、Document 持久化、组合根和完整回归测试设施；
- 2026-08-30 在未改生产代码前实际复跑 Debug 149/149 通过、零跳过；
- 当前明确不使用 AIFLOW，Windows CI 和发布门禁延期。

### 2.2 需要新增的能力

- 五通道专用 `BitPlaneChannel`；现有频域 `ImageChannel` 不包含 Alpha，且语义不同，不应强行扩展；
- 连续 8 位 `BytePlane` 或等价只读通道样本，避免用 `double[]` 表示离散位事实；
- 受验证的 8 位掩码值对象、单位掩码、高位掩码和低位掩码；
- 一次扫描统计八个位平面的 0/1 数量、占比和二元熵；
- 单位平面、不拉伸的多位组合通道图，以及与原图一致坐标的有界投影；
- R/G/B/Alpha/Y 的确定性重建和裁切报告；
- 原始像素、通道字节、二进制、掩码和结果值的像素探针；
- Session、用例、Document、View、插件身份、组合根和 Standalone 入口；
- 位平面专用测试、指南、数学说明、用户手册和实施历史。

### 2.3 主工程约束

- Plugin Module 继续作为贡献登记的唯一事实源；
- 新能力登记为第七个 Persistable Document，不登记 Tool；
- Domain 不依赖 Avalonia、文件路径、JSON、DI、Bitmap 或图片编码器；
- Application 只通过窄端口协调解码、投影和导出；
- Infrastructure 只适配编解码、文件对话框、Bitmap 和原子写入；
- Feature 只管理实例状态、命令、取消、Revision 与展示，不包含逐像素算法；
- 不复制现有图片编解码、BT.601 公式、预览坐标和原子写入；需要调整公共颜色原语时先加回归测试；
- 原则上不新增第三方 NuGet，不引入图像处理框架、脚本运行时或反射式算法发现；
- 不使用 AIFLOW，不登记 Workflow Action 或 Workbench Command；
- 不新增 Windows CI，不执行 ZIP、真实 Host、安装/卸载或发布封板。

## 3. 产品范围与明确非目标

### 3.1 V1 必须完成

- R、G、B、Alpha、Y 五个 8 位通道；
- bit 7 到 bit 0 单独观察，统一显示编号、权重和 MSB/LSB 标签；
- 任意多位勾选，以及全部、清空、仅高位、仅低位四类预设；
- 单位平面黑白图、多位组合通道图、原图和重建图联动；
- 每个位的 1/0 数量、1 占比和二元熵，附带“不能单独证明隐写”的说明；
- 像素探针显示源坐标、RGBA、通道值、`0bxxxxxxxx`、掩码和保留后值；
- 选中 R/G/B/Y/Alpha 后符合本计划的原通道重建；
- Alpha 棋盘背景和不透明黑白位图，确保透明像素仍可观察；
- 当前完整尺寸重建结果原子导出为 PNG；
- 路径、选择状态的轻量快照，以及恢复后的显式重新分析；
- 取消、迟到结果拒绝、多个 Document Scope 隔离和 Bitmap 释放；
- 完整本地单元测试、集成测试、Headless View 门禁和中文专用文档。

### 3.2 明确不实现

- LSB 文本/文件写入、提取、密码、容量协议或伪随机位置映射；
- 卡方、RS、SPA、样本对分析、机器学习隐写检测或“可疑分数”；
- 自动宣称传感器噪声、篡改、相机来源或隐写存在；
- 10/12/16 位 RAW、HDR、浮点图、ICC 管理或 Bayer/CFA 数据；
- Cb/Cr、HSV、Lab 和任意用户公式通道；
- 同时载入多图、目录扫描、批量导出或后台监控；
- 位平面动画、时间轴、3D 图、可编程表达式和插件式算法框架；
- 将低位重建导出为 JPEG；JPEG 会重新量化并破坏要观察的低位事实；
- 修改现有频域水印协议、感知指纹、鲁棒性结论或频谱分析行为；
- AIFLOW、Workflow Action、Workbench Command、Windows CI 和发布门禁。

### 3.3 与后续 LSB 隐写实验的边界

位平面观察器只读用户选择的图片，并根据掩码生成确定性重建。后续“LSB 隐写与统计实验”若实施，应拥有独立协议、
容量、安全措辞、统计方法、Document 和测试数据，不能把写入命令偷偷放进本能力。V1 可以提供稳定的只读位平面领域
原语，但不预留万能算法注册中心或未使用的写入接口。

## 4. Document 形态与状态所有权

### 4.1 贡献形态

| 字段 | 固定值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.bit-plane-viewer` |
| 显示名称 | `位平面观察器` |
| 描述 | `拆分 R、G、B、Alpha 或 Y 的 8 个位平面并观察掩码重建结果` |
| 分类 | `图像分析` |
| Host 注册 | `AddPersistableDocument<BitPlaneViewerDocument, BitPlaneViewerView>` |
| 实例基数 | 多实例；每实例独立图片、通道、掩码、分析结果和取消令牌 |

选择 Persistable Document 而不是 Tool 的原因：

- 图片路径、通道、位选择和探针坐标共同构成一份可保存工作上下文；
- 用户可能并排比较同一图片的不同通道或两张不同图片；
- 大图解码、分析缓存和 Bitmap 必须跟随单个实例关闭而释放；
- singleton Tool 会错误共享“当前图片”和掩码，破坏多实例隔离；
- 该能力是主工作内容，不是依附其他 Document 的全局辅助状态。

### 4.2 持久状态

- 源图片路径；
- 当前 `BitPlaneChannel`；
- 显示模式：单位平面或多位组合；
- 当前单位 bit 索引；
- 当前 8 位掩码；
- 高位/低位预设的边界索引；
- 是否显示棋盘背景、统计说明和像素探针；
- 最后选中的原图坐标。

### 4.3 运行时派生状态

- 已解码的完整 `PixelImage`；
- 当前通道的只读 8 位样本和八个位统计；
- 最大边 1024 的源图、位图、组合图与重建图代理；
- 当前像素探针报告和 Y 重建裁切数；
- 完整尺寸导出期间的临时重建图和编码字节；
- Avalonia `Bitmap`、忙碌状态、错误、取消源和 generation。

只缓存当前通道，不为五个通道预先创建五份完整 `BytePlane`，也不为八个位预先缓存八张 RGBA 图。切换通道时释放
旧通道样本；切换掩码时复用当前样本和统计，只重新生成受控预览。

### 4.4 快照与恢复

- schema 从 `1` 开始，枚举保存稳定 ID，不保存中文显示文字；
- 掩码保存为 0–255 整数，bit 索引只允许 0–7；未知值回退到 `Y + bit 7 + 0x80`；
- 不序列化像素、通道数组、统计、Bitmap、重建 PNG 或错误堆栈；
- 恢复时只恢复路径与参数，不自动读取文件；用户显式点击“分析”后才解码；
- 文件不存在或不可读时保留参数并显示可恢复错误，不阻断 Host 恢复；
- 瞬时悬停不推进 Revision；路径、通道、模式、bit、掩码和说明选项变更才标记 Dirty；
- 关闭 Document 时取消分析与导出，拒绝迟到提交，清空大对象引用并释放所有 Bitmap。

## 5. 位与颜色的数值协议

### 5.1 通道字节

R、G、B、Alpha 直接读取解码后的未预乘 RGBA8888 字节。Y 使用现有 BT.601 全范围公式：

```text
Ydouble = 0.299R + 0.587G + 0.114B
Ybyte   = clamp(round-to-even(Ydouble), 0, 255)
```

必须为 `Ybyte` 增加边界和半值 Golden Vector。位运算只作用于 `Ybyte`，不能对 `double` 的二进制存储拆位，也不能在
投影后重新计算 Y。Alpha 不参与 Y 计算，透明像素隐藏的 RGB 仍按解码字节分析，并在界面说明中明确。

### 5.2 位提取与编号

对通道字节 `v ∈ [0,255]` 和 `b ∈ [0,7]`：

```text
bit(v, b) = (v >> b) & 1
weight(b) = 1 << b
```

界面按 `bit 7` 到 `bit 0` 从上到下排列；任何内部数组若使用 0–7 升序，必须在命名和测试中明确，不能依赖视觉顺序
猜测。Golden 至少覆盖 `0x00`、`0x01`、`0x02`、`0x7F`、`0x80`、`0xAA`、`0x55`、`0xFE` 和 `0xFF`。

### 5.3 8 位掩码

```text
single(b)      = 1 << b
keepHigh(min)  = (0xFF << min) & 0xFF
keepLow(max)   = (1 << (max + 1)) - 1     // max = 7 时显式返回 0xFF
kept(v, mask)  = v & mask
removed        = v & (~mask & 0xFF)
```

`BitMask8` 值对象负责验证范围和生成预设。UI 不手写位移公式；Document 不保存八个可能互相矛盾的布尔值，而保存一个
掩码，并通过只读行模型派生勾选状态。`mask=0x00` 是合法的“清空”，`mask=0xFF` 是合法的“全部”。

### 5.4 单位平面与组合显示

- 单位平面只观察一个 bit：0 输出 `(0,0,0,255)`，1 输出 `(255,255,255,255)`；
- 显示图 Alpha 永远为 255，包括观察 Alpha 通道时；否则 bit 0 会因为透明而不可见；
- 多位组合通道图输出 `kept` 的灰度 `(kept,kept,kept,255)`；
- 多位组合不按当前最大值拉伸到 255，避免把 bit 0 的权重 1 伪装成 bit 7 的权重 128；
- 界面并列显示“位结构图”和“真实量级重建图”，让可见性与数值贡献不混为一谈。

### 5.5 统计

一次完整通道扫描同时计算八个位的：

- 1 的数量、0 的数量；
- `oneRatio = ones / pixelCount`；
- 二元熵 `H = -p log2(p) - (1-p) log2(1-p)`，`p=0/1` 时对应项定义为 0；
- 理论单像素最大权重 `2^b`。

熵只描述 0/1 分布，不描述空间结构，也不是隐写概率。V1 不把熵着色为红色警报，不设置“可疑阈值”。空间噪声由位图
供人工观察；若以后需要邻接、游程、卡方或 RS 统计，应进入独立的 LSB 分析设计和数据集门禁。

## 6. 重建与导出语义

### 6.1 R/G/B

对所选颜色通道应用 `kept = sourceChannel & mask`，另外两个颜色字节与 Alpha 必须逐字节不变。`mask=0xFF` 的重建必须
与源图逐字节相同；`mask=0x00` 只把所选颜色通道清零。

### 6.2 Alpha

Alpha 重建只替换 Alpha 字节，RGB 包括完全透明像素下的隐藏 RGB 都保持不变。结果面板默认使用棋盘背景，同时提供“原始
Alpha 通道灰度图”，避免用户把透明后的肉眼结果误认为位数据消失。PNG 导出保留真实 Alpha。

### 6.3 Y

Y 先按 5.1 量化为 `Ybyte`，再计算 `targetY = Ybyte & mask`。重建保留源像素的 Cb/Cr 和 Alpha，使用与现有
`ImageChannelConverter` 相同的全范围逆变换，RGB 四舍五入并裁切到 0–255。界面和导出完成信息必须显示发生裁切的像素数。

为避免复制颜色公式，G0/G1 应把共享的单像素 YCbCr 正逆变换提取成领域层小原语，再让既有 `ColorSpaceConverter`、
`ImageChannelConverter` 和位平面重建共同调用。该重构必须先锁住频域分析、水印和比较测试，不能改变已有数值结果。

### 6.4 预览与完整结果

- 位必须在原始字节上提取；禁止先面积平均、双线性或最近邻缩放后再拆位；
- 大图预览使用与源图坐标一致的最近邻采样映射，最大边固定 1024，小图不放大；
- 源图、单位平面、组合图和重建图使用同一采样坐标，保证探针可对应；
- UI 切换掩码只创建受控代理，不创建完整尺寸重建图；
- 用户点击导出后才创建完整尺寸重建，并在编码完成或失败后立即释放临时引用；
- PNG 是唯一 V1 输出格式；导出使用现有原子写入，失败不得留下半文件。

## 7. SOLID 架构与朴素设计

### 7.1 依赖方向

```text
Features/BitPlaneViewer
  BitPlaneViewerDocument        实例状态、命令、Revision、取消和 Bitmap 生命周期
  BitPlaneViewerView            AXAML 布局与绑定
  BitPlanePreviewControl        仅坐标映射、棋盘背景和选中像素覆盖层
                 │
                 ▼
Application/BitPlanes
  IPrepareBitPlaneSessionUseCase   解码并建立当前图片 Session
  IAnalyzeBitPlaneChannelUseCase   抽取通道并一次计算八个位统计
  IProjectBitPlaneViewUseCase      生成单位/组合/重建代理和探针事实
  IExportBitPlaneImageUseCase      完整尺寸重建、PNG 编码和原子写入
                 │
                 ▼
Domain/BitPlanes + Domain/Imaging
  BitMask8、BytePlane、通道抽取、统计、投影、探针和重建
                 ▲
                 │
Infrastructure
  Avalonia 图片编解码、文件选择、Bitmap 适配和原子文件写入
```

依赖只由 Feature/Application 指向领域抽象与端口。Domain 不认识文件、Avalonia 或 DI；Infrastructure 不反向持有
Document；View 不直接调用领域算法。

### 7.2 单一职责（SRP）

- `BitPlaneChannelExtractor`：只把 `PixelImage` 转成一个只读 `BytePlane`；
- `BitPlaneStatisticsCalculator`：只在一个 `BytePlane` 上一次计算八个位统计；
- `BitPlaneProjector`：只生成黑白单位平面和灰度组合代理；
- `BitPlaneReconstructor`：只按通道和掩码重建像素并报告裁切；
- `BitPlanePixelInspector`：只返回某坐标的原值、二进制和掩码结果；
- Application 用例负责工作流和端口调用；
- Document 负责 UI 状态与生命周期；View/Control 负责展示和坐标转发。

不创建同时负责解码、拆位、统计、Bitmap、导出和状态的 `BitPlaneService`。

### 7.3 开闭与替换（OCP/LSP）

- 五个通道用显式枚举和穷尽 `switch`，未知值立即拒绝；V1 不为假想 16 位通道建立插件框架；
- 纯算法类没有真实替换需求时使用具体类构造注入，不为每个类机械创建单实现接口；
- 应用用例接口是测试替身和 Document 依赖倒置的真实替换点；
- 所有用例对取消、异常、空结果和所有权采用一致契约，测试替身不得需要 Document 特判；
- 未来新增 Cb/Cr 或 16 位必须新增数值协议和 Golden，不能让现有 `BitMask8` 悄悄改变含义。

### 7.4 接口隔离与依赖倒置（ISP/DIP）

- 复用已有 `IImageCodec`、`IImageFileDialog` 和 `IAtomicFileWriter`，不新增万能文件服务；
- 导出用例只接收 PNG 输出意图，不依赖 Payload、比较报告、指纹报告或鲁棒性报告对话框；
- Document 只注入四个窄用例、图片对话框和 `IDocumentLifetime`；
- 领域算法由组合根登记为无状态 singleton；Document 与 Session 保持 scoped/实例所有权；
- 禁止 Service Locator、静态可变缓存、反射扫描和在 View 中解析 `IServiceProvider`。

### 7.5 允许使用的朴素模式

- 值对象：`BitMask8` 保证 8 位掩码始终有效；
- 用例/应用服务：隔离 UI 工作流与纯算法；
- Adapter：沿用 Avalonia 编解码、Bitmap 和文件系统适配；
- Session：明确一张已解码图片和当前通道缓存的所有权；
- generation gate：防止旧异步结果覆盖新图片或新通道；
- 构造注入：显式表达依赖。

V1 不使用 Strategy 注册表、Abstract Factory、Mediator、Visitor、事件总线、动态插件发现或通用管线。这些模式没有当前
替换需求，只会模糊五通道和一个 8 位掩码的简单事实。

## 8. 建议领域与应用契约

以下签名表达职责边界，实施时可在不改变语义的前提下调整命名；不得把所有记录塞入一个巨型文件。

```csharp
internal enum BitPlaneChannel
{
    Red,
    Green,
    Blue,
    Alpha,
    Luma
}

internal readonly record struct BitMask8
{
    public byte Value { get; }

    public static BitMask8 Single(int bitIndex);
    public static BitMask8 KeepHigh(int minimumBit);
    public static BitMask8 KeepLow(int maximumBit);
    public bool Contains(int bitIndex);
    public byte Apply(byte value);
}

internal sealed record BitPlaneStatistics(
    int BitIndex,
    int Weight,
    long ZeroCount,
    long OneCount,
    double OneRatio,
    double BinaryEntropy);

internal sealed record BitPlanePixelReport(
    int SourceX,
    int SourceY,
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha,
    byte ChannelValue,
    string BinaryValue,
    byte Mask,
    byte KeptValue);
```

应用边界建议保持四个窄用例：

```csharp
internal interface IPrepareBitPlaneSessionUseCase
{
    Task<BitPlaneSession> ExecuteAsync(
        string sourcePath,
        CancellationToken cancellationToken);
}

internal interface IAnalyzeBitPlaneChannelUseCase
{
    Task<BitPlaneChannelAnalysis> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannel channel,
        CancellationToken cancellationToken);
}

internal interface IProjectBitPlaneViewUseCase
{
    Task<BitPlaneViewProjection> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        int focusedBit,
        CancellationToken cancellationToken);
}

internal interface IExportBitPlaneImageUseCase
{
    Task<BitPlaneExportResult> ExecuteAsync(
        BitPlaneSession session,
        BitPlaneChannelAnalysis analysis,
        BitMask8 mask,
        string outputPath,
        CancellationToken cancellationToken);
}
```

`BitPlaneSession` 拥有一张源图和固定预览坐标信息；`BitPlaneChannelAnalysis` 只拥有当前通道 `BytePlane` 与八个位统计。
二者不暴露可写数组。投影结果拥有受控 `PixelImage` 代理；Feature 转为 Avalonia Bitmap 后应尽快释放旧结果引用。

## 9. 交互与界面结构

### 9.1 建议布局

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ 选择图片 | 路径 | 分析 | 取消 | 导出 PNG                                │
├───────────────┬──────────────────────────────────────────────────────────┤
│ 通道           │ 原图                 单位平面                            │
│ ○ R ○ G ○ B   │ ┌─────────────────┐  ┌─────────────────┐                │
│ ○ Alpha ○ Y   │ │                 │  │                 │                │
│                │ └─────────────────┘  └─────────────────┘                │
│ bit 7  [✓] 128 │                                                          │
│ bit 6  [✓]  64 │ 组合通道             掩码重建                            │
│ ...            │ ┌─────────────────┐  ┌─────────────────┐                │
│ bit 0  [ ]   1 │ │                 │  │                 │                │
│                │ └─────────────────┘  └─────────────────┘                │
│ 全部 / 清空    │                                                          │
│ 仅高位 ≥ [4]   ├──────────────────────────────────────────────────────────┤
│ 仅低位 ≤ [3]   │ 像素：RGBA | Y/A/R/G/B | 0bxxxxxxxx | mask | kept       │
│                │ 统计：bit、权重、0/1 数量、1 占比、二元熵               │
└───────────────┴──────────────────────────────────────────────────────────┘
```

窄窗口下四个预览允许变成单列滚动；控制区顺序和键盘 Tab 顺序必须保持一致。不要依赖颜色区分 bit 0/1，黑白图之外
还要提供二进制文本、统计和像素探针。

### 9.2 交互规则

- 初始通道为 Y，初始焦点为 bit 7，初始掩码为 `0x80`；
- 点击某个位行会改变焦点位；勾选框改变组合掩码，两者语义分开；
- “单位平面”始终显示焦点位，“组合通道”和“重建”始终使用当前掩码；
- 全部设为 `0xFF`，清空设为 `0x00`；高/低位预设显示最终十六进制和二进制掩码；
- 掩码连续变化使用约 100 ms 防抖，并取消旧投影；只有最后 generation 可以提交；
- 切换图片会取消并清空所有旧结果；切换通道保留源图，只替换当前通道分析；
- 点击任一预览映射到同一原图坐标并刷新探针，不重新扫描全图；
- `mask=0x00` 不是错误，界面解释为“所选通道贡献被清空”；
- 导出前显示原始尺寸、通道、掩码和预计额外内存；导出完成后显示裁切数和路径；
- 错误、取消、空状态和“恢复后尚未重新分析”必须有可见文本，不只禁用按钮。

### 9.3 Alpha 专项显示

- 单位平面仍是完全不透明黑白图；
- 组合通道使用灰度显示 Alpha 数值；
- 重建图使用棋盘背景，并允许关闭棋盘核对实际透明效果；
- 探针同时显示源 Alpha、掩码和结果 Alpha；
- 完全透明像素的 RGB 不丢弃、不清零、不参与 Alpha 位判断。

## 10. 异步、资源和失败边界

### 10.1 资源预算

- 编码输入继续上限 64 MiB，解码像素继续上限 16,000,000；
- 一张完整 RGBA 源图最多约 64 MiB；当前通道 `BytePlane` 最多约 16 MiB；
- 四张 1024 最大边 RGBA 代理合计不超过约 16 MiB，不缓存五通道或八个位的全尺寸 RGBA；
- 完整尺寸导出可能临时再占约 64 MiB 重建图及 PNG 编码缓冲，必须串行且可取消；
- 同一 Document 同时只允许一个分析/投影链和一个导出动作；关闭时全部取消；
- 性能测试记录 1 MP、4 MP、16 MP 的通道扫描、投影、完整重建时间和托管分配，不提前写市场承诺。

### 10.2 generation 与取消

Document 至少维护源图 generation 和投影 generation：

1. 新图片推进源图 generation，取消分析、投影和导出，释放旧结果；
2. 新通道推进投影 generation，取消旧通道分析；
3. 新掩码/焦点位只推进投影 generation，不重新解码；
4. 用例在每行或固定像素批次检查 `CancellationToken`；
5. 返回 UI 后同时检查 generation、Session 引用、Document 未关闭和 token 未取消；
6. 迟到结果直接释放，不覆盖新状态，也不把取消显示成错误；
7. 导出捕获取消和 I/O 失败，保留有效分析 Session 供用户重试。

### 10.3 可恢复错误

- 文件不存在、编码超过上限、图片损坏或解码失败；
- 未分析就导出、导出路径为空或目标不可写；
- 快照通道 ID、bit、掩码或 schema 非法；
- 领域层收到尺寸不一致、越界坐标或未知通道；
- Y 重建发生裁切不是失败，但必须报告数量；
- 用户取消不显示红色错误，只恢复稳定空闲状态。

## 11. 中文注释和设计说明规定

本能力新增生产代码必须使用中文注释，并遵守以下门槛：

- 每个领域值对象、算法类、应用用例、Session、Document 和自绘 Control 都有中文 XML `summary`；
- 涉及位序、Y 量化、Alpha、最近邻采样、裁切、缓存所有权和 generation 的位置必须有 `remarks` 或行内原因说明；
- 注释解释“为什么采用该语义、输入输出由谁拥有、哪些近似会误导”，不复述 `for`、`if` 或属性名；
- 位移公式旁给出 bit 7/bit 0 示例；Y 的四舍五入方式和 Alpha 不参与 Y 的原因必须写明；
- 完整尺寸导出和 Bitmap 替换处说明临时内存与释放责任；
- 每个 G 包历史记录增加“设计思路”小节，说明 SOLID 责任划分和没有采用复杂模式的理由；
- 不写“永远安全”“能够检测隐写”等超出自动证据的结论；
- 合并前人工检查中文术语统一：位平面、最高有效位（MSB）、最低有效位（LSB）、掩码、重建、二元熵。

示例风格：

```csharp
/// <summary>把一个 8 位通道样本投影为不透明黑白位图。</summary>
/// <remarks>
/// 输出 Alpha 固定为 255，而不是复用被观察的 Alpha 位；否则 Alpha=0 的位置会因透明而不可见，
/// 用户看到的将是合成结果而不是位平面本身。该方法只负责显示投影，不改变重建样本的真实权重。
/// </remarks>
```

## 12. 单元测试与开发门禁

### 12.1 G0/G1 领域 Golden

- `BitMask8` 的 `0x00/0xFF`、single 0/7、high 0/7、low 0/7 和非法 -1/8；
- `0x00/01/02/7F/80/AA/55/FE/FF` 的八位序列；
- R/G/B/Alpha 直接取字节，Y 的黑、白、红、绿、蓝、灰和半值舍入；
- 1×1、奇数宽高、最大合法尺寸的索引和长度保护；
- 八个位统计满足 `zeros + ones = pixelCount`，全 0/全 1 熵为 0，50% 熵为 1；
- 源 `PixelImage` 与 `BytePlane` 不被算法修改；
- 已取消 token 在长循环中抛出 `OperationCanceledException`。

### 12.2 G2 投影

- 单位平面只产生不透明黑/白 RGBA，Alpha 通道观察也不例外；
- `0x80`、`0x0F`、`0xF0`、`0x55`、`0xAA` 组合灰度等于 `v & mask`；
- 多位组合不做归一化拉伸，bit 0 单独组合最大值只能是 1；
- 大图先拆位后取样，构造一个能区分“先缩放再拆位”的反例 Golden；
- 最大边 1024、纵横比、小图不放大、四个预览坐标一致；
- 预览点击四角、中心和边界映射到正确原图坐标。

### 12.3 G3 重建与导出

- R/G/B 各通道在 `0x00/0x0F/0xF0/0xFF` 下的手算像素；
- 未选 RGB 和 Alpha 逐字节不变，`0xFF` 全图逐字节恒等；
- Alpha 重建只改 Alpha，透明像素隐藏 RGB 保留；
- Y 保留 Cb/Cr 和 Alpha，验证黑白/彩色 Golden、四舍五入、裁切数和全掩码回归；
- 源图始终不变，结果拥有独立缓冲；
- PNG 编码后回读尺寸、RGBA 和 Alpha 正确；
- 原子写入成功、失败不留半文件、取消不报告成功；
- JPEG 输出意图在 V1 明确不可达。

### 12.4 G4/G5 用例与 Document

- 准备用例只解码一次，通道切换不重复解码；
- 通道分析一次生成八份统计，掩码切换不重复全图统计；
- 新图片取消旧图片，新通道取消旧通道，新掩码拒绝旧投影；
- 迟到成功、迟到失败和关闭后返回均不能覆盖当前状态；
- 两个 Document Scope 的路径、掩码、Session、取消源和 Bitmap 完全隔离；
- 快照只含路径和轻量参数，不含 RGBA、BytePlane、统计、Bitmap 或 PNG；
- 合法 schema 1 恢复、未知 schema、未知通道、bit 越界和缺失文件安全降级；
- Dirty/Revision 只随持久状态变化，悬停和进度不标脏；
- 关闭释放 Bitmap，导出失败后仍可继续观察和重试。

### 12.5 G6 UI、组合根与回归

- AXAML 编译绑定零错误，View 能在 Avalonia Headless 中构造；
- 无 Session、忙碌、取消、错误、Alpha、空掩码和有效结果状态可见；
- 八个位从 7 到 0 排列，权重 128 到 1，键盘可选择和勾选；
- 高对比主题中仍可通过文本和统计理解 0/1，不只依赖颜色；
- Module 恰好登记七个唯一 Persistable Document，新 ID/描述/分类正确；
- Standalone 通过真实 Module 和 DI 解析第七个 Document，不复制实现；
- 不登记 Tool、Workflow Action 或 Workbench Command；生产代码中不引入 AIFLOW；
- 既有水印、频域、比较、鲁棒性、指纹 149 项基线全部继续通过。

### 12.6 本地开发门禁命令

G7 必须从解决方案根目录执行并记录原始摘要：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

门禁要求：

- Debug/Release 构建均零警告、零错误；
- Debug/Release 测试均零失败、零跳过；
- 测试总数必须大于起始 149，并在 `testing.md` 写明新增数量和分类；
- 任何现有测试不得删除、跳过、放宽精度或改为只断言“不抛异常”；
- 所有位运算和 Y 量化必须有确定值断言；
- 不以 Standalone 截图替代自动测试，也不以自动测试替代 G7 有限人工交互清单。

## 13. 各 G 包交付和验收

### G0：产品与位语义基线

交付：

- 冻结本计划 1–6 节中的范围、位序、掩码、通道、预览、重建和导出语义；
- 为共享 YCbCr 小原语建立既有行为回归清单；
- 建立手算 Golden 表和内存预算；
- 创建 `history/g0-product-and-bit-baseline.md`，记录决定和非目标。

验收：没有未决定的 Alpha、Y 舍入、bit 编号、预览先后顺序或高低位边界。

### G1：纯领域基础

交付：`BitPlaneChannel`、`BitMask8`、只读 `BytePlane`、通道抽取、八位统计、共享颜色小原语回归。

验收：纯测试通过；Domain 无 Avalonia、文件、JSON、DI；中文注释说明位序、所有权和 Y 量化。

### G2：位平面与掩码投影

交付：单位平面、多位组合、同坐标最近邻代理、源图代理、统计投影和取消检查。

验收：证明“先拆位后取样”；Alpha 位图不透明；不缓存八张完整位图；mask 快速切换可取消。

### G3：重建、探针与 PNG 导出

交付：五通道重建、裁切报告、像素探针、完整尺寸 PNG 导出用例所需领域能力。

验收：全掩码恒等、未选通道不变、Y Golden、Alpha 隐藏 RGB、原子失败和回读测试全部通过。

### G4：应用用例与 Session

交付：四个窄用例、Session/ChannelAnalysis 所有权、取消、generation 输入输出和结构化错误。

验收：Document 可用替身验证所有分支；应用层不依赖 Bitmap；没有万能服务或 Service Locator。

### G5：Persistable Document 与生命周期

交付：稳定 ID、Module/DI 登记、Document 命令和状态、schema 1、Scope 隔离、Bitmap 替换与关闭释放。

验收：快照轻量且安全恢复；两个实例隔离；迟到结果门禁；贡献总数恰好七个且 ID 唯一。

### G6：联动 UI 与可解释性

交付：四预览布局、位列表、预设、统计、探针、棋盘背景、状态/错误、键盘与 Headless View 测试；Standalone
通过真实 Module 展示新 Document。

验收：无鼠标也能完成选择与导出；单位平面和真实贡献不混淆；空掩码、Alpha 和 Y 裁切都有可见解释。

### G7：本地封板与文档

交付：

- 执行 12.6 的本地 Debug/Release 全门禁；
- 新增 `README.md`、`guide.md`、`user-manual.md`、`mathematical-principles.md`、`testing.md` 和 `history/README.md`；
- 同步仓库 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md`；
- 创建 `history/g7-local-sealing.md`，记录实际数字、未执行事项和回滚方式。

验收：文档链接有效、完成状态只来自证据、代码注释符合第 11 节；不新增 Windows CI 或发布脚本。

## 14. 有限人工验收清单

G6/G7 使用 Standalone 做开发阶段人工检查，但不冒充真实 Host：

1. 打开不透明 PNG，依次检查 R/G/B/Y 的 bit 7 和 bit 0；
2. 用 `0xF0` 与 `0x0F` 比较高位轮廓和低位纹理，确认组合图没有偷偷拉伸；
3. 打开含半透明和全透明像素的 PNG，确认 Alpha 位图可见、棋盘重建正确、隐藏 RGB 不丢失；
4. 打开 JPEG，明确提示观察的是解码后像素，不是 JPEG 文件字节或 DCT 系数；
5. 检查全部、清空、仅高位、仅低位和任意多选，掩码文本与勾选一致；
6. 在四个预览点击同一视觉位置，探针坐标与二进制值一致；
7. 快速切换通道/掩码并立即换图，旧结果不得闪回；
8. 分析 16 MP 上限附近图片，观察取消、忙碌提示和内存释放；
9. 导出 R、Alpha、Y 重建 PNG 并回读，核对原始尺寸、Alpha 和裁切提示；
10. 保存/恢复 Document，确认只恢复路径参数且不会自动读取文件；
11. 同时打开两个实例，确认路径、掩码、探针和取消互不影响；
12. 在键盘和高对比主题下完成通道、bit、预设和导出操作。

## 15. 专用文档同步规则

实施过程中按现有能力目录惯例维护：

- `README.md`：能力入口、阅读顺序和当前状态；
- `user-manual.md`：面向第一次接触位运算的用户，使用图示化语言解释 MSB/LSB；
- `guide.md`：准确描述通道、掩码、预览、探针、导出、资源和限制；
- `mathematical-principles.md`：二进制展开、掩码、Y 量化、二元熵和重建公式；
- `implementation.md`：本文，保持总计划与完成状态一致；
- `testing.md`：记录自动测试分类、命令、实际总数、已证明和未证明事项；
- `history/`：G0–G7 实际记录，不复制成当前使用说明。

公共入口同步原则：规划阶段只标注“规划中”；生产实现和本地门禁全部完成后，才可改成“已实现”。未来能力清单保留
LSB 隐写的独立条目，并把位平面观察器从候选描述更新为实施证据链接。

## 16. 完成定义

只有同时满足以下条件，才可把本计划状态改为“开发实现与本地自动门禁完成”：

- G0–G7 均有实际历史记录，且没有把待办写成完成；
- 五通道、八个位、任意掩码、高低位预设、探针、重建和 PNG 导出闭环可用；
- SOLID 分层、窄接口、Scope、取消、generation 和资源边界全部有自动测试；
- 新代码中文注释覆盖算法语义、设计思路、所有权和关键取舍；
- Debug/Release locked 本地门禁零失败、零跳过、零警告；
- 既有 149 项测试全部保留，新增测试总数和分类写入 `testing.md`；
- 专用文档、四个公共入口和未来能力状态同步；
- 代码和贡献清单中没有 AIFLOW、Workflow Action 或 Workbench Command；
- 没有新增 Windows CI，也没有声称完成真实 Host、ZIP、安装/卸载或发布验收。

## 17. 发布阶段明确延期

以下项目不是本轮开发门禁，准备正式发布时再按公共部署与发布文档执行：

- Windows CI 与平台矩阵；
- 正式 ZIP 内容、manifest、哈希和可复现打包；
- 真实 Host Catalog、Dock、多实例保存恢复和卸载清理；
- Windows 安装目录、权限、长路径和中文路径；
- 不同 GPU/缩放/DPI/主题的窗口级人工验收；
- 真实大图语料的长时间内存、性能和泄漏观察；
- 发布说明、兼容性承诺和回滚演练。

本地 Release 配置只作为第二编译配置回归，不等于发布门禁，也不能把本文状态写成“已发布”。
