# ImageLabPlugin V1 鲁棒性实验室实施计划

> 计划状态：G0–G9 开发实现与本地自动门禁完成；真实 Host、ZIP、Windows CI 与发布封板按要求延期
> 基线日期：2026-08-30  
> 产品名称：Robustness Lab／鲁棒性实验室  
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3  
> 起始自动基线：Debug 97/97 通过、零跳过；完成证据：Debug/Release 120/120 通过、零跳过、零警告
> 核心路线：受控水印基线 + 有序扰动链 + 单参数扫描 + 分步诊断 + Profile 横向比较 + 版本化实验报告  
> 实施原则：SOLID 是首要约束；设计模式朴素使用；先冻结指标和随机性，再实现算子、诊断、用例、Document 与界面

| 实施包 | 当前状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 完成 | 冻结产品范围、术语、指标、随机性、资源和失败语义 | [实施记录](history/g0-product-and-metric-baseline.md) |
| G1 | 完成 | 建立实验配方、扫描计划、结果值对象和资源预算验证 | [实施记录](history/g1-experiment-domain.md) |
| G2 | 完成 | 完成确定性像素、噪声和颜色扰动算子 | [实施记录](history/g2-pixel-and-color-operators.md) |
| G3 | 完成 | 完成模糊、锐化和几何扰动算子 | [实施记录](history/g3-filter-and-geometry-operators.md) |
| G4 | 完成 | 完成 JPEG 信道和显式扰动链执行器 | [实施记录](history/g4-jpeg-and-chain-execution.md) |
| G5 | 完成 | 建立水印原始读数、BER、纠错和失败原因诊断 | [实施记录](history/g5-watermark-diagnostics.md) |
| G6 | 完成 | 完成扫描编排、分步探针、Profile 矩阵、质量指标和 Session | [实施记录](history/g6-experiment-use-cases.md) |
| G7 | 完成 | 完成 Persistable Document、取消、快照、导出和资源生命周期 | [实施记录](history/g7-document-lifecycle.md) |
| G8 | 完成 | 完成曲线、矩阵、分步解释和可访问界面 | [实施记录](history/g8-ui-and-explanation.md) |
| G9 | 完成（本地开发） | 完成本地双配置门禁、专用文档与开发阶段封板 | [实施记录](history/g9-local-sealing.md) |

本文定义 ImageLab 在频域水印、频域分析器和图像比较实验室之后的下一个产品能力。它不是一个只有
“攻击图片”按钮的演示页，而是一个可以复现实验配方、解释第一次失败位置、比较三种 Profile 并导出原始结果的
受控实验环境。

本文也是实施时的唯一总计划。每个 G 包完成后，必须在对应记录中填写实际修改、自动测试、数值证据、性能数据、
偏差、遗留风险和回滚方式。本文不得提前把“计划门禁”写成“已经通过”。当前阶段只执行本地开发门禁，不执行
Windows CI、ZIP、真实 Host、安装/卸载或发布封板。

## 1. V1 目标与固定实施顺序

### 1.1 用户闭环

```text
选择原始载体图片和实验 Payload
    ↓
选择隐蔽、均衡、鲁棒中的一个或多个 Profile
    ↓
在内存中建立可复现的水印基线，并先证明未扰动基线可以完整回读
    ↓
按顺序添加 JPEG、缩放、噪声、滤波、几何或颜色扰动步骤
    ↓
选择一个步骤参数做列表或范围扫描；随机算子选择固定种子和重复次数
    ↓
预检总案例数、分步探针数、像素上限和预计工作量
    ↓
串行执行每个 Profile、扫描点和重复试验，并在每个链前缀读取水印
    ↓
查看检测、Payload、完整性、BER、RS 修复、置信度、PSNR/SSIM 和局部质量
    ↓
查看强度—成功率曲线、Profile 比较矩阵和首次不可恢复观察位置
    ↓
选择任一案例查看基线、最终图、差异预览和逐步失败解释
    ↓
原子导出版本化 JSON 报告和扁平 CSV 数据，供复核而不是只保存一张截图
```

### 1.2 固定实施顺序

1. G0 先冻结“成功”、BER、纠错、置信度、图像质量、首次失败和随机重复的语义；
2. G1 再冻结稳定配方、单轴扫描、资源上限和结果 schema，不从 UI 控件倒推领域模型；
3. G2–G4 逐类实现可独立测试的扰动算子，最后才允许组合成有序链；
4. G5 为现有水印读取链增加窄诊断能力，严禁为了实验室复制一套协议或提取器；
5. G6 用应用用例协调基线嵌入、扫描、逐步探针、指标和 Session，不让 Document 执行像素循环；
6. G7 让 scoped Document 管理配方、取消、generation、快照、Session 与敏感数据；
7. G8 最后实现曲线、矩阵、案例详情和失败说明，图表不反向拥有实验状态；
8. G9 执行本地 Debug/Release 自动门禁并同步专用文档，不执行发布阶段门禁。

### 1.3 V1 的控制变量原则

V1 固定采用“一个实验只扫描一个参数”的朴素模型：链中可有多个步骤，但只有一个步骤的一个参数是扫描轴，
其余参数固定。Profile 和随机重复次数是独立比较维度，不与第二个扫描参数做笛卡尔积。

这样做的原因不是限制算法，而是保证曲线的横轴有明确含义、案例数量可预估、失败原因可以解释。多参数网格、
自动参数搜索、贝叶斯优化和所有步骤排列组合都留到以后建立单独设计，不在 V1 中通过通用工作流引擎实现。

## 2. 当前基线与已有事实

### 2.1 当前工程基线

当前仓库已经具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集，以及复用真实 Module 的 Standalone；
- “水印写入”“提取与验证”“频域分析器”“图像比较实验室”四个 Persistable Document；
- Y 通道 8×8 DCT-QIM 水印、固定控制信道、三种 Profile 和 RS(255,223)；
- PNG/JPEG 正式编解码、真实编码字节回读自检和 JPEG 质量参数；
- `ExtractionReport` 的检测状态、Payload、完整性、总 RS 修复符号数和汇总置信度；
- `PixelImage`、16,000,000 像素安全上限、64 MiB 编码输入上限与未预乘 RGBA8888 语义；
- `FullReferenceQualityAnalyzer` 的流式 PSNR-Y、PSNR-RGB、全局 SSIM-Y 和误差统计；
- 有界比较代理、差异场、热力图、统一摘要、原子文件输出和窄文件对话框端口；
- scoped Document、generation、取消、迟到结果保护、快照恢复和 Bitmap 替换模式；
- 97 个 Domain、协议、用例、生命周期、组合根和 Headless View 自动测试；
- `--locked-mode` restore、Debug/Release `-warnaserror` build/test 的本地门禁惯例。

### 2.2 可直接复用的能力

- 水印嵌入必须继续走 `WatermarkFrameProtocol` 与 `FrequencyWatermarkCarrier`；
- 水印最终结论必须继续走正式提取语义，不另写“实验版检测器”；
- `EmbeddingProfileId` 和 `EmbeddingProfile.Resolve` 是三种 Profile 的唯一事实源；
- `IImageCodec` 可承担 JPEG 编码—解码信道，不把 JPEG 算法搬入 Domain；
- `FullReferenceQualityAnalyzer` 可计算同尺寸、同坐标图像的全参考质量；
- `ImageAnalysisProxyProjector`、差异投影和热力图思想可用于选中案例预览；
- `IAtomicFileWriter`、比较报告文件选择和剪贴板适配器可以按意图扩展复用；
- 现有 Session、取消、快照和 Headless View 测试方式可作为生命周期基线。

### 2.3 当前缺口

- 当前 `ExtractionReport.CorrectedSymbols` 合并了 Header 与 Data 修复量，无法解释损伤发生在哪个信道；
- 当前 Carrier 只返回投票后的字节和平均置信度，不返回每副本原始判决，无法严谨计算纠错前 BER；
- Header 无法解码时，正式提取只给出“未检测到”，但受控实验仍需要比较原始控制信道读数；
- 当前没有稳定的扰动步骤 ID、参数 schema、随机种子、扫描定义或有序链执行器；
- 当前图片比较只接受同尺寸两图，不应被几何扰动用例偷偷扩展成自动配准器；
- 当前没有曲线、Profile 矩阵、逐步探针、失败分类或实验报告 schema；
- 当前文件对话框没有专门的鲁棒性报告导出意图；
- Module、Standalone 和持久身份目前只认识四个 Document。

### 2.4 主工程约束

- Plugin Module 仍是贡献和服务登记的唯一事实源；
- 鲁棒性实验室登记为第五个 Persistable Document，不登记 singleton Tool；
- 每个 Document 实例拥有独立 DI Scope、配方、Session、取消源和结果；
- Domain 不依赖 Avalonia、文件系统、JSON、DI、具体图片编码器或 Host；
- View 不执行图片扰动、水印嵌入、扫描、提取或文件写入；
- 不复制水印协议、RS、DCT-QIM、质量计算或图像编解码实现；
- 原则上不新增图像处理、图表、数学或原生运行时 NuGet；
- 不使用 AIFLOW，不登记 Workflow Action 或 Workbench Command；
- 不新增 Windows CI，不执行 ZIP、真实 Host 或发布门禁。

## 3. Document 形态与状态所有权

### 3.1 贡献形态

“鲁棒性实验室”固定登记为第五个 Persistable Document：

| 字段 | 固定值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.robustness-lab` |
| 显示名称 | `鲁棒性实验室` |
| 描述 | `以可复现扰动链、参数扫描和分步诊断测量 ImageLab 水印的恢复边界` |
| 分类 | `图像安全` |
| Host 注册 | `AddPersistableDocument<RobustnessLabDocument, RobustnessLabView>` |
| 实例基数 | 多实例，每个实例独立配方、扫描、密码、Session、取消令牌和结果 |

选择 Document 而不是 Tool 的原因：

- 原图、Payload、Profile、扰动链、扫描轴、随机种子和结果构成一个可恢复工作上下文；
- 用户可能并行比较不同载体、Payload 大小或算子顺序；
- 单例 Tool 会错误共享密码、Payload、当前案例和取消状态；
- 完整实验可能持续较长时间，必须有明确的关闭取消和资源释放边界；
- 配方需要 Dirty/Revision 和轻量快照语义，实验像素与结果不应进入 Host 布局。

### 3.2 Document 私有状态

持久状态：

- 原始载体路径和 Payload 来源类型；
- 仅在 Payload 来自文件时保存文件路径，不保存内联 Payload 内容；
- 选中的 Profile 集合；
- 有序扰动步骤及其非敏感参数；
- 扫描目标步骤 ID、参数名、列表/范围定义和重复次数；
- 固定实验种子、是否启用逐步探针；
- 当前选中的曲线、矩阵行列和案例键；
- 面板折叠、图表尺度和说明区显示状态。

仅存在于当前运行实例的派生状态：

- 已解码原图、每个 Profile 的基线水印图和预期诊断事实；
- 当前运行计划、案例结果、分步结果、聚合曲线和 Profile 矩阵；
- 选中案例的最终图、有界预览、差异代理和局部质量网格；
- 当前进度、取消源、generation、错误与资源统计；
- Avalonia Bitmap 和图表绘制缓存。

敏感或瞬时状态：

- 内联 Payload、恢复出的 Payload 和密码；
- Mapping Key、加密派生密钥、完整预期 Frame 和原始信道字节；
- Pointer 悬停、拖动、当前图表 tooltip 与临时参数输入错误。

敏感状态不得写入快照、日志、异常消息或导出报告。诊断内部可以短暂持有预期编码字节和 Mapping Key，但必须由
可释放的受控基线对象拥有，并在案例批次结束、重新运行或关闭 Document 时清零。

### 3.3 快照与恢复

- 快照 schema 从 `1` 开始，算子和参数使用稳定英文 ID，不序列化中文显示文字；
- 不保存图片字节、Payload 内容、密码、密钥、BER 原始位、结果表、预览或异常堆栈；
- 恢复时验证路径、Profile、步骤顺序、参数范围、种子、重复次数和资源预算；
- 未知算子或未知参数不能静默忽略，应保留可见的“不支持步骤”占位并阻止运行；
- 恢复后只恢复配方，不自动读取文件、嵌入水印或执行扫描；
- 文件不存在时保留配方并显示可恢复错误，不让 Host 布局恢复失败；
- 密码和内联 Payload 在恢复后保持空白，用户必须重新输入；
- 关闭 Document 时取消工作、清零敏感缓冲区、释放 Session 与 Bitmap。

## 4. V1 产品范围

### 4.1 必须完成

- 以原始载体 + Payload 建立受控水印基线，并可选择一个或多个现有 Profile；
- 基线未扰动时必须先由正式提取链完整恢复，否则实验不开始；
- 支持 JPEG 重编码、缩放、三类噪声、三类滤波、裁剪、补边、平移、旋转、轻度透视和颜色变化；
- 多个扰动按用户可见顺序组合，支持增删、启停、复制和上下移动；
- 一个实验支持一个参数轴的显式列表或等步长范围扫描；
- 随机算子支持固定种子和重复试验，结果不依赖案例执行顺序；
- 每个链前缀可执行水印诊断，用于定位第一次不可恢复观察位置；
- 输出检测状态、Payload 相等、完整性、原始物理 BER、投票后 BER、RS 修复、信道置信度和失败原因；
- 输出扰动相对水印基线、端到端相对原图的质量指标；尺寸不可比时输出 N/A 和结构化原因；
- 对同尺寸案例输出固定网格的局部误差变化，不伪称滑窗 SSIM Map；
- 输出强度—成功率曲线、三种 Profile 横向矩阵和选中案例详情；
- 原子导出版本化 JSON 总报告和 UTF-8 CSV 案例表；
- 支持取消、迟到结果保护、多实例 Scope 隔离、快照恢复和资源释放；
- 同步更新根 README、开发文档索引、未来能力状态、公共领域边界、专用用户指南、测试门禁和 G0–G9 记录。

### 4.2 明确不实现

- 把 JPEG 做成通用格式转换器或批量格式处理工具；
- 自动遍历所有算子顺序、多个扫描参数笛卡尔积或自动寻找最强攻击；
- AIFLOW、工作流节点、Workbench Command 或后台无人值守任务；
- 运行时第三方算子插件、反射扫描、脚本算子或任意表达式；
- 对几何变换做隐藏的特征配准、同步模板搜索或反向校正后再读取水印；
- 把缩放后的图片偷偷拉回原尺寸来制造可比较 PSNR/SSIM；
- 局部滑窗 SSIM Map、MS-SSIM、Delta E、感知哈希或深度模型；
- 视频、动画图、16-bit/HDR、ICC、广色域或 EXIF 方向保真；
- 历史数据库、远程实验队列、并行占满 CPU 的批处理引擎；
- 对任意图片、编码器或平台作“鲁棒”“安全”“不可破坏”的市场承诺；
- Windows CI、ZIP、真实 Host、安装/卸载或发布封板。

### 4.3 受控实验与外部水印图边界

V1 主路径只接受“原始载体 + Payload”，由实验室自己建立预期 Frame。这是计算 BER、区分 Header/Data 修复和
比较 Profile 的必要条件。

导入一张外部已有水印图可以作为以后扩展的“观察模式”，但它没有原始 Frame、Mapping Key 和未扰动基线，最多只能
给出正式提取结论与有限置信度，不能伪造 BER、Payload 相等、端到端质量或 Profile 矩阵。因此不进入 V1 必须范围。

## 5. SOLID 架构与依赖方向

### 5.1 分层

```text
Features/RobustnessLab
  RobustnessLabDocument           实例状态、命令、Revision、取消和生命周期
  RobustnessLabView               纯布局、绑定和可访问文本
  RobustnessCurveControl          曲线绘制与 Pointer 查询
  RobustnessMatrixControl         固定矩阵绘制与单元格选择
                 │
                 ▼
Application/Robustness
  IPrepareRobustnessBaselineUseCase
  IPlanRobustnessExperimentUseCase
  IRunRobustnessExperimentUseCase
  IProjectRobustnessCaseUseCase
  IExportRobustnessReportUseCase
  PerturbationChainExecutor       按显式登记的算子顺序执行
                 │
          ┌──────┴────────┐
          ▼               ▼
Domain/Robustness     Domain/Imaging + Domain/Comparison
  配方、扫描、结果     PixelImage、颜色、质量、差异代理
  纯像素扰动
          ▲               ▲
          └──────┬────────┘
                 │
Infrastructure
  JPEG 往返信道、Avalonia 编解码、文件对话框、原子写入
  现有水印 Carrier/Protocol 的受控诊断适配
```

依赖只允许由外向内。Domain 不知道 Avalonia、JPEG 编码器、文件路径、JSON 或 DI；应用用例不知道 Bitmap；
Document 不直接 new 算子、协议、编码器、文件流或 `ServiceProvider`。

### 5.2 单一职责

- `RobustnessRecipeValidator`：只验证步骤、参数、扫描轴和资源上限；
- `RobustnessExperimentPlanner`：只把 Profile × 扫描点 × 重复次数展开成稳定案例键；
- 每个扰动类只实现一种数学操作，不写报告、不提取水印；
- `PerturbationChainExecutor`：只按顺序执行步骤并触发前缀观察回调；
- `WatermarkDiagnosticReader`：只从受控基线读取原始判决与正式恢复结果；
- `RobustnessResultAggregator`：只聚合成功率、分位数、曲线和矩阵；
- `RobustnessReportSerializer`：只序列化稳定 DTO，不重新跑实验；
- 用例负责工作流，Document 负责当前实例，View/Control 负责展示和 Pointer 转发。

不得建立万能 `ImageAttackService`、通用 DAG、事件总线、命令总线、抽象工厂层或反射注册中心。V1 使用普通构造注入、
少量窄接口、不可变值对象和显式集合即可。

### 5.3 开闭原则与朴素 Strategy

多个算子确实需要统一执行边界，因此允许一个小型 Strategy：

```csharp
internal interface IImagePerturbationOperator
{
    PerturbationKind Kind { get; }

    ValueTask<PixelImage> ApplyAsync(
        PixelImage source,
        PerturbationParameters parameters,
        DeterministicTrialContext trial,
        CancellationToken cancellationToken);
}
```

约束如下：

- 实现通过组合根显式登记，禁止反射扫描；
- `Kind` 必须唯一，启动测试检查重复和缺失；
- UI 参数编辑器仍按冻结 schema 显式生成，不做任意对象属性反射；
- JPEG 实现留在 Infrastructure，纯像素实现调用 Domain 算法；
- 新增算子只增加一个实现、一个显式 schema 分支和测试，不修改已有算子行为；
- 不为每个算子再建立 Factory、Builder、Visitor 或 Decorator。

### 5.4 接口隔离

建议新增以下真实端口，不能把它们塞回包含所有文件动作的接口：

```csharp
internal interface IRobustnessReportFileDialog
{
    Task<string?> PickJsonOutputAsync(string suggestedName, CancellationToken cancellationToken);
    Task<string?> PickCsvOutputAsync(string suggestedName, CancellationToken cancellationToken);
}

internal interface IWatermarkDiagnosticReader
{
    WatermarkDiagnosticResult Read(
        PixelImage image,
        ControlledWatermarkBaseline baseline,
        string? password,
        CancellationToken cancellationToken);
}
```

图片选择继续复用 `IImageFileDialog`，Payload 选择继续复用 `IPayloadFileDialog`，输出继续委托 `IAtomicFileWriter`。
诊断接口不返回密码、Mapping Key、完整原始 Payload 或可修改的 Frame 缓冲区。

### 5.5 应用用例契约

建议冻结五个窄用例：

```csharp
internal interface IPrepareRobustnessBaselineUseCase
{
    Task<RobustnessBaselineSession> ExecuteAsync(
        PrepareRobustnessBaselineRequest request,
        CancellationToken cancellationToken);
}

internal interface IPlanRobustnessExperimentUseCase
{
    RobustnessExecutionPlan Execute(
        RobustnessRecipe recipe,
        IReadOnlyList<EmbeddingProfileId> profiles);
}

internal interface IRunRobustnessExperimentUseCase
{
    Task<RobustnessExperimentSession> ExecuteAsync(
        RobustnessBaselineSession baseline,
        RobustnessExecutionPlan plan,
        IProgress<RobustnessProgress>? progress,
        CancellationToken cancellationToken);
}

internal interface IProjectRobustnessCaseUseCase
{
    RobustnessCaseProjection Execute(
        RobustnessExperimentSession session,
        RobustnessCaseKey key,
        CancellationToken cancellationToken);
}

internal interface IExportRobustnessReportUseCase
{
    Task ExportJsonAsync(RobustnessExperimentReport report, string path, CancellationToken cancellationToken);
    Task ExportCsvAsync(RobustnessExperimentReport report, string path, CancellationToken cancellationToken);
}
```

`RobustnessBaselineSession` 与 `RobustnessExperimentSession` 均是 Document 私有、可释放的结果所有者。接口可在测试中替换
慢实现，但纯数学类不为“可 Mock”而机械增加接口。

## 6. 扰动配方、步骤与参数 schema

### 6.1 稳定步骤身份

每个步骤包含：

- V1 配方内唯一 `StepId`，使用创建时生成并持久化的稳定字符串；
- 稳定 `Kind`，如 `jpeg-reencode`、`gaussian-noise`、`rotate`；
- `Enabled`；
- 版本化参数对象；
- 中文显示名称只由 `Kind` 映射，不进入兼容判断；
- 列表顺序就是执行顺序，不再维护第二份依赖关系。

未知 `Kind` 必须阻止运行并保留原始参数 JSON，用户可以删除或替换该步骤。禁止恢复时静默跳过，因为跳过会改变
实验含义。

### 6.2 参数类型

V1 只支持明确的参数类型：有界整数、有界小数、布尔、固定枚举、RGB 颜色和二维数值。参数 schema 必须包含：

- 稳定英文参数名；
- 单位；
- 最小值、最大值和默认值；
- 是否可作为扫描轴；
- 对零值/恒等值的定义；
- 是否改变尺寸；
- 是否使用随机数；
- 参数非法时的中文错误说明。

不得把参数存成随意的字符串字典后在算法中到处 `Parse`。持久化层可以使用 DTO，进入 Domain 前必须转换为已验证
的强类型参数。

### 6.3 单轴扫描

扫描定义支持两种形式：

- 显式列表：按用户顺序列出值，去重后执行；
- 等步长范围：起点、终点、步长，使用十进制定点展开，避免二进制浮点累计多出端点。

扫描目标由 `StepId + ParameterId` 唯一定位。目标步骤被删除、禁用或参数不可扫描时，配方立即变为不可运行。
范围必须明确包含起点；只有步长正好到达终点时才包含终点，UI 预检显示实际点数和最终值。

### 6.4 资源上限

V1 冻结以下开发安全上限：

| 项目 | 上限 |
| --- | ---: |
| 扰动步骤数 | 12 |
| 单次扫描点数 | 101 |
| 随机重复次数 | 20 |
| Profile 数 | 3 |
| 完整案例数（Profile × 点 × 重复） | 300 |
| 分步观察数（案例 × 已启用步骤） | 1,200 |
| 输入/中间图片像素 | 16,000,000 |
| 报告中保留的预览 | 0；预览只存在于当前 Session |

计划器在开始前一次性验证这些乘积，使用 checked 算术并给出具体超限项。不能运行一半后才因组合爆炸失败。
这些是开发安全边界，不是性能承诺；实现测量后只能收紧或通过新版本显式调整。

## 7. 随机性与可复现性

### 7.1 两类随机源必须分离

- 水印加密、盐、nonce 和 Mapping Seed 继续使用现有密码学随机源；
- 噪声和随机像素位置使用实验专用确定性随机源；
- 绝不能为了复现实验把水印安全随机源替换成固定伪随机源；
- 也不能让噪声算子消费全局 `Random.Shared`。

### 7.2 试验子种子

每个随机步骤的子种子由以下稳定事实派生：

```text
SHA-256(
  schemaVersion || experimentSeed || profileId || scanPointCanonicalValue
  || trialIndex || stepId || operatorKind
)
```

只取实现冻结的字节段初始化专用 PRNG。这样即使案例排序、进度刷新或前一个步骤内部实现改变，其他案例的随机序列也
不会漂移。报告记录实验种子、派生算法版本和 trial index，不记录密码学密钥。

### 7.3 重复试验语义

- 确定性链默认重复次数为 1；设置更大次数时结果应完全相同，测试必须证明；
- 只要链中有随机算子，成功率分母就是实际完成的 trial 数；
- 被取消的未完成 trial 不进入成功率，但报告必须标记实验不完整；
- 同一种子、同配方、同输入像素和同实现版本必须产生相同中间像素与结果；
- 跨不同 JPEG 后端不承诺像素一致，报告必须记录 JPEG 实现与版本事实。

## 8. 候选扰动算子设计

### 8.1 JPEG 重编码

- 参数：质量 `1–100`，默认 `95`，可扫描；
- 固定处理：`PixelImage → JPEG bytes → 正式 Decode → PixelImage`；
- 使用现有 Avalonia/Skia 编码路径，不新增 JPEG 库；
- Alpha 语义必须显式：JPEG 不支持 Alpha，V1 只允许完全不透明输入进入该步骤，否则阻止并解释；
- 每次编码都从当前链输入开始，不从磁盘文件或前一次扫描输出复用；
- 报告记录质量、编码字节数和实现标识；不保存中间 JPEG 文件；
- 该算子只代表传输信道的有损扰动，不提供格式转换保存入口。

### 8.2 缩放

- 支持等比例比例因子和非等比例 `scaleX/scaleY`；
- V1 固定双线性插值，不提供一串未验证的插值器；
- 输出尺寸按原尺寸乘比例后使用 `MidpointRounding.AwayFromZero`，最小 1×1；
- 预检输出像素上限，超限在分配前失败；
- 允许链中显式再添加一次缩放恢复原尺寸，但报告保留两步，不能自动恢复；
- Alpha 与 RGB 一起做未预乘通道插值，并在文档中说明透明边缘局限。

### 8.3 噪声与确定性像素扰动

- 高斯噪声：参数为 RGB 标准差 `sigma`，均值固定 0，Box–Muller 或其他算法一旦冻结不得静默更换；
- 椒盐噪声：参数为受影响像素比例，盐/椒各占一半，RGB 设为 255/0，Alpha 不变；
- 确定性像素扰动：参数为整数幅度，按像素索引和通道使用固定正负模式，完全不消费随机源；
- 所有输出按 `[0,255]` 饱和裁切；
- 默认不修改 Alpha，避免把透明度破坏混入水印信道结论；
- `sigma=0`、比例 0、幅度 0 必须逐字节恒等且不得分配不必要的大型辅助数组。

### 8.4 模糊与锐化

- 高斯模糊：参数为 `sigma` 和由其确定的奇数核半径，核归一化并采用可分离卷积；
- 中值模糊：V1 只支持 3×3、5×5 两档；
- 锐化：固定 unsharp mask 语义，参数为 amount，模糊基线固定，避免“锐化强度”含义漂移；
- 边界固定使用 clamp-to-edge，并写入报告；
- RGB 参与处理、Alpha 保持；
- 实现按行检查取消，辅助内存有界，禁止为每像素创建集合或 LINQ 热循环。

### 8.5 裁剪、补边与平移

- 裁剪参数为左、上、右、下像素，均显式记录；输出不得小于 1×1；
- 补边参数为四边像素和固定 RGBA 填充色；
- 平移保持画布尺寸不变，参数为整数 `dx/dy`，空白区使用固定 RGBA 填充色；
- 不提供自动中心裁剪或“智能”填充；
- 裁剪/补边改变尺寸，平移不改变尺寸，但三者都会改变水印槽位的绝对对应；
- 报告必须把“同步位置丢失”列为可能失败原因，不能只显示“没有水印”。

### 8.6 旋转与轻度透视

- 旋转固定保持原画布尺寸，参数角度建议范围 `[-15°, 15°]`，以图像中心为原点；
- 轻度透视使用四个角的归一化偏移，V1 UI 限制每轴不超过边长的 10%；
- 两者都使用逆向映射和双线性采样，画布外使用固定 RGBA 填充色；
- 单应矩阵不可逆、数值不稳定或映射越界异常时在分配前失败；
- 不执行自动裁边、扩画布、内容对齐或读取前逆变换；
- V1 以小尺寸 Golden Matrix/像素基准验证坐标方向，不仅做“看起来差不多”的截图测试。

### 8.7 亮度、对比度、Gamma、饱和度和色偏

- 亮度：在线性公式中增加明确的 8-bit 偏移量；
- 对比度：围绕 127.5 缩放；
- Gamma：固定 `output = 255 × (input / 255)^(1/gamma)`，报告明确公式；
- 饱和度：复用冻结的亮度公式，在 RGB 与 Y 之间插值；
- 色偏：分别对 R/G/B 增加有符号偏移；
- Alpha 不变，结果饱和裁切并采用统一舍入；
- 恒等参数必须逐字节不变；不同颜色算子顺序通常不可交换，链 UI 必须保留顺序事实。

## 9. 扰动链执行语义

### 9.1 固定执行规则

```text
Profile 基线水印图
  → 克隆为案例工作图
  → Step 1 Apply
  → 可选 Probe 1
  → Step 2 Apply
  → 可选 Probe 2
  → ...
  → Final Probe
  → 质量计算与摘要
```

- 每个案例都从不可变基线开始，绝不从上一扫描点输出继续累积；
- 禁用步骤不执行、不计入前缀位置，但保留在配方；
- 算子不能修改输入 `PixelImage`，必须返回新的拥有者；
- 执行器按顺序尽早释放上一张中间图，长期只保留基线、当前工作图和选中案例预览；
- 算子失败时记录 `OperatorFailed` 和步骤 ID，不继续执行后续步骤；
- 取消不生成伪失败结果，也不把半成品提交给 Session。

### 9.2 第一次失败位置

“首次发生不可恢复失败”冻结为：按启用步骤顺序执行前缀诊断时，第一个不满足“Payload 完整恢复且完整性有效”的
步骤位置，称为 `FirstObservedUnrecoverableStep`。

这个定义必须同时说明：

- 它是观察到的链前缀，不证明该算子单独必然导致失败；
- 后续非单调处理可能偶然再次恢复，因此还要记录 `RecoveredAfterFailure`；
- Header 未检测、需要密码、协议不支持、数据不可恢复、完整性无效和算子异常是不同原因；
- 如果关闭分步探针，只能报告最终失败，首次位置为 `NotMeasured`，不得猜测。

### 9.3 顺序比较

V1 不自动排列步骤。用户可以复制配方并手动调整顺序，或复制某一步后上下移动。报告中的配方摘要与哈希必须包含
步骤顺序，因此两份不同顺序的结果不会被误认为同一实验。

## 10. 水印诊断与 BER 语义

### 10.1 受控基线事实

每个 Profile 的 `ControlledWatermarkBaseline` 在内存中持有：

- 未扰动水印图；
- 预期 Header 和 Data 的 RS 编码字节；
- Profile、Payload 摘要、内容类型和是否加密；
- 只供 Carrier 定位的 Mapping Key；
- 未扰动正式提取报告与基线质量；
- 可释放、可清零的敏感缓冲区所有权。

报告只导出 Payload 长度和 SHA-256 的截断实验标识，不导出 Payload、密码、Mapping Key、salt、nonce 或完整 Frame。

### 10.2 两种 BER

V1 同时定义并明确命名两种 BER：

1. `PhysicalRawBer`：每个实际载体副本的 QIM 原始 bit 判决与预期 bit 比较，发生在副本投票和 RS 之前；
2. `VotedPreEccBer`：副本加权投票后的编码字节与预期 RS 编码字节比较，发生在 RS 解码之前。

公式统一为：

```text
BER = 错误 bit 数 / 实际比较 bit 数
```

Header 与 Data 分开统计，并给出合计。分母为 0 时结果是 N/A，不是 0。Data 位置需要受控 Mapping Key；如果几何变化
导致槽位不足，报告 `InsufficientCarrierSlots`，只保留仍可比较的 Header 原始读数。

### 10.3 RS 修复统计

- Header 与 Data 的修复符号数必须分开；
- 合计值可继续映射到现有 `ExtractionReport.CorrectedSymbols`，保持既有调用兼容；
- RS 解码失败时不能用“最大可修复数 + 1”伪造修复量，应报告 `Unrecoverable`；
- 修复量为 0 不代表没有信道 bit 错误，可能已经由副本投票消除；
- BER、投票和 RS 三层都必须有独立测试。

### 10.4 置信度

- Header/Data 的物理判决平均置信度和最小分位摘要分开保存；
- 正式提取汇总置信度继续使用现有语义，不因实验室静默改公式；
- 置信度范围固定 `[0,1]`，NaN/Infinity 视为实现错误；
- 低置信但成功、较高置信却完整性失败都必须如实显示；
- V1 不从置信度推导未经语料校准的“成功概率”。

### 10.5 正式恢复状态

每个观察点至少保存：

- 是否检测到受支持 V1 水印；
- `WatermarkDetectionStatus`；
- 是否完整恢复 Payload；
- 恢复 Payload 是否与基线 Payload 逐字节相等；
- `IntegrityStatus`；
- Header/Data/总 RS 修复；
- Header/Data BER 与置信度；
- 用户可见失败分类和技术原因代码。

“成功”固定为：检测到受支持水印、Payload 完整恢复、与基线逐字节相等、完整性为 `Valid`。缺少密码、只检测到 Header、
CRC/认证失败或只恢复部分内容都不是成功。

## 11. 质量指标与局部变化

### 11.1 两组参考关系

每个最终案例尽量计算：

- `AttackOnlyQuality`：最终扰动图相对该 Profile 未扰动水印图，描述信道新增损伤；
- `EndToEndQuality`：最终扰动图相对原始载体图，描述嵌入和信道的总损伤。

未扰动基线另保存原图与水印图之间的 `EmbeddingQuality`。三组名称不得在 UI 中都简称“PSNR”。

### 11.2 尺寸与坐标可比性

- 只有尺寸完全相同才调用现有全参考分析器；
- 缩放、裁剪、补边导致尺寸变化时指标返回 N/A 和 `SizeMismatch`；
- 平移、固定画布旋转和透视尺寸相同，可以计算坐标对应质量，但说明它包含几何错位损伤；
- 不自动缩放、裁剪交集或配准后计算；
- `PositiveInfinity` PSNR 继续使用现有合法非有限数序列化规则。

### 11.3 局部质量变化

V1 的“局部质量”固定为同尺寸图片上的 `16 × 16` 归一化网格。每格流式计算：

- RGB MAE；
- Y MAE；
- 最大 RGB 绝对差异；
- 变化像素比例。

边缘格通过整数区间覆盖全部像素，空格不产生。它用于定位局部损伤，不称为 SSIM Map，也不输出未经验证的局部
质量阈值。完整 JSON 默认保存网格数值，CSV 案例表只保存最大格、平均格和位置摘要，避免列爆炸。

## 12. 扫描、聚合与比较矩阵

### 12.1 稳定案例键

案例键由 `ProfileId + ScanPointIndex + CanonicalValue + TrialIndex` 组成。结果排序固定先 Profile、再扫描点、再 trial，
与任务完成时序无关。

### 12.2 成功率曲线

每个 Profile、每个扫描点输出：

- 完成 trial 数、成功数和成功率；
- 各失败原因计数；
- BER、RS 修复和置信度的最小/中位/最大值；
- 可用质量指标的最小/中位/最大值；
- 第一次失败步骤的频次；
- 是否存在失败后恢复。

只有一个 trial 时 UI 显示 0% 或 100% 的单次观察，并明确标注 `n=1`，不能伪装成统计稳定的概率。

### 12.3 Profile 横向矩阵

矩阵行固定为扫描点，列固定为用户选中的 Profile。主单元格显示成功率，详情同时显示中位 BER、RS 修复、置信度和
质量。未选择的 Profile 不显示；容量不足或基线失败显示结构化 N/A，不作为 0% 成功率混入聚合。

### 12.4 失败解释优先级

一个案例可能有多个异常信号，主解释按以下优先级选择：

1. 算子参数/执行失败；
2. 载体槽位不足或尺寸超限；
3. 未检测到控制信道；
4. 不支持的版本/Profile；
5. 检测到但需要密码；
6. Data 超出纠错能力；
7. Payload 恢复但完整性无效或与基线不等；
8. 成功但 BER、修复量或低置信提示接近边界；
9. 完整成功。

详细面板保留所有事实，主解释只是为用户提供稳定入口，不得丢弃次要信号。

## 13. 应用工作流、取消与资源预算

### 13.1 基线准备

对每个选中 Profile：

1. 解码一次原始载体；
2. 估算容量，容量不足则该 Profile 形成结构化不可运行结果；
3. 用正式协议编码并嵌入；
4. 使用未扰动像素直接正式提取；
5. 确认 Payload 相等且完整性有效；
6. 保存受控诊断事实和嵌入质量。

任一选中 Profile 基线失败时，默认阻止整次比较，避免矩阵混入无效基线。UI 可让用户取消选择失败 Profile 后重新预检，
不能自动降级 Profile 或截断 Payload。

### 13.2 串行案例执行

V1 默认单案例串行执行。原因是每个案例可能持有多张最大 16M 像素图片，盲目并行会放大内存和 CPU 峰值。以后若有
真实测量证明可安全并行，应以显式并发上限和新资源门禁加入，不能直接使用 `Parallel.ForEachAsync`。

### 13.3 保留策略

- 长期保留原图和每个 Profile 的一张基线图；
- 每个案例执行时只保留当前输入、当前输出和诊断小对象；
- 默认只保存数值结果，不保存每步完整图片；
- 选中案例的最终图按需重放生成，并用 generation 防止旧投影提交；
- 当前选择变化时释放旧预览和 Bitmap；
- JSON/CSV 不嵌入图片 base64。

### 13.4 取消与迟到结果

- 新运行开始前取消旧运行并递增 generation；
- 取消后已完成结果可保留为 `Incomplete` Session，但必须与完整报告明显区分；
- 算子按行、核阶段或固定块检查取消；
- 水印读取、质量扫描、报告序列化和按需重放都检查取消；
- 迟到进度、案例、预览和错误只有 generation 一致才能提交；
- 关闭 Document 后任何回调不得访问已释放 Bitmap 或敏感缓冲区。

## 14. 界面与交互设计

### 14.1 总体布局

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ 原始图片 [选择]  Payload [文本/JSON/文件]  Profile [隐蔽][均衡][鲁棒] │
│ 密码 [••••••]  种子 [12345]  重复 [1]  [建立基线并预检]                │
├───────────────────────┬──────────────────────────────────────────────────┤
│ 扰动链                │ 扫描与运行                                      │
│ 1 JPEG q=95           │ 扫描：Step 1 / quality / 100..50 step -5       │
│ 2 高斯噪声 σ=2        │ 案例 33  分步观察 66  预计工作量                │
│ 3 亮度 +4             │ [开始] [取消]  进度/当前 Profile/当前案例       │
│ [+添加] [复制][↑][↓]  │                                                  │
├───────────────────────┴──────────────────────────────────────────────────┤
│ [成功率曲线] [Profile 矩阵] [分步失败] [案例详情]                       │
│                                                                          │
│                           图表或矩阵                                     │
│                                                                          │
├──────────────────────────────────────┬───────────────────────────────────┤
│ 基线 / 最终图 / 差异 / 局部网格       │ 检测、Payload、BER、RS、置信度    │
│                                      │ PSNR/SSIM、首次失败与原因说明      │
├──────────────────────────────────────┴───────────────────────────────────┤
│ [导出 JSON] [导出 CSV]  状态、警告与敏感信息说明                        │
└──────────────────────────────────────────────────────────────────────────┘
```

### 14.2 参数编辑与预检

- 添加算子使用固定分类菜单，不使用可搜索插件市场或脚本入口；
- 选中步骤后只显示该算子的强类型参数、单位、范围和恒等值说明；
- 扫描参数在步骤旁有明确标记，同一时间只能有一个；
- 执行前必须显示案例数、分步观察数、是否随机、尺寸风险和预计输出尺寸范围；
- 参数非法、容量不足、JPEG Alpha 不兼容、输出像素超限或配方未知时禁用运行并显示具体原因；
- 调整配方、Profile、Payload、密码或种子后立即使旧结果标记为过期。

### 14.3 曲线与矩阵

- 曲线 X 轴使用参数真实单位，Y 轴成功率固定 0–100%；
- 离散枚举扫描使用分类轴，不伪装成连续数值；
- 图例同时使用文字、线型和标记，不只依靠颜色；
- 每个点可通过键盘聚焦，读出 Profile、参数、成功数/总数和失败主因；
- 矩阵提供等价表格语义，颜色热力只是辅助；
- 未完成、N/A、0% 和 100% 使用不同文字，不能只靠色块区分。

### 14.4 失败说明

详情应按“观察事实 → 最可能机制 → 可验证建议”组织，例如：

```text
观察事实：Step 2 平移 (dx=1) 后控制信道未通过 RS 解码。
可能机制：V1 水印按绝对 8×8 块位置读取，1 像素平移破坏了块同步。
建议：单独运行该平移步骤或在前缀结果中比较 BER；工具不会自动反向平移后重试。
```

说明只能基于结构化事实和已冻结的协议机制。不得把所有失败都归因于“攻击太强”，也不得声称已经证明唯一因果。

### 14.5 View 与代码隐藏边界

- View 只做绑定、布局、可访问名称和视觉状态；
- 曲线/矩阵 Control 只绘制不可变显示模型并转发选择；
- 代码隐藏只处理 Pointer、键盘、尺寸映射和绘制失效；
- 不在 AXAML converter 中执行聚合、BER、图片处理或文件访问；
- 不使用定时器驱动实验；进度由用例显式报告并节流到 UI；
- Headless 测试必须加载第五个 View，并验证关键绑定和可访问文字。

## 15. 报告与持久化 schema

### 15.1 JSON 总报告

`RobustnessExperimentReport` schema 从 `1` 开始，至少包含：

- 插件版本、报告 schema、UTC 时间、实验是否完整；
- 输入文件名或隐私化标识、解码尺寸，不导出绝对路径；
- Payload 类型、长度和截断摘要，不导出内容；
- 所选 Profile 与各自基线质量；
- 完整有序配方、参数、扫描轴、实验种子和 PRNG 版本；
- JPEG 实现标识、颜色/插值/边界/舍入规则版本；
- 每个案例与每个前缀观察的原始数值；
- 聚合曲线、Profile 矩阵、失败原因计数；
- 取消、异常、N/A 原因与资源统计；
- 明确的“开发实验结果，不是通用鲁棒性承诺”声明。

JSON 枚举使用稳定英文值。非有限 PSNR 按现有比较摘要的合法规则处理。Serializer 必须拒绝 NaN 置信度、负计数、
分母不一致或案例键重复，不能把内部错误写成看似正常的报告。

### 15.2 CSV 案例表

CSV 一行一个最终案例，固定 UTF-8 BOM 策略和 RFC 4180 转义，列至少包括：

- Profile、扫描参数、值、trial、成功；
- 主失败代码、首次失败步骤、是否失败后恢复；
- Header/Data Physical BER、Voted BER、RS 修复、置信度；
- Attack-only 和 End-to-end 的可用质量指标；
- 局部最大误差格摘要；
- 完成/取消状态。

CSV 不展开完整分步观察和局部网格；需要完整证据时使用 JSON。

### 15.3 原子输出与隐私

- JSON/CSV 先完整序列化到受控缓冲区，再调用 `IAtomicFileWriter`；
- 取消或写入失败不留下半个正式目标文件；
- 默认建议文件名只使用 UTC 时间和配方短标识；
- 不导出绝对路径、密码、Payload 内容、恢复内容、Mapping Key、salt、nonce 或异常堆栈；
- 文件名本身可能敏感，报告默认只保留不含目录的名称，并允许用户关闭；
- 导出失败不销毁仍有效的 Session。

## 16. G0–G9 实施包

### G0：产品、指标与资源基线

目标：把所有会影响结论的语义先写清楚。

- 冻结 V1 主路径、受控基线、单轴扫描和逐步探针；
- 冻结成功、两类 BER、RS、置信度、质量、局部网格和第一次失败定义；
- 冻结算子参数、插值、边界、舍入、随机性和 Alpha 语义；
- 冻结步骤/案例上限、串行执行和敏感数据边界；
- 建立 `docs/design/robustness-lab/history/README.md` 与 G0 记录；
- 复跑现有 97 项基线并记录实际证据。

门禁：术语无二义性；不存在无法计算却写为 0 的指标；没有代码改动；97 项现有测试不回归。

### G1：实验领域模型

目标：先建立不依赖 UI 和算法实现的稳定配方与结果核心。

- 实现步骤、强类型参数、扫描定义、配方验证和稳定哈希；
- 实现确定性计划展开、案例键、资源乘积验证和排序；
- 实现观察、案例、曲线、矩阵、失败原因和报告领域模型；
- 建立 schema 1 DTO 映射，但暂不接文件系统；
- 为未知算子、非法范围、溢出、重复 StepId 和资源上限建立测试。

门禁：同一配方稳定展开；非法配方在执行前失败；Domain 零 Avalonia/JSON/文件依赖。

### G2：像素、噪声和颜色算子

目标：完成低风险、可确定性验证的第一组纯 Domain 算子。

- 确定性像素扰动、高斯噪声和椒盐噪声；
- 亮度、对比度、Gamma、饱和度和 RGB 色偏；
- 固定 PRNG 与子种子派生；
- 恒等值、饱和裁切、Alpha 不变、源对象不变和取消；
- 小矩阵 Golden Vector 与统计分布宽容门禁。

门禁：相同种子逐字节一致；案例顺序变化不改变输出；恒等参数逐字节不变；不创建全图 `double[]` 噪声场。

### G3：滤波与几何算子

目标：完成有明确坐标和边界语义的第二组纯 Domain 算子。

- 可分离高斯模糊、中值模糊和 unsharp mask；
- 等比/非等比双线性缩放；
- 裁剪、补边、固定画布平移；
- 固定画布旋转和轻度透视逆向映射；
- 输出尺寸预检、像素上限、取消和辅助内存门禁。

门禁：小矩阵坐标方向、边界和舍入有精确断言；恒等变换逐字节不变；几何输出尺寸和 Alpha 语义固定。

### G4：JPEG 与链执行

目标：把正式 JPEG 信道与纯算子组合成简单可靠的有序执行器。

- Infrastructure JPEG round-trip Strategy；
- 显式算子登记与重复/缺失 Kind 启动测试；
- 链顺序、禁用步骤、失败短路、前缀回调和中间资源释放；
- 当前案例从基线独立开始的测试；
- JPEG 质量 1/50/95/100、Alpha 阻断、编码字节数和取消测试。

门禁：JPEG 使用正式 codec；无中间磁盘文件；链次序可观察；算子不能修改输入。

### G5：水印诊断

目标：在不复制协议的前提下提供受控实验所需原始事实。

- 从现有 Carrier 抽出共享的只读物理判决路径；
- 返回 Header/Data 每副本错误、投票后字节、分层置信度；
- 计算 PhysicalRawBer 与 VotedPreEccBer；
- 分开 Header/Data RS 修复，并保持既有 `ExtractionReport` 兼容；
- Header 解码失败、槽位不足、错误密码、超纠错和完整性失败分类；
- 敏感缓冲区清零和报告泄漏测试。

门禁：现有水印 97 项回归不放宽；BER 用人工翻转 bit 的 Golden Vector 精确验证；实验室不拥有第二套提取算法。

### G6：实验用例与 Session

目标：完成端到端无 UI 扫描闭环。

- 基线准备、容量与未扰动回读；
- Profile × 点 × trial 串行执行；
- 分步探针、第一次失败、失败后恢复和主原因选择；
- 两组质量、局部 16×16 网格、曲线与矩阵聚合；
- Session 所有权、按需案例重放和有界投影；
- 进度、取消、不完整结果和异常隔离。

门禁：固定合成图与小 Payload 的已知扫描结果稳定；N/A 不混入 0%；取消不返回半个案例；内存不随案例数线性增长。

### G7：Document、快照与导出

目标：把无 UI 用例接入真实持久 Document 生命周期。

- `PluginIds.RobustnessLabDocument` 与 scoped Document；
- 路径、Payload、密码、Profile、链、扫描和结果失效规则；
- generation、取消、迟到结果、关闭释放和多 Scope 隔离；
- schema 1 快照恢复，不自动运行、不保存敏感内容；
- JSON/CSV Serializer、窄文件对话框和原子输出；
- Standalone 真实 Module 复用。

门禁：两个实例互不影响；快照零敏感数据；非法旧配方安全阻断；导出失败保留 Session。

### G8：界面、曲线与解释

目标：让参数扫描和失败原因可见、可操作、可访问。

- 输入与 Profile 区、扰动链编辑器、扫描预检与运行区；
- 成功率曲线、Profile 矩阵、分步失败和案例详情；
- 基线/最终/差异/局部网格预览；
- 键盘选择、文字图例、等价表格、N/A 和未完成语义；
- 参数错误、容量、尺寸、JPEG Alpha 和敏感信息提示；
- Headless View 与控件加载、绑定、坐标和选择测试。

门禁：第五个真实 View 可加载；不靠颜色独占信息；View/Control 不执行算法或文件访问。

### G9：本地集成与开发封板

目标：完成本地开发证据与文档，不冒充发布完成。

- Module 固定贡献五个 Persistable Document、零 Tool；
- 既有四个 Document、协议、比较、频域和水印全量回归；
- Debug/Release、locked restore、warn-as-error 门禁；
- 运行代表性单算子、组合链、随机扫描和三 Profile 矩阵；
- 同步所有 README、未来能力状态、公共边界、用户指南和测试文档；
- 填写 G0–G9 实施记录和实际测试总数。

门禁：本地自动证据完整、专用文档齐全、无 AIFLOW/Windows CI/发布配置；人工 Standalone 场景明确记录为人工证据或延期。

## 17. 预计代码与文档落点

### 17.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Domain/Robustness/
│  ├─ RobustnessRecipe.cs
│  ├─ RobustnessScan.cs
│  ├─ RobustnessResults.cs
│  ├─ DeterministicTrialRandom.cs
│  └─ Operators/
│     ├─ NoiseOperators.cs
│     ├─ ColorOperators.cs
│     ├─ FilterOperators.cs
│     └─ GeometryOperators.cs
├─ Application/Robustness/
│  ├─ RobustnessContracts.cs
│  ├─ RobustnessBaselineUseCase.cs
│  ├─ RobustnessExperimentUseCase.cs
│  ├─ PerturbationChainExecutor.cs
│  ├─ RobustnessProjectionUseCase.cs
│  └─ RobustnessReportExportUseCase.cs
├─ Infrastructure/Robustness/
│  ├─ JpegReencodeOperator.cs
│  ├─ WatermarkDiagnosticReader.cs
│  └─ RobustnessReportSerializer.cs
└─ Features/RobustnessLab/
   ├─ RobustnessLabDocument.cs
   ├─ RobustnessLabView.axaml
   ├─ RobustnessLabView.axaml.cs
   ├─ RobustnessCurveControl.cs
   └─ RobustnessMatrixControl.cs
```

文件名可以在实现时因职责进一步拆小，但不得把所有算子、所有 DTO 或整个 Document 塞入单一巨型文件。

### 17.2 测试

```text
tests/ImageLabPlugin.Tests/
├─ RobustnessRecipeTests.cs
├─ RobustnessRandomnessTests.cs
├─ RobustnessNoiseAndColorTests.cs
├─ RobustnessFilterAndGeometryTests.cs
├─ RobustnessJpegAndChainTests.cs
├─ WatermarkDiagnosticTests.cs
├─ RobustnessExperimentUseCaseTests.cs
├─ RobustnessReportTests.cs
├─ RobustnessLabDocumentTests.cs
└─ RobustnessLabViewTests.cs
```

### 17.3 专用文档

实现期间必须新增或同步：

- `docs/design/robustness-lab/README.md`：能力入口与阅读顺序；
- `docs/design/robustness-lab/guide.md`：用户操作、指标、失败解释和限制；
- `docs/design/robustness-lab/user-manual.md`：从受控实验基础概念开始的新手说明；
- `docs/design/robustness-lab/mathematical-principles.md`：扫描、概率、BER、纠错和质量指标背景；
- `docs/design/robustness-lab/testing.md`：自动测试、本地命令、实际数量和未执行门禁；
- `docs/design/robustness-lab/report-schema.md`：JSON/CSV schema、版本和隐私边界；
- `docs/design/shared/image-domain-boundaries.md`：补充扰动、诊断与比较职责；
- `docs/future-capabilities.md`：把本能力从候选更新为实际阶段，并保留 V2 边界；
- `docs/README.md` 与根 `README.md`：新增第五个 Document 和文档入口；
- `docs/design/robustness-lab/history/README.md` 及 G0–G9 实施记录。

“同步文档”是每个 G 包的完成条件，不允许到 G9 才补写所有说明。用户指南和测试文档必须写实际行为，不能复制本计划中的
未来时描述。

## 18. 单元测试、自动测试与质量门禁

### 18.1 SOLID 与结构

- Domain 项目命名空间不引用 Avalonia、JSON、文件、DI 或 Infrastructure；
- 每个算子单责，链执行器不包含具体数学公式；
- JPEG、诊断、文件对话框和原子写入使用窄真实端口；
- Document 不出现完整像素循环、DCT、RS、BER 或 JSON 逻辑；
- Module 明确登记五个 Persistable Document、零 Tool、零 Workflow Action；
- 无 AIFLOW、反射注册、Service Locator、运行时脚本或通用 DAG。

### 18.2 算子数值

- 所有恒等参数逐字节不变，源图不变，Alpha 规则固定；
- 高斯噪声固定种子 Golden Vector，以及大样本均值/标准差宽容检查；
- 椒盐比例、像素不重复策略和盐椒计数边界；
- 颜色公式、裁切、舍入和极值；
- 高斯核归一化、可分离结果、中值奇数核和锐化极值；
- 缩放、裁剪、补边、平移、旋转和透视的小矩阵坐标基准；
- 尺寸乘法溢出、16M 像素上限、非法矩阵和取消；
- JPEG 真实编码—解码、质量边界和 Alpha 阻断。

### 18.3 随机与计划

- 同种子同案例逐字节一致；
- 案例执行顺序变化不改变各案例输出；
- 不同 trial 子种子不同；
- 范围展开无额外浮点端点；
- Profile/点/trial 排序和键唯一；
- 300 案例、1,200 观察和 checked 溢出门禁；
- 未知/重复步骤和扫描目标失效安全失败。

### 18.4 水印诊断

- 无损基线两类 BER 为 0、RS 修复为 0；
- 人工翻转物理副本只影响 Physical BER，投票可能仍为 0；
- 翻转投票结果时 Voted BER 精确匹配错误 bit 数；
- Header/Data RS 修复分开且合计兼容旧报告；
- Header 失败仍能给出可获得的原始控制信道事实；
- 槽位不足、错误密码、超纠错和完整性失败分类；
- Payload、密码、Mapping Key 与原始 Frame 不进入报告和快照。

### 18.5 用例、聚合与生命周期

- 基线不成功时阻止扫描，容量不足不自动降级；
- 每案例从相同基线开始，链顺序和禁用步骤正确；
- 首次失败、失败后恢复与关闭分步探针语义；
- N/A 不计作失败，取消 trial 不进入成功率分母；
- `n=1`、多 trial、失败原因计数和分位数；
- 同尺寸两组质量与 16×16 局部网格，尺寸不同时结构化 N/A；
- Session Dispose、按需重放、迟到结果、两个 Scope 隔离；
- 快照恢复不自动运行且无敏感数据；
- JSON/CSV schema、非有限数、UTF-8、原子写入和路径隐私；
- 第五个 Headless View、曲线和矩阵控件加载与键盘选择。

### 18.6 回归与资源

- 现有 97 项作为起始回归基线，最终记录实际总数而不是预估数；
- 不放宽水印、频域或图像比较既有数值阈值；
- 最大案例测试使用小图片证明结果内存不随案例数保存完整图；
- 大图结构测试证明长期完整图数量受控；
- 热循环不使用逐像素 LINQ、逐像素对象或无界并行；
- 性能测试使用宽松结构门禁，不使用机器相关的严格毫秒断言；
- 所有注释与设计说明不替代可执行测试。

### 18.7 本地回归命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

本地 Release 只表示另一编译配置的开发回归，不代表发布。当前禁止把 Windows CI、ZIP、真实 Host 或安装测试加入
G9 完成条件；准备发布时再按 `docs/design/shared/deployment-and-release.md` 单独启用。

## 19. 人工验收场景

### 19.1 基线与单算子

1. 打开两个鲁棒性实验室实例，分别选择不同载体；
2. 建立三 Profile 基线，确认容量和未扰动回读可见；
3. 扫描 JPEG 100→50，查看曲线、矩阵和案例详情；
4. 扫描高斯噪声并设置 5 次重复，确认点上显示 `n=5`；
5. 检查相同种子重跑结果一致。

### 19.2 组合顺序与失败解释

1. 建立“JPEG → 噪声 → 亮度”和反向顺序的两份配方；
2. 确认配方摘要、结果和报告哈希区分顺序；
3. 添加 1 像素平移，观察块同步相关说明；
4. 检查首次失败步骤与前缀详情；
5. 关闭分步探针后确认 UI 明确显示 `NotMeasured`。

### 19.3 几何、质量与 N/A

1. 执行缩放、裁剪和补边，确认输出尺寸可见；
2. 确认尺寸变化后 PSNR/SSIM 显示 N/A 与原因，而不是 0；
3. 执行固定画布旋转和平移，确认指标说明包含几何错位；
4. 选择同尺寸颜色扰动，查看全局指标和 16×16 局部网格；
5. 检查基线、最终图和差异预览切换不改变实验结果。

### 19.4 取消、恢复与导出

1. 运行多案例实验中途取消，确认结果标记不完整；
2. 修改种子或步骤，确认旧结果过期；
3. 保存布局、关闭并恢复，确认配方恢复但密码、内联 Payload 和结果为空；
4. 导出 JSON/CSV，检查无绝对路径、密码、Payload 或密钥；
5. 模拟导出失败，确认 Session 仍可查看和重试。

### 19.5 Standalone 边界

Standalone 只做本地布局、命令、图表交互和资源释放检查。它不能替代真实 Host、ZIP、Windows CI 或发布验收。
本轮若未执行完整人工场景，G9 记录必须明确写“延期”，不能用 Headless 自动测试冒充人工证据。

## 20. 兼容、迁移与回滚

### 20.1 兼容规则

- 不改变水印 V1 Magic、Header、Profile 参数、DCT 系数、Mapping 或正式读取结果；
- 新诊断接口只暴露既有读取过程的额外观察，不参与正式协议判定；
- 既有 `ExtractionReport` 字段和语义保持兼容；新分层修复与 BER 使用新结果类型；
- 配方 schema 和报告 schema 分开版本化，不能用插件版本代替数据版本；
- 算子公式、PRNG、插值和舍入属于复现实验的兼容事实，改变时必须升版本；
- Document ID 一旦进入可恢复布局就不得改名。

### 20.2 回滚顺序

1. 可先隐藏 Module 中第五个 Document 贡献，同时保留类型供旧布局安全识别；
2. 再移除 Feature UI 与专用应用用例注册；
3. 再移除算子和报告实现；
4. 最后评估是否移除水印诊断扩展，但不得破坏既有正式提取路径；
5. 已导出的 JSON/CSV 是用户文件，回滚不能删除或覆盖。

### 20.3 无破坏迁移

V1 首次实现没有旧配方数据库需要迁移。开发期 schema 改动仍应通过显式版本分支处理测试快照；禁止通过捕获所有异常后
返回空配方来“兼容”。未知字段可忽略，未知算子和改变实验语义的字段必须可见阻断。

## 21. 中文注释与实施纪律

- 新增公开或跨层内部契约、复杂数学、资源所有权和失败分类必须使用详细中文 XML 注释；
- 注释优先解释“为什么这样定义”“单位与边界是什么”“谁拥有并释放”，不要逐行翻译代码；
- 高斯、Gamma、双线性、单应矩阵、BER、分位数和子种子派生处必须写公式、舍入与数值风险；
- 任何看似反常的行为都要说明，例如几何变化后不自动对齐、尺寸不同时质量为 N/A；
- 安全相关注释必须说明密码学随机源与实验随机源不可混用；
- 复杂循环先用中文概述数据流和内存边界，再保持代码本身直接；
- 每个 G 包开始前先读本文与上一包记录，完成后立即写实际记录；
- 不通过降低断言、增加重试、扩大误差或跳过测试来封板；
- 不使用 AIFLOW，不新增 Windows CI 或发布门禁；
- 设计模式只在真实替换点使用，禁止为了“模式数量”引入额外层次。

建议在关键类型采用如下风格：

```csharp
/// <summary>
/// 按稳定案例事实派生噪声步骤的确定性子种子。
/// </summary>
/// <remarks>
/// 这里不能消费水印协议使用的密码学随机源，也不能依赖案例执行顺序。
/// 否则取消、重排或新增一个扫描点会改变其他案例的噪声样本，使两次报告失去可比性。
/// </remarks>
internal static ulong DeriveTrialSeed(...)
```

## 22. V1 开发封板检查清单

### 产品与实验语义

- [x] 受控基线未扰动时完整回读；
- [x] 一个参数轴、Profile 和 trial 维度清晰；
- [x] 所有候选算子有固定参数、单位、恒等值和边界；
- [x] 链顺序、分步探针和第一次失败语义可见；
- [x] JPEG 只作为有损信道，不提供格式转换产品入口；
- [x] N/A、失败、取消和未测量不会混淆。

### SOLID 与生命周期

- [x] Domain、Application、Feature、Infrastructure 依赖方向正确；
- [x] 算子单责，Strategy 显式登记且没有反射/工厂堆叠；
- [x] Document 不执行算法、编解码、BER 或文件写入；
- [x] 两个 Scope、取消、generation、关闭和迟到结果安全；
- [x] Session 内存受控，敏感缓冲区及时清零；
- [x] 第五个 Persistable Document 身份和快照稳定。

### 数值、诊断与资源

- [x] 随机派生可复现且不影响密码学随机性；
- [x] Physical BER、Voted BER、Header/Data RS 和置信度有 Golden Vector；
- [x] Profile 基线与正式提取器完全复用；
- [x] 两组质量与 16×16 局部网格语义准确；
- [x] 300 案例、1,200 分步观察、16M 像素上限在运行前验证；
- [x] 串行执行和中间图片释放有结构证据。

### 测试与文档

- [x] 现有 97 项起始回归不放宽，最终实际测试数已记录；
- [x] 算子、BER、聚合、Document、报告与 UI 的单元测试门禁齐全；
- [x] Debug/Release、locked restore 和 warn-as-error 本地门禁通过；
- [x] JSON/CSV、快照、隐私、Headless View 和组合根测试齐全；
- [x] 根 README、docs 索引、未来能力和公共边界已同步；
- [x] 用户指南、测试门禁、报告 schema 与 G0–G9 记录齐全；
- [x] 无 AIFLOW、Workflow Action、Workbench Command；
- [x] 无 Windows CI、ZIP、真实 Host 或发布完成声明。

只有以上开发项有实际证据后，状态才可写为“开发实现与本地自动门禁完成”。这仍不等于已经发布；发布阶段必须另行
执行真实 Host、正式包、目标设备和授权语料门禁。
