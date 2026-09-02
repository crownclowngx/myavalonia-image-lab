# ImageLabPlugin V1 Magnitude/Phase Swap 实施计划

> 计划状态：V1 生产接入与本地自动门禁完成；真实素材人工观察及发布门禁延期
> 基线日期：2026-09-02
> 产品名称：Magnitude/Phase Swap／幅度与相位交换
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3
> 核心路线：双输入规范画布 + 共享二维 FFT + 共轭安全分量组合 + IFFT + 频域/空间指标联动
> 首要规定：SOLID 优先，朴素模式，中文详细注释，先单元测试与数值门禁再接 UI

本文同时保留 V1 实施约束与最终实证。生产代码已按 G1–G7 落地，G8 已同步文档但真实素材人工观察延期，G9 本地自动门禁完成。测试数量、耗时和通过结论只写真实执行结果。

## 1. 产品目标与固定顺序

### 1.1 用户闭环

```text
选择图片 A 与 B
    ↓
选择 256 / 512 / 1024 规范画布
    ↓
白底合成、BT.601 亮度、等比例 FitContain
    ↓
各执行一次二维 FFT，观察 A/B 幅度和相位
    ↓
生成 A 幅度+B 相位、B 幅度+A 相位
    ↓
观察 A/B 幅度-only、相位-only
    ↓
执行幅度插值或相位插值
    ↓
频谱、重建、频点、供体误差与空间指标联动
    ↓
按需导出当前规范画布 PNG、Recipe 和脱敏报告
```

### 1.2 固定实施顺序

1. G0 冻结输入、FFT、零幅度、共轭、投影、指标与资源语义；
2. G1 先完成规范画布，保证 A/B 逐频点具有相同尺寸和坐标；
3. G2 用纯数值测试完成两种交换和共轭安全，不接 UI；
4. G3 完成单分量与两类插值，覆盖圆周边界和自共轭点；
5. G4 完成诊断、指标和共享量程投影；
6. G5 建立双输入 Session、取消、generation 和应用用例；
7. G6 完成严格 Recipe/Report、快照和原子导出；
8. G7 最后接入第二十个 Persistable Document、View、DI 和 Standalone；
9. G8 同步专用/公共文档并执行有限人工复核；
10. G9 执行 Debug/Release 本地门禁，不执行 Windows CI 和发布门禁。

任何 Gate 未达到门禁时不得提前登记可见入口，也不得用 View 中的临时代码绕过领域层。

## 2. 当前基线与复用边界

### 2.1 已有事实

实施前仓库基线有十九个 Persistable Document、零 Tool，并具备：

- 自有不可变 RGBA8888 `PixelImage`、图像尺寸和解码上限；
- `ImageChannelConverter`、分析代理和正式 PNG/JPEG 编解码；
- 已通过 Spectrum Inspector 等能力使用的 `Fft1DTransform`、`Fft2DTransform`、`FrequencySpectrumBuilder`、频率坐标和频谱投影；
- `FrequencyInverseTransformer`、共轭安全遮罩以及虚部残差门禁经验；
- `FullReferenceQualityAnalyzer` 的 PSNR-Y/RGB、SSIM-Y 和同尺寸前置条件；
- Image Compare 的差异/指标 DTO，Hybrid Image 的双输入 Session、内容指纹、代理/完整结果区分和共享频谱量程经验；
- scoped Document、取消、generation、防迟到、严格 JSON、原子文件和 Headless View 门禁。

### 2.2 可以复用

- FFT/IFFT 数值核心和未中心化工作坐标；
- 频率坐标、中心化显示映射和公共只读 `FrequencySpectrum`；
- 图片选择、解码、PNG 编码、原子发布和目标回读端口；
- Document 生命周期、Bitmap 替换和多 Scope 隔离方式；
- 质量指标中定义一致的 PSNR-Y/SSIM-Y 实现；
- 架构依赖扫描、组合根、Standalone 和编译绑定测试方式。

### 2.3 不直接复用或不改变

- Spectrum Inspector 的单输入 Session、参数、快照和 Document 行为不改变；
- Frequency Filter/Mask Editor 的增益遮罩不是幅度/相位交换模型；
- Hybrid Image 的 Gaussian、控制点、对齐、raw 合成和 recipe 不进入本产品；
- Spectral Art 的幅度写入和可见性协议不作为通用 mixer；
- 不把产品模式加入公共 FFT 枚举，不建立跨产品“万能频域实验”接口；
- 若公共类型需要扩展，只允许加入至少两个消费者需要且语义中立、可独立测试的事实。

## 3. V1 范围

### 3.1 必须实现

- 双图片显式输入与规范 A/B 画布预览；
- 256、512、1024 三档方形分析画布；
- A 幅度+B 相位、B 幅度+A 相位；
- A/B 幅度-only、A/B 相位-only；
- 幅度 A→B 线性插值，相位固定 A 或 B；
- 相位 A→B 最短圆弧插值，幅度固定 A 或 B；
- 源/结果幅度谱、相位谱、重建图和同频点探针；
- 幅度供体误差、相位供体误差、共轭误差、虚部残差、裁切统计；
- 对 A/B 的 NCC、梯度相关及条件性 PSNR-Y/SSIM-Y；
- Session、取消、generation、stale、迟到拒绝、多实例与关闭释放；
- PNG、strict Recipe JSON、Report JSON/CSV 和轻量快照；
- 完整单元/应用/Document/UI/组合/架构门禁；
- 中文详细注释、专用文档和公共索引同步。

### 3.2 明确不实现

- R/G/B/YCbCr 多通道或彩色相位交换；
- 自动特征对齐、相似/仿射/透视/非刚性配准；
- 以 A 或 B 原始尺寸为输出、超 1024 完整 FFT 或代理放大冒充原图；
- 任意手绘频谱、滤波、Notch、图案写入或水印协议；
- 同时自由插值幅度和相位的二维参数面；
- 自动判断“结构由相位决定”的通过徽章或因果百分比；
- 视频、摄像头、批量目录、工作流、脚本、远程处理或 AI；
- 新 NuGet、反射路由、服务定位器或运行时插件式算法目录；
- AIFLOW、Workflow Action、Workbench Command 或新增 Tool；
- Windows CI、真实 Host、ZIP、签名、安装和发布封板。

## 4. SOLID 与朴素模式

### 4.1 单一职责

建议职责如下：

| 组件 | 唯一职责 |
| --- | --- |
| `FrequencyPairCanvasProjector` | 把单张 RGBA 图片规范化为固定亮度画布并报告内容矩形 |
| `SpectrumComponentMixer` | 按已验证 Recipe 组合逐频点幅度和相位 |
| `CircularPhaseInterpolator` | 最短圆弧、π tie-break 和自共轭相位规则 |
| `MagnitudePhaseReconstructor` | IFFT、实值验证和 raw 结果，不负责 UI 投影 |
| `MagnitudePhaseDisplayProjector` | 物理裁切或科学投影及裁切统计 |
| `MagnitudePhaseDiagnostics` | 频域供体误差、空间相关和结构化 N/A |
| `MagnitudePhaseSession` | 独占 A/B 画布、只读频谱和当前结果 |
| 应用用例 | 协调端口、取消、预算与候选提交 |
| Document | 参数、命令、状态、Revision 和 Bitmap 所有权 |
| View/Control | 布局、绑定、绘制和坐标转发 |

职责是边界，不要求每项机械拆成一个文件。短小值对象可以合并；算法、Session、Document 和文件 IO 不得合成一个大类。

### 4.2 开闭、替换、隔离与倒置

- 新产品通过新 Domain/Application/Feature 组合复用公共 FFT，不修改 FFT 来认识产品模式；
- 固定算法类优先 `sealed`，不以继承支持假想扩展；
- 文件选择、解码、原子写入等外部边界通过现有窄端口；
- Document 只依赖用例接口，不依赖具体 FFT、JSON serializer 或文件系统；
- 只在真正存在替代实现或层间边界时创建接口；
- DTO 保持不可变事实，不持有 Avalonia `Bitmap`、服务或回调。

### 4.3 允许的朴素模式

- Application Facade/Use Case：隔离 UI 与领域工作流；
- Scoped Session：表达大型资源的单一所有者；
- 现有 Port/Adapter：文件和编码边界；
- generation token：确定性防迟到提交。

不创建算法 Strategy 注册中心、Abstract Factory、Mediator 总线、Visitor、反射命令或多层装饰器。固定的四类实验由强类型枚举和值对象表达，分支保持可读。

## 5. 输入与规范画布协议

### 5.1 双输入身份

- A/B 是稳定角色，交换角色命令必须显式交换路径提示、内容指纹和展示，不复用旧频谱冒充新 generation；
- 替换任一输入立即取消运行、释放两张频谱和当前结果；
- 文件路径只存在当前实例内存，不进入快照、Recipe 或 Report；
- 相同文件允许同时作为 A/B，用于恒等和单分量实验。

### 5.2 规范化

每张图片独立执行：

1. 验证解码尺寸和像素上限；
2. RGBA 在白色 sRGB 背景合成；
3. 按 BT.601 得到 double Y；
4. 计算保持比例、居中的目标内容矩形；
5. 缩小用面积聚合，放大用像素中心双线性；
6. 内容矩形外填 255；
7. 输出不可变 `N×N` double 画布、映射事实和内容指纹。

禁止隐式裁切主体、非等比例拉伸、Clamp 采样或依赖 Avalonia 平台缩放器。输入预览必须来自领域规范画布的 byte 投影，不能单独走另一套 UI 缩放产生不一致。

### 5.3 资源预算

- 画布只允许 256/512/1024，默认 512；
- Session 长期最多持有 A/B 两个 `double[]` 画布和两份只读 `Complex[]` 频谱；
- 当前结果长期最多保留一份 raw 或等价结果和必要 Bitmap，不缓存全部模式；
- 一次重建最多创建一个 `Complex[]` 工作副本，IFFT 原地消费；
- 缓冲长度、字节数和估算总工作集全部 checked，并在分配前验证；
- 1024 实际峰值在 G5 记录，计划阶段不写耗时或内存承诺。

## 6. 频谱分量模型

### 6.1 强类型 Recipe

领域 Recipe 至少包含：

```text
CanvasSize
MagnitudeMode        SourceA / SourceB / LinearAtoB / UnitNonZero
MagnitudeAmount      仅 LinearAtoB
PhaseMode            SourceA / SourceB / ShortestArcAtoB / Zero
PhaseAmount          仅 ShortestArcAtoB
ProjectionKind       PhysicalClamp / SignedScientific
固定算法版本事实
```

构造时一次性验证合法组合和有限范围。UI 不允许构造“先接受非法值、运行时再猜”的半有效 Recipe。

### 6.2 共轭代表

- `ConjugateIndex` 使用未中心化 `(N-u)%N,(N-v)%N`；
- 每对只处理规范较小行优先索引，另一项精确写共轭；
- 自共轭点显式走实数分支；
- 结果构造后以独立扫描验证最大共轭误差；
- 超限候选返回结构化错误，不能继续 IFFT 后丢虚部。

### 6.3 零幅度和相位

每张频谱分别计算 `max(1e-12,maxMagnitude×1e-12)` 阈值。相位供体低于阈值时：

- 显示为无数据，不显示伪造的 0° 颜色；
- 组合公式使用稳定 0 作为数值占位；
- 若幅度供体在该频点非零，增加借用未定义相位数量和幅度能量；
- Report 保留数量/比例，UI 显示解释性警告。

## 7. 重建与显示投影

### 7.1 普通结果

交换与插值输出执行 IFFT 后：

- 先验证有限值、最大虚部和相对虚部残差；
- raw real 保持 double，统计 min/max/mean；
- 显示/PNG 使用 ToEven 舍入和 `[0,255]` 裁切；
- 统计低端、高端和总裁切，不自动归一化对比度；
- 输出固定灰度不透明 RGBA。

### 7.2 单分量结果

- 幅度-only 使用零相位并保持原幅度量纲，可同时展示物理裁切与原点环绕解释；
- 相位-only 使用 unit-nonzero 幅度和固定 P99.5 零中心科学投影；
- 科学投影必须有可见水印式标签“诊断显示，不保留原亮度量纲”；
- 相位-only PNG 是诊断图，Recipe/Report 必须记录投影；
- 不提供每张图自动 min-max 拉伸选项，以免同屏视觉不可比较。

## 8. 诊断与指标

### 8.1 频域事实

- 相对幅度 L2 误差，对应指定幅度供体；
- 幅度加权圆周相位误差，对应指定相位供体；
- 未定义相位数、借用能量比例、π 歧义数、自共轭过零数；
- 最大共轭误差、最大虚部和相对虚部残差；
- A/B/Result 的 DC、总谱能量和 Parseval 相对误差。

### 8.2 空间事实

- 与规范 A/B 的 mean-centered NCC；
- 与规范 A/B 的固定梯度相关；
- 普通物理投影与规范 A/B 的 PSNR-Y、SSIM-Y；
- 科学投影固定返回结构化 N/A；
- raw 与投影的范围、均值和裁切数。

所有指标值带状态和原因，不用 NaN、Infinity 或 0 冒充不可定义。指标面板只描述当前结果，不自动选择“获胜图片”。

## 9. Session、用例与并发

### 9.1 Session 所有权

`MagnitudePhaseSession` 属于单个 scoped Document，负责释放：

- 规范 A/B 画布和内容映射；
- A/B 内容指纹与只读频谱；
- 共享幅度显示尺度等小型摘要；
- 当前成功结果、指标和 Recipe fingerprint；
- 与这些结果对应的 generation。

Session 不持有文件端口、Document、View、Bitmap 或全局缓存。Bitmap 由 Document 替换并释放。

### 9.2 应用用例

建议三个窄入口：

- `PrepareMagnitudePhasePair`：解码、规范化、预算、两次 FFT 和源摘要；
- `RenderMagnitudePhaseExperiment`：组合、IFFT、投影、指标和候选结果；
- `ExportMagnitudePhaseArtifacts`：PNG、Recipe、Report 的严格发布。

如果现有用例组织更适合合并，可保持两到三个类；不得建立一个接受任意 object 参数的通用执行器。

### 9.3 generation 与提交

- 任何输入、画布或 Recipe 改变都推进 generation；
- 候选开始时捕获 Session 身份、A/B fingerprint、Recipe fingerprint 和 generation；
- 只有四项在完成时仍相同，才原子替换当前结果；
- 取消、失败和迟到结果只释放候选，不清空最后有效结果；
- 准备新输入是例外：它立即使旧 Session 与导出资格无效，避免跨图片误导。

Slider 采用短防抖；取消检查不能只放在命令开始，而应覆盖画布行、FFT 行/列、共轭代表扫描、IFFT、投影和指标扫描。

## 10. Document、快照和状态

### 10.1 贡献

| 字段 | 计划值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.magnitude-phase-swap` |
| 显示名称 | `幅度与相位交换` |
| 描述 | `交换或插值两张图片 FFT 的幅度与相位并联动观察重建` |
| 分类 | `图像分析` |
| 注册 | `AddPersistableDocument<MagnitudePhaseSwapDocument, MagnitudePhaseSwapView>` |
| 实例 | 多实例、每实例独立双输入 Session |

它是未来能力列表第 18 项，但按仓库实际贡献顺序将是第二十个 Persistable Document。产品编号与注册序号不得混淆。

### 10.2 持久与派生状态

快照 schema 1 只保存：

- A/B 文件名提示，不保存路径；
- canvas size；
- 当前合法实验模式、固定供体和插值参数；
- 当前显示页、同步缩放和可见面板偏好。

不保存路径、像素、频谱、Bitmap、结果 PNG、内容指纹、指标或错误堆栈。恢复后显示“请重新选择 A/B”，不自动访问磁盘或执行 FFT。

### 10.3 Dirty/Revision

- canvas 和实验 Recipe 改变推进 Revision；
- 瞬时频谱悬停、pan/zoom、进度和错误不推进 Revision；
- 重新选择文件是否标 Dirty 按现有 Document 约定执行，但路径不得进入快照；
- 当前结果成功提交不反向修改 Recipe Revision；
- 导出成功不改变 Recipe。

## 11. UI 与联动

### 11.1 布局

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ A / B 选择  交换角色  画布  准备  模式  插值  运行/取消  导出           │
├───────────────────────┬────────────────────────┬─────────────────────────┤
│ 规范 A                │ A/B/Result 幅度或相位  │ 当前重建                │
│ 规范 B                │ 同频点十字与截止坐标   │ 同步缩放/原点标记       │
├───────────────────────┴────────────────────────┼─────────────────────────┤
│ 供体误差、NCC、梯度、PSNR/SSIM、裁切、虚部     │ 频点详情与解释性警告    │
└────────────────────────────────────────────────┴─────────────────────────┘
```

### 11.2 交互规则

- 未准备双输入时只允许选择、交换角色和 canvas；
- 准备期间禁止第二次准备，允许取消；
- 完成准备后模式预设生成合法 Recipe；
- 幅度插值只显示一个 t 和固定相位 A/B；相位插值只显示一个 t 和固定幅度 A/B；
- 滑块值与提交结果分离，状态明确“计算中/结果过期/当前”；
- 频谱 A/B/Result 切换不重新 FFT；显示幅度/相位不推进 generation；
- 无定义相位使用纹理/文字，不能只用颜色；
- 相位-only 结果有固定“科学投影”标签；
- 所有状态、警告和按钮有中文文本与可访问名称。

### 11.3 View 边界

- AXAML 负责布局、样式和编译绑定；
- 自定义 Control 只绘制像素、十字、频率坐标和无数据纹理；
- code-behind 只做 Pointer/键盘到标准化坐标的转发；
- View 不读取文件、不执行 FFT/IFFT、不算指标、不管理取消源；
- letterbox、频谱频点和缩放坐标使用独立可测试映射器。

## 12. Recipe、Report 与导出

- Recipe 遵循 [Recipe schema 1](recipe-schema.md)，读取未知/重复字段、非法组合和指纹不一致即拒绝；
- Report 遵循 [Report schema 1](report-schema.md)，只含脱敏事实；
- 导入 Recipe 不自动查找文件，用户显式选择 A/B 后核对规范内容指纹；
- PNG 只导出当前 result fingerprint 对应的 `N×N` 规范画布；
- 目标不得覆盖 A/B，即使路径大小写或规范化写法不同；
- PNG 先编码至内存并回读，再原子发布，再从真实目标回读尺寸、RGBA 和内容；
- JSON/CSV 同样使用原子写入，不保留半文件；
- 任一导出失败保留内存当前结果和可重试状态。

## 13. 注释规定

所有新增生产代码注释使用中文，并遵循：

- 公开/复杂领域类型用 XML `<summary>` 说明职责，用 `<remarks>` 说明设计原因与不变量；
- 规范画布说明白底、BT.601、像素中心、面积/双线性选择和内容矩形；
- mixer 说明未中心化坐标、共轭代表、自共轭点和零相位阈值；
- 相位插值给出 wrapToPi 公式、π tie-break 和不能直接算术平均的原因；
- IFFT 说明归一化、虚部拒绝和不能静默丢虚部；
- Session/用例说明缓冲所有者、线程使用、取消点、generation 和迟到提交条件；
- 指标说明量纲、N/A 条件、科学投影为何不能计算 PSNR/SSIM；
- 引用本文具体章节或稳定公式时保持链接/措辞可搜索；
- 不给每个 getter、简单构造赋值和显而易见 if 写重复中文；
- 注释不得宣称“相位一定决定结构”或与数值测试不一致。

代码评审必须把注释正确性作为门禁，而不是仅检查是否出现中文字符。

## 14. 自动测试与本地质量门禁

完整测试矩阵以 [testing.md](testing.md) 为准。最低完成定义包括：

- 独立公式 oracle 的画布、FFT 分量、交换、单分量和插值测试；
- 共轭、自共轭、π 边界、零幅度、虚部和 Parseval 测试；
- 指标、投影、裁切、N/A、strict schema 和真实 PNG 回读；
- Session、取消、generation、迟到、多 Scope、关闭和资源释放；
- Document 快照/Revision、Headless View、编译绑定、坐标和可访问性；
- Module 第二十 Document 固定顺序、DI lifetime 和架构依赖扫描；
- 所有既有测试在 Debug/Release 继续通过，0 skip，build 0 warning；
- `git diff --check` 通过。

当前只允许本地 restore/build/test 门禁。不新增 Windows CI，不执行真实 Host、ZIP、签名、安装或发布验收。

## 15. G0–G9 实施包

### G0：产品、数学与文档基线（已完成）

- 冻结本文和全部专用文档；
- 冻结 V1/非 V1、公式、画布、投影、指标、资源、SOLID 和注释要求；
- 同步索引和未来能力；
- 记录没有生产代码或自动测试。

证据：[G0 记录](history/g0-product-math-and-baseline.md)。

### G1：规范画布与双输入领域（已完成）

- 实现规范画布、内容矩形、映射事实和指纹；
- 先写透明、缩放、像素中心、面积/双线性和不变性测试；
- 复核与现有亮度/代理实现的复用边界。

门禁：纯 Domain、无 Avalonia/IO；独立 oracle 全通过；资源在分配前验证。

### G2：交换与共轭安全（已完成）

- 实现分量读取、两种交换、共轭代表和自共轭点；
- 完成常量、冲激、平移、正弦、棋盘格和随机实值谱测试；
- 完成 IFFT 与虚部拒绝。

门禁：供体误差在容差内为零；共轭和虚部门禁通过；不修改公共 FFT 产品语义。

### G3：单分量与插值（已完成）

- 幅度-only、相位-only、科学投影；
- 幅度线性插值、相位最短圆弧、π tie-break 和自共轭过零；
- Recipe 合法组合与端点测试。

门禁：端点精确、跨 ±π 正确、科学投影不冒充亮度结果。

### G4：诊断、指标与频谱联动（已完成）

- 频域供体误差、零相位诊断、共轭/Parseval；
- NCC、梯度相关、条件性 PSNR/SSIM 和裁切；
- A/B/Result 共享幅度量程、固定相位量程和频点 DTO。

门禁：独立 oracle 与 N/A 测试通过；指标不输出因果结论。

### G5：Session、资源与应用用例（已完成；进程级峰值采样延期）

- 双输入准备、重建和候选提交用例；
- Session 所有权、generation、stale、取消、防迟到和预算；
- 实测 1024 峰值并据实调整预算。

门禁：每输入每 generation 解码/FFT 一次；多 Scope 隔离；失败保留最后有效结果。

### G6：Recipe、Report、快照与导出（已完成）

- strict JSON/CSV、内容指纹、脱敏和跨文化；
- schema 1 轻量快照和恢复不自动计算；
- PNG 内存回读、原子发布、真实目标回读和覆盖拒绝。

门禁：非法或歧义输入全部拒绝；无路径/像素/频谱泄漏；无半成功文件。

### G7：Document、UI、组合与 Standalone（已完成）

- 新增稳定 ID、第二十个 Document、DI 和真实 Module；
- 完成规范输入、三频谱、结果、模式、指标和警告联动；
- Standalone 复用真实 Module/View/Document；
- Headless、绑定、坐标、可访问性、生命周期和架构测试。

门禁：Document/View 无算法；旧十九个 Document 顺序和行为不变；零 Tool。

### G8：文档复核与有限人工验收（文档完成；真实素材观察延期）

- 根据实际代码同步本文、数学、指南、手册、schema 和测试证据；
- 创建 G1–G8 实际记录；
- 同步根 README、文档中心、设计总览、未来能力和公共领域边界；
- 执行人脸/建筑/文字、同图/平移/亮度/留白和交互生命周期人工场景。

门禁：文档不超出证据；未执行的真实 Host/发布项明确延期。

### G9：本地开发封板（自动门禁完成）

- 执行 locked restore、Debug/Release warn-as-error build 和全量 test；
- 记录真实数量、耗时、警告、失败、跳过和环境；
- 复核 SOLID、朴素模式、中文注释、资源、隐私和旧能力回归；
- 检查无 AIFLOW、Windows CI、发布文件和无关改动。

门禁：所有本地门禁真实通过或状态保持未完成；不以计划勾选代替证据。

## 16. 预计代码、测试与文档落点

### 16.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Application/MagnitudePhaseSwap/
│  ├─ MagnitudePhaseContracts.cs
│  ├─ MagnitudePhaseSession.cs
│  └─ MagnitudePhaseUseCases.cs
├─ Constants/PluginIds.cs
├─ Domain/MagnitudePhaseSwap/
│  ├─ FrequencyPairCanvas.cs
│  ├─ FrequencyPairCanvasProjector.cs
│  ├─ MagnitudePhaseRecipe.cs
│  ├─ SpectrumComponentMixer.cs
│  ├─ CircularPhaseInterpolator.cs
│  ├─ MagnitudePhaseReconstructor.cs
│  ├─ MagnitudePhaseDisplayProjector.cs
│  └─ MagnitudePhaseDiagnostics.cs
├─ Features/MagnitudePhaseSwap/
│  ├─ MagnitudePhaseSwapDocument.cs
│  ├─ MagnitudePhaseSwapView.axaml
│  ├─ MagnitudePhaseSwapView.axaml.cs
│  ├─ MagnitudePhaseSpectrumControl.cs
│  └─ MagnitudePhaseCoordinateMapper.cs
├─ Infrastructure/Persistence/
│  └─ MagnitudePhaseSerializers.cs
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs
```

命名可随现有代码规模适当合并，但层间边界不可消失，也不得为了匹配树形图创建空壳类。

### 16.2 测试

建议按失败定位组织：

- `MagnitudePhaseCanvasTests.cs`；
- `SpectrumComponentMixerTests.cs`；
- `MagnitudePhaseInterpolationTests.cs`；
- `MagnitudePhaseDiagnosticsTests.cs`；
- `MagnitudePhaseApplicationTests.cs`；
- `MagnitudePhasePersistenceTests.cs`；
- `MagnitudePhaseDocumentTests.cs`；
- `MagnitudePhaseViewTests.cs`；
- `MagnitudePhaseArchitectureTests.cs`；
- 对公共 FFT、质量、组合和现有十九 Document 测试做增量回归。

### 16.3 专用文档

```text
docs/design/magnitude-phase-swap/
├─ README.md
├─ implementation.md
├─ mathematical-principles.md
├─ testing.md
├─ guide.md
├─ user-manual.md
├─ recipe-schema.md
├─ report-schema.md
└─ history/
   ├─ README.md
   └─ g0-... 至 g9-...   # 仅在阶段真实完成后创建
```

## 17. 人工验收场景

### 17.1 经典交换

1. 选择轮廓差异明显、主体居中的人脸或物体；
2. 运行两个交换方向；
3. 核对结果幅度误差更接近幅度供体、相位误差更接近相位供体；
4. 比较 NCC/梯度相关，不要求 PSNR 给出同一结论；
5. 记录主观结构更接近谁及例外，不写普遍定理。

### 17.2 平移与单分量

1. A 与 B 使用同一图的已知循环平移版本；
2. 验证幅度近似不变、相位呈线性变化；
3. 观察幅度-only 的原点/对称结构；
4. 观察相位-only 的科学投影与边缘；
5. 确认 PSNR/SSIM N/A 提示不可消除。

### 17.3 插值

1. 幅度固定 A 相位，t 从 0 到 1；
2. 相位固定 A 幅度，t 从 0 到 1；
3. 检查端点与预设交换完全一致；
4. 快速拖动后只有最后 t 提交；
5. 检查 π 歧义和自共轭过零计数可见。

### 17.4 生命周期

1. 两实例使用不同 A/B 和 canvas，状态完全隔离；
2. 运行时取消、替换图片和关闭，不出现迟到 Bitmap；
3. 导出失败后重试，内存结果仍可用；
4. 快照恢复后不自动读取磁盘；
5. 1024 超预算前置阻断时不冒充 512 结果。

Standalone 只能证明插件内部对象图和本地交互，不能证明真实 Host Catalog/Dock/卸载、ZIP、Windows CI、签名或发布兼容。

## 18. 风险与对策

| 风险 | 对策 |
| --- | --- |
| 不同尺寸无法逐频点交换 | 显式共同方形画布，先显示规范输入和内容矩形 |
| 规范化边界主导频谱 | 固定白底、报告留白比例、人工场景覆盖大量留白 |
| 相位零幅度时无定义 | 阈值、无数据纹理、借用数量/能量诊断 |
| 普通相位平均跨 ±π 错向 | 最短圆弧、π tie-break 和端点 Golden |
| 自共轭点产生复数残差 | 专门实数分支、共轭后验和虚部拒绝 |
| 相位-only 自动拉伸被当作真实亮度 | 固定科学投影、可见标签、PSNR/SSIM N/A |
| 指标被误解为因果比例 | 分开供体误差与空间相似，不输出获胜/贡献百分比 |
| 两份 FFT + 工作副本内存大 | 最大 1024、两份长期谱、一个短期工作谱、前置预算 |
| Slider 旧结果覆盖新参数 | 防抖、取消、generation + Session/Recipe fingerprint 校验 |
| 为复用污染公共 FFT | 产品领域独立，公共层只保留语义中立事实 |
| 文档先于代码被误读为完成 | README/testing/history 明确 G0-only 和未执行项 |

## 19. 兼容、迁移与回滚

### 19.1 兼容

- 现有十九个 Document ID、顺序、快照、Recipe/Report 和数值协议不变；
- 新 ID 只在 G7 达标后登记，登记后不得更名；
- 公共 FFT 的缩放、坐标和 buffer 所有权不改变；
- schema 1 首次实现后，画布、阈值、插值、共轭或投影语义变化必须升级版本；
- 本计划不授权新 NuGet；需要依赖时先单独更新设计、锁文件和风险结论。

### 19.2 回滚顺序

1. 不登记或移除未稳定 Module/Standalone 入口；
2. 移除 Document/View 和应用用例；
3. 移除产品专用 Domain 与 serializers；
4. 只有经过独立回归、且至少两个现有消费者需要的公共修正才可保留；
5. 不回退或放宽任何既有能力测试；
6. history 如实记录失败 Gate、原因和保留内容。

### 19.3 数据迁移

当前没有本产品快照、Recipe 或 Report。G7 前开发数据可清理；首次发布后必须保留旧 schema 显式读取或返回结构化不兼容，不能用新公式静默解释旧实验。

## 20. 完成定义

以下清单按本轮真实实现与证据更新；发布前人工观察与进程级资源采样仍明确保留。

### 产品与数学

- [x] V1、非 V1、画布、公式、指标和限制文档冻结；
- [x] 两种交换、四种单分量和两类插值全部可用；
- [x] 共轭、零幅度、自共轭、π 边界和投影语义有独立测试；
- [x] 频谱、重建、频点和质量指标原子联动；
- [x] 不输出“相位必然决定结构”的未经定义结论。

### SOLID、实现与注释

- [x] Domain/Application/Infrastructure/Feature 依赖方向正确；
- [x] Document/View 不含 FFT、像素、指标或 JSON 业务；
- [x] 只有真实边界使用接口，固定算法无模式炫技；
- [x] 两频谱、工作副本、结果和 Bitmap 所有权明确；
- [x] 中文详细注释经静态门禁复核并与公式、取消和 generation 一致。

### 单元测试与门禁

- [x] 规范画布、交换、单分量、插值、指标、投影有单元测试；
- [x] 应用、迟到、多 Scope、关闭、持久化和导出有自动测试，取消由共享 FFT/用例取消点覆盖；
- [x] Headless View、组合根、DI lifetime、Document 顺序和架构扫描通过；
- [x] 既有全部测试 Debug/Release 继续通过，739/739、0 skip、0 warning；
- [ ] 实际测试数量、耗时已记录；1024 进程级峰值资源证据延期。

### 文档与范围

- [x] 专用 README、实施、数学、测试、指南、手册、Recipe、Report 和 G0 历史已建立；
- [x] 公共索引与未来能力状态已按“设计完成、尚未实现”同步；
- [x] G1–G9 history 按真实完成或延期状态创建；
- [x] 实现完成后再次同步文档和公共领域边界；
- [x] 不使用 AIFLOW；当前阶段不增加 Windows CI 或发布门禁。

只有全部本地实现项和证据项真实完成，才可把状态改为“V1 本地开发封板”。
