# ImageLabPlugin V1 Image Oscilloscope 实施计划

> 实施状态：V1 生产接入与本地自动门禁完成；人工素材与发布验收延期<br>
> 基线日期：2026-09-02<br>
> 产品名称：Image Oscilloscope／图像示波器<br>
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3<br>
> 核心路线：单图全像素流式累计 + 固定密度栅格 + 多视图投影 + 源像素探针联动<br>
> 首要规定：SOLID 优先，朴素模式，中文详细注释，先单元测试与数值门禁再接 UI

本文冻结 V1 的范围、数值协议、职责、资源和实施顺序。任何测试数量、耗时、内存峰值和通过结论只能在真实执行后写入 `testing.md` 与 `history/`，不能用计划值冒充实证。

## 1. 产品目标与用户闭环

### 1.1 要回答的问题

- 亮度主要分布在阴影、中间调还是高光？
- 亮度沿图片横向怎样变化，哪些列出现高光或阴影堆积？
- R、G、B 三通道是否在相同位置和量级分布，是否有单通道裁切？
- 色度点云集中在中心还是外缘，主要偏向哪个色相方向？
- 饱和度、Hue 和色度半径如何分布？
- 当前鼠标下的源像素在 Waveform、Parade、Vectorscope 和直方图中落在哪里？

### 1.2 固定用户流程

```text
选择一张图片
    ↓
按白底 sRGB 合成并流式扫描全部源像素
    ↓
生成 Luma Waveform、RGB Parade、Vectorscope、四通道直方图
    ↓
生成裁切计数/覆盖层、饱和度/Hue/色度分布和平均色度向量
    ↓
在原图移动或固定探针，所有 Scope 显示同一源像素的采样点
    ↓
切换密度显示、裁切模式和阈值；不改变源图，不重新解码
```

### 1.3 成功标准

1. 同一输入和参数产生确定性相同的计数、探针坐标与显示投影；
2. 每类累计的样本总数与源像素数守恒；
3. 全图扫描不依赖分析缩略图，不因缩小而漏掉孤立裁切像素；
4. UI 不直接执行像素循环或颜色公式；
5. 两个 Document 可并排分析不同图片，关闭任一实例不会影响另一实例；
6. 任何失败、取消或迟到结果都不能覆盖最后一次有效分析；
7. 自动门禁覆盖数值、生命周期、UI 绑定、架构依赖和固定资源上限。

## 2. Host 形态与产品边界

### 2.1 固定为 Persistable Document

V1 追加为第 21 个多实例 `Persistable Document`，拟定稳定身份：

```text
myavalonia.plugin.image.lab.document.image-oscilloscope
```

选择 Document 而不是 Tool 的原因：

- Host Tool 是插件级 singleton，不适合持有某一张图片的独占 Session；
- 分析缓存、覆盖层和 Bitmap 需要与一个可关闭 Scope 同寿命；
- 用户需要并排打开多个实例比较不同曝光或调色版本；
- 轻量视图参数适合 Persistable Document 快照，源文件和大型数组不进入快照；
- 当前插件已有成熟的 scoped Document、取消、generation、Standalone 与 Headless 门禁。

Module 完成后应为二十一个稳定 Persistable Document、零 Tool、零 Workflow Action、零 Workbench Command。旧二十个 ID、顺序、schema 和行为不得改变。

### 2.2 V1 必须实现

- 单张 PNG/JPEG 等现有解码端口可读静态图；
- Luma Waveform，横轴对应源图归一化 x，纵轴对应 8-bit Y；
- RGB Parade，R/G/B 三段共享纵轴和密度量程；
- YCbCr Vectorscope，显示色度点云、中性中心、六个参考色目标和平均色度向量；
- R/G/B/Y 256-bin 精确直方图；
- 亮度高光/阴影与 RGB 任一通道高光/阴影计数、比例和诊断覆盖层；
- 饱和度 256-bin、饱和度加权 Hue 360-bin、色度半径 256-bin；
- 平均 Cb/Cr、平均色度半径和方向；
- 原图 hover 与 click pin 的像素探针，联动全部 Scope 标记；
- 全图精确累计、固定显示栅格、取消、generation、stale、快照和多 Scope 隔离；
- 生产代码中文详细注释、专用文档、公共索引和单元测试门禁。

### 2.3 明确不实现

- 视频文件、时间轴、摄像头、实时采集或帧率门禁；
- HDR、PQ、HLG、scene-linear、浮点图片、10/12/16-bit Scope；
- ICC profile、显示器色彩管理、Rec.709/2020 切换、legal/full range 切换或 IRE；
- 肤色线自动判断、肤色检测、人脸识别、白平衡错误判定或调色建议；
- LUT、曲线、曝光、饱和度、白平衡等编辑操作或图片写回；
- 反向从 Scope 框选全部源像素、区域统计、多个 ROI 或批量目录分析；
- 报告、CSV、Scope PNG、剪贴板和打印导出；
- 新 NuGet、GPU/Compute Shader、SIMD 专项优化或平台原生依赖；
- AIFLOW、Windows CI、真实 Host、ZIP、签名、安装或发布门禁。

导出、ROI、视频与 HDR 都需要独立语义和资源门禁，不能在 V1 实施中顺手加入。

## 3. 输入、Alpha 与颜色协议

### 3.1 输入事实

- 使用现有 `PixelImage` RGBA8888 和 16,000,000 像素上限；
- Domain 不读取路径，不持有 Avalonia `Bitmap`；
- 分析扫描源图全部像素，不先缩小再统计；
- 用于原图展示和裁切覆盖层的交互代理最大边 1024，不作为 Scope 计数事实源；
- 源 `PixelImage` 不可变，分析器不得原地修改 RGBA 缓冲区。

### 3.2 Alpha 语义

V1 固定按白色 sRGB 背景合成可见 RGB：

```text
Cvisible = roundToEven((A × C + (255 - A) × 255) / 255)
```

其中 `C` 为 R/G/B 原字节，`A` 为 Alpha。所有 Scope、直方图、裁切和颜色分布使用同一 `Cvisible`。这样全透明像素的隐藏 RGB 不会造成肉眼不可见的伪色偏；UI 必须显示“白底合成”事实。原 Alpha 保留在探针详情中，但不单独提供 Alpha Scope。

### 3.3 亮度与色度

采用现有 gamma-coded sRGB/BT.601 数值语义：

```text
Y  = 0.299R + 0.587G + 0.114B
Cb = (B - Y) / (1.772 × 255)
Cr = (R - Y) / (1.402 × 255)
```

`Y` 按 ToEven 舍入到 0..255 进入离散亮度 bin；`Cb/Cr` 保持 double 并按理论范围 `[-0.5,+0.5]` 投影。公式不是广播编码器，不添加 16/235 或 16/240 偏移，也不标 IRE。

## 4. 领域结果与累计协议

### 4.1 不可变分析结果

建议产品专用 `ImageOscilloscopeAnalysis` 包含：

- 源尺寸、总像素数和 Alpha 合成策略；
- Waveform 尺寸与 `uint[]` 密度；
- Parade 单通道尺寸与 R/G/B 三份 `uint[]` 密度；
- Vectorscope 尺寸与 `uint[]` 密度；
- R/G/B/Y 四份 256-bin `ulong[]`；
- 饱和度、Hue、色度半径分布；
- 高光/阴影的精确计数；
- 平均 Cb/Cr、平均色度半径、最大密度和显示量程事实；
- 用于探针换算的强类型坐标协议版本。

数组在构造时复制或只由结果独占，不向 UI 暴露可写引用。结果不包含路径、Bitmap、命令、取消源或回调。

### 4.2 Luma Waveform

- 固定高度 256，显示顶部为 255、底部为 0；
- 宽度 `min(sourceWidth, 1024)`，至少 1；
- 源像素 x 映射为 `floor(x × waveformWidth / sourceWidth)`；
- Y bin 映射到显示行 `255 - yBin`；
- 每个源像素对唯一密度格贡献 1；
- 所有密度格总和必须等于源像素数。

当源宽超过 1024，多列只在 x 方向汇入同一显示列，绝不丢弃像素或随机抽样。

### 4.3 RGB Parade

- 每个通道的内部栅格宽度与 Waveform 相同、高度 256；
- R/G/B 分别使用合成后字节值，显示行仍为 `255-value`；
- UI 水平排列三段，但 Domain 保留三个独立同尺寸栅格；
- 三段使用同一个密度显示上限，避免每通道单独拉伸掩盖强弱差异；
- 每个通道的密度总和都必须等于源像素数。

### 4.4 Vectorscope

- 固定 `512×512` 色度栅格；
- Cb 从左 `-0.5` 到右 `+0.5`；Cr 从下 `-0.5` 到上 `+0.5`；
- 投影先 clamp 理论边缘误差，再按 ToEven 映射到 `[0,511]`；
- 每个源像素贡献一个点，全部栅格总和等于源像素数；
- 显示中性中心、R/Mg/B/Cy/G/Yl 六个纯色参考目标；目标只作坐标参考，不表示 broadcast 合规框；
- 平均向量从未量化 Cb/Cr 在线累加得到，不能从显示栅格反推。

### 4.5 直方图与颜色分布

- R/G/B/Y 各 256-bin，计数总和分别等于源像素数；
- HSV 饱和度 `S=(max-min)/max`，`max=0` 时 S=0，按 ToEven 映射 0..255；
- Hue 使用标准 HSV 角度 `[0,360)`，灰阶或 `S<=1e-12` 不进入 Hue bin；
- Hue 分布使用饱和度权重 `S`，同时记录有定义像素数，不能把灰阶堆在 0°；
- 色度半径 `r=sqrt(Cb²+Cr²)`，按固定理论最大半径映射为 256-bin；
- 平均色度向量 `(meanCb,meanCr)` 是内容统计，不输出“偏色错误”真假值。

### 4.6 高光与阴影

默认阈值：

```text
ShadowThreshold    = 5
HighlightThreshold = 250
```

范围均为 0..255，且必须满足 `ShadowThreshold < HighlightThreshold`。定义：

- Luma shadow：`Ybin <= shadow`；Luma highlight：`Ybin >= highlight`；
- RGB shadow：`min(R,G,B) <= shadow`；RGB highlight：`max(R,G,B) >= highlight`；
- 额外显示 R/G/B 各自阈值计数，便于识别单通道裁切；
- 覆盖层是从完整源图按“代理像素覆盖的任一源像素命中即标记”生成的诊断 mask；
- 覆盖层不得写回源图，切换开关只改变显示组合。

阈值改变只重算裁切计数和覆盖层，不重新解码，也不重建与阈值无关的 Scope。可通过独立的 `ClippingAnalyzer` 对现有 Session 源图执行一次可取消扫描。

## 5. 密度显示协议

计数事实与显示亮度必须分离。V1 每个 Scope 先保留精确 `uint` 计数，再产生可丢弃的显示投影：

```text
display = clamp(log1p(count) / log1p(P99_5(nonZeroCounts)), 0, 1)
```

- 空栅格上限固定为 1；
- 百分位按确定性 nearest-rank 规则；
- Parade 的 P99.5 在三通道非零格合并集合上计算；
- Waveform 和 Vectorscope 各自计算显示上限；
- 显示主题可以改变颜色，不得改变计数、坐标或探针；
- 图例必须同时使用文字、刻度和形状，不能只靠红绿蓝颜色区分。

V1 可提供“线性/对数”显示切换，但它只重新投影现有计数，不重新扫描图片。默认对数；两种模式与上限算法均需固定测试。

## 6. 鼠标与采样点联动

### 6.1 主方向

V1 固定为“源图像素 → 全部 Scope 采样点”的精确联动：

1. View code-behind 只把 Pointer 转成源图显示区域中的归一化坐标；
2. 独立 `ImageProbeCoordinateMapper` 处理 letterbox、缩放、边界和最后像素；
3. Document 用源坐标从 Session 取得合成 RGB、原 Alpha、Y、Cb、Cr、HSV；
4. `ScopeProbeMapper` 计算 Waveform、R/G/B Parade、Vectorscope、直方图和三种分布的 bin；
5. 各自定义 Control 只绘制十字、圆环或刻度线，不重新计算颜色值。

Hover 是短生命周期预览，不修改 Dirty/Revision；click pin 固定探针坐标并进入轻量快照。鼠标离开后，未固定探针消失；固定探针继续显示。

### 6.2 V1 不做的反向选择

Scope hover 可以显示该密度格的精确计数和坐标范围，但不反向枚举、框选或高亮所有源像素。后者需要额外倒排索引或每次全图扫描，会扩大内存与交互协议，应留给 ROI/反向选择设计。

## 7. SOLID 与朴素设计模式

### 7.1 单一职责

| 组件 | 唯一职责 | 明确不负责 |
| --- | --- | --- |
| `OscilloscopeColorConverter` | 白底合成、Y/Cb/Cr/HSV 数值 | 文件、Bitmap、UI |
| `ImageOscilloscopeAnalyzer` | 一次扫描累计固定分析事实 | 阈值覆盖层、绘图、状态 |
| `ClippingAnalyzer` | 按阈值生成计数和有界覆盖层 | 修改图片、调色建议 |
| `ScopeDensityProjector` | 精确计数到显示亮度 | 重新分析像素 |
| `ScopeProbeMapper` | 源像素到各 Scope 坐标 | Pointer/letterbox 换算 |
| `ImageProbeCoordinateMapper` | 显示坐标到源像素坐标 | 颜色计算、Document 状态 |
| `ImageOscilloscopeSession` | 独占源图、分析和当前覆盖层 | 文件对话框、View |
| 应用用例 | 选择、分析、重算裁切、取消和候选提交 | 绘图、具体控件 |
| Document | 实例参数、命令、状态、快照与 Bitmap 所有权 | 像素循环、颜色公式 |
| View/Control | 布局、绑定、密度图与标记绘制 | Domain、IO、取消源 |

这是职责约束，不要求每个短小值对象机械拆成文件。紧密的纯值对象可以同文件；Analyzer、Session、Document 和 View 不得合成大类。

### 7.2 开闭、替换、隔离和倒置

- 新能力通过产品专用 Domain/Application 组合复用 `PixelImage` 与文件端口，不修改公共图像类来认识 Scope；
- 固定算法服务优先 `sealed`，不通过继承支持假想格式；
- Document 只依赖应用用例接口，不依赖具体文件系统、解码器或分析器；
- 文件选择、图片解码等现有真实外部边界继续使用 Port/Adapter；
- 不为 Waveform/Parade/Vectorscope 分别创建可运行时注册的 Strategy；
- 不建立 `ScopeFactory`、Mediator、事件总线、服务定位器或反射命令路由；
- 强类型枚举只表达有限显示模式、裁切模式和状态，不接受任意字符串分支。

### 7.3 允许的朴素模式

- Application Use Case/Facade：隔离 UI 与分析工作流；
- scoped Session：表达源图和大型结果的单一所有者；
- 现有 Port/Adapter：隔离文件选择和图像解码；
- generation token：防止迟到结果提交；
- 不可变 Result/Value Object：冻结分析事实和坐标协议。

## 8. Session、异步与状态

### 8.1 Session 所有权

每个 Document Scope 独占一个 Session，长期最多持有：

- 一张完整源 `PixelImage`；
- 一份完整分析结果；
- 一份最大边 1024 的显示代理；
- 一份当前裁切覆盖层；
- Scope 显示 Bitmap，由 Document 在 UI 线程替换和释放。

Session 不进入 singleton 服务，不跨 Document 分享可变缓存。新输入成功提交后释放旧 Session；解码或分析失败时保留最后有效 Session 并显示错误，不能先清空后留下半成品。

### 8.2 generation 与取消

- 选择新图、重新分析、改变 Alpha 协议版本等完整分析参数时递增 analysis generation；
- 改变裁切阈值只递增 clipping generation；
- 改变线性/对数显示只重投影，不增加 analysis generation；
- 长循环至少每行检查取消，Vectorscope/分布投影也要有可测试检查点；
- 候选提交必须同时验证 Document 未关闭、generation 仍匹配、输入 fingerprint 未变；
- `OperationCanceledException` 转为取消状态，不吞掉其他异常；
- 关闭时取消全部任务，拒绝迟到候选并释放 Bitmap/Session。

### 8.3 Dirty、Revision 与快照

schema 1 只保存：

- Waveform/Parade/Vectorscope/Histogram 可见性和布局模式；
- 线性/对数密度模式；
- shadow/highlight 阈值与裁切显示模式；
- 固定探针的归一化坐标（若存在）；
- 视图缩放等轻量参数。

快照不保存绝对路径、源像素、密度数组、Bitmap、错误、进度或取消状态；恢复后显示“请选择图片重新分析”，不自动访问文件。hover、进度和错误不推进 Revision；用户提交的阈值、布局、显示模式和 pin 推进 Revision。

## 9. UI 与可访问性

### 9.1 建议布局

```text
顶部：选择图片｜分析｜取消｜密度模式｜裁切阈值｜状态
左侧：源图 + 高光/阴影覆盖层 + hover/pin 探针
中部：Luma Waveform / RGB Parade（可切换或上下排列）
右侧：Vectorscope + RGB/Y Histogram
底部：饱和度/Hue/色度分布、平均色度、精确像素读数和解释
```

窗口较窄时使用明确 Tab/折叠布局，不把四类 Scope 缩到无法读取刻度。密度栅格只保留一份数值结果，Control 按当前尺寸绘制或使用可丢弃 Bitmap，不因窗口 resize 重算 Domain。

### 9.2 View 边界

- AXAML 负责布局、样式和编译绑定；
- 自定义 Control 负责密度像素、刻度、参考目标和探针标记；
- code-behind 只转发 Pointer、尺寸和键盘焦点；
- 坐标、颜色公式、阈值和累计不进入 code-behind；
- 所有绑定设置 `x:DataType`；
- 不允许为每次 PointerMoved 分配大型对象或启动全图分析。

### 9.3 可访问性

- R/G/B 除颜色外使用字母、固定段位置和线型区分；
- 阴影/高光同时使用文字、图案和颜色；
- Scope 刻度具备文本替代，探针详情可被屏幕阅读器读取；
- 键盘可选择源图、执行分析、切换 Scope、固定/清除探针和调整阈值；
- 颜色不作为成功、失败、裁切或通道的唯一信息载体。

## 10. 资源预算

### 10.1 固定结构上限

在 16,000,000 像素输入上，Scope 大数组与原图尺寸解耦：

| 资源 | 上限 |
| --- | --- |
| Waveform | `1024×256×uint`，约 1 MiB |
| RGB Parade | `3×1024×256×uint`，约 3 MiB |
| Vectorscope | `512×512×uint`，约 1 MiB |
| 四直方图与三分布 | 小于 32 KiB |
| 交互代理 | 最大边 1024 的 RGBA |
| 裁切覆盖层 | 最大边 1024，紧凑 mask/投影 |

checked 乘法和字节预算必须在分配前验证。不得为每个源像素保存 Y/Cb/Cr/HSV，也不得建立从每个 Scope bin 到源像素列表的倒排索引。

### 10.2 性能规则

- 完整分析以一次行优先扫描同时累计所有主要事实；
- 裁切阈值重算允许独立再次扫描，但不重建其他 Scope；
- 不用严格毫秒数作为跨机器单元门禁；
- 实际耗时和进程峰值只在 G7 本地实测后记录；
- V1 不并行扫描，先保证确定性、取消和内存；只有实测证明必要且能保持计数确定性时才单独设计并行归并。

## 11. 预计代码落点

```text
src/ImageLabPlugin.Plugin/
├─ Application/ImageOscilloscope/
│  ├─ ImageOscilloscopeContracts.cs
│  ├─ ImageOscilloscopeSession.cs
│  └─ ImageOscilloscopeUseCases.cs
├─ Constants/PluginIds.cs
├─ Domain/ImageOscilloscope/
│  ├─ OscilloscopeColorConverter.cs
│  ├─ ImageOscilloscopeAnalyzer.cs
│  ├─ ImageOscilloscopeAnalysis.cs
│  ├─ ClippingAnalyzer.cs
│  ├─ ScopeDensityProjector.cs
│  └─ ScopeProbeMapper.cs
├─ Features/ImageOscilloscope/
│  ├─ ImageOscilloscopeDocument.cs
│  ├─ ImageOscilloscopeView.axaml
│  ├─ ImageOscilloscopeView.axaml.cs
│  └─ Scope 绘制 Controls
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs

tests/ImageLabPlugin.Tests/
├─ ImageOscilloscopeColorAndAnalysisTests.cs
├─ ImageOscilloscopeClippingAndProbeTests.cs
├─ ImageOscilloscopeSessionAndDocumentTests.cs
└─ ImageOscilloscopeViewAndArchitectureTests.cs
```

文件名可按实际规模合并，但职责依赖不可倒置。Document/View 不出现逐像素循环，Domain 不依赖 Avalonia、IO、JSON 或 DI。

## 12. G0–G8 实施顺序

### G0：产品、数学和资源基线

交付：冻结本文、数学文档和测试计划；确认第 21 个 Persistable Document、白底 Alpha、BT.601、坐标、阈值、密度量程与延期项。

门禁：不存在未决公式、范围、Host 形态或完成定义；不写生产入口。

### G1：颜色与坐标基础

交付：白底合成、Y/Cb/Cr、HSV、Scope 坐标和值对象；中文公式和边界注释。

门禁：纯色、灰阶、透明、边界、ToEven 和独立 oracle 单元测试通过。

### G2：全图精确累计

交付：Waveform、Parade、Vectorscope、四直方图和三类颜色分布的一次扫描 Analyzer。

门禁：合成图 Golden、计数守恒、最大尺寸 checked 预算、取消和源对象不变测试通过。

### G3：裁切与显示投影

交付：阈值值对象、精确计数、有界覆盖层、线性/对数密度投影和共享 Parade 量程。

门禁：阈值包含边界、孤立裁切像素、覆盖层保守聚合、P99.5 与零输入测试通过。

### G4：探针联动

交付：图片显示坐标、源像素探针、各 Scope/bin 映射、hover/pin 状态模型。

门禁：letterbox、缩放、边缘、最后像素、所有 Scope 坐标和 hover 不改变 Revision 测试通过。

### G5：Session 与应用用例

交付：选择/解码/分析/重算裁切用例、Session、取消、generation、stale 和失败保留。

门禁：多 Scope 隔离、快速重入、关闭、迟到拒绝、解码一次和大型资源释放测试通过。

### G6：Document、快照与组合

交付：Document 状态/命令、schema 1、服务登记、稳定 ID 和 Module 第 21 个贡献。

门禁：快照往返/非法回退/不自动读取，旧 ID/顺序不变，二十一个 Document、零 Tool 的组合测试通过。

### G7：View、Controls 与 Standalone

交付：编译绑定 View、Scope Controls、可访问性、Standalone 真实 Scope 和 Bitmap 生命周期。

门禁：Headless 加载、Pointer 边界、主题/resize 不重算、键盘和组合根测试通过；有限人工观察完成并据实记录。

### G8：本地封板与文档同步

交付：Debug/Release locked restore/build/test；更新根 README、docs 索引、未来能力和公共领域边界；填写真实历史记录。

门禁：本地自动命令全部成功且文档只陈述真实证据。不执行 Windows CI、真实 Host、ZIP、签名、安装和发布门禁。

## 13. 单元测试与质量门禁摘要

完整矩阵见 [testing.md](testing.md)。实现不得以 UI 截图替代以下自动门禁：

- 颜色公式和 Alpha 合成 Golden；
- Waveform/Parade/Vectorscope/直方图/分布计数守恒；
- 阈值边界、逐通道裁切与覆盖层；
- 显示密度 P99.5、线性/对数和共享量程；
- 图片坐标、Scope 坐标和探针联动；
- 取消、generation、失败保留、关闭和多实例隔离；
- 快照、编译绑定、Headless View、Module 顺序和架构依赖；
- 既有全部测试继续通过，测试数量只能在真实执行后更新。

## 14. 注释与实施纪律

- 新增生产代码注释全部使用中文；
- 颜色公式、理论范围、bin 量化、图像上下方向、Vectorscope 轴向必须有 XML `<remarks>` 或邻近设计注释；
- 累计数组的布局、总数守恒、checked 预算和单一所有权必须说明；
- Session 的线程假设、取消点、generation 和候选提交条件必须说明；
- Control 的坐标输入/输出与 code-behind 边界必须说明；
- 不给简单 getter、构造赋值和显而易见循环堆砌逐行注释；
- 不建立无真实替换者的接口，不用继承、反射、Mediator 或 Factory 炫技；
- 每个 Gate 先写/补测试，再实现，再填写实际历史；
- 若实现改变公式、默认值、边界或延期项，必须同步专用文档，不能只改代码注释；
- 不使用 AIFLOW；当前阶段不加入 Windows CI 或发布门禁。

## 15. V1 完成检查清单

以下项目已按代码、自动测试和本地门禁的真实证据核对：

### 产品与数值

- [x] Luma Waveform、RGB Parade、Vectorscope 和四直方图全部基于全图精确累计；
- [x] 饱和度、Hue、色度半径和平均 Cb/Cr 协议与数学文档一致；
- [x] 高光/阴影边界、逐通道计数与覆盖层一致；
- [x] 源像素 hover/pin 与全部 Scope 标记精确联动；
- [x] 源图在任何分析路径中均未被修改。

### 架构与生命周期

- [x] SOLID 依赖方向和单一职责门禁通过；
- [x] 设计模式保持朴素，没有万能 Engine 或多余 Strategy/Factory；
- [x] 第 21 个 Persistable Document、零 Tool，旧贡献身份和顺序不变；
- [x] 多实例、取消、generation、关闭和 Bitmap/Session 释放路径有自动与架构证据；
- [x] schema 1 不含路径或大型派生数据，恢复不自动分析。

### 测试与文档

- [x] Domain、Application、Document、UI、组合、架构与资源测试齐全；
- [x] Debug/Release 本地 `-warnaserror` 和全部 760 项测试通过；
- [x] 重要生产代码有详细中文设计注释；
- [x] 专用文档、公共索引、未来能力和公共领域边界同步；
- [x] 未宣称 Windows CI、真实 Host、ZIP、签名、安装或发布门禁已经执行。
