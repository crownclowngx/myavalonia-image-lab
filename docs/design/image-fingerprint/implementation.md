# ImageLabPlugin V1 感知指纹实施计划

> 计划状态：待实施；本文只完成设计与本地基线确认，不表示功能已经进入产品<br>
> 基线日期：2026-08-30<br>
> 产品名称：Image Fingerprint／感知指纹<br>
> 技术基线：.NET 10、C# 14、Avalonia 12、Managed Plugin SDK 3.3<br>
> 起始自动基线：Debug 122/122 通过、零跳过<br>
> 核心路线：统一视觉归一化 + aHash + dHash + pHash + 64 位规范摘要 + 汉明距离 + 显式双图比较 + 受控稳定性试验 + Robustness Lab 观测入口<br>
> 实施原则：SOLID 是首要规定；设计模式只用于真实替换点；中文注释详细解释算法、边界和所有权；先冻结数值语义，再实现领域算法、用例、Document 与界面

| 实施包 | 当前状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 待实施 | 冻结产品范围、算法版本、位序、阈值校准、资源和隐私语义 | `history/g0-product-and-numeric-baseline.md` |
| G1 | 待实施 | 建立视觉归一化、64 位指纹值对象和汉明距离基础 | `history/g1-normalization-and-fingerprint-core.md` |
| G2 | 待实施 | 完成 aHash 与 dHash，并建立独立 Golden Vector | `history/g2-ahash-and-dhash.md` |
| G3 | 待实施 | 完成标准化 pHash 低频 DCT，并证明既有 DCT/水印零回归 | `history/g3-phash-and-dct-reuse.md` |
| G4 | 待实施 | 完成双图比较、版本化结论策略、Session 和报告 | `history/g4-pair-comparison-and-report.md` |
| G5 | 待实施 | 完成 Persistable Document、取消、快照和资源生命周期 | `history/g5-document-lifecycle.md` |
| G6 | 待实施 | 完成位图、并排结果、限制说明和无障碍交互 | `history/g6-ui-and-explanation.md` |
| G7 | 待实施 | 完成四类受控稳定性试验与 Robustness Lab 指纹观测入口 | `history/g7-stability-and-robustness-observer.md` |
| G8 | 待实施 | 完成本地双配置门禁、专用文档与开发阶段封板 | `history/g8-local-sealing.md` |

本文定义 ImageLab 的第六个 Persistable Document。它判断两张图片在经历缩放、重编码或轻微颜色变化后，是否仍表现出相近的视觉指纹；它比较的是经过明确算法归一化后的图像内容，不比较文件字节、文件名、EXIF、编码器标记或目录关系。

感知指纹只能提供启发式相似线索，不能证明版权归属、原始来源、编辑历史或法律意义上的同一性。V1 不扫描目录、不自动寻找重复文件、不建立图库索引，也不把相似度百分比描述为概率。

本文是实施阶段的唯一总计划。每个 G 包完成后，必须新增对应历史记录并填写实际代码、自动测试、数值证据、偏差、风险和回滚方式。计划中的测试数不能提前写成已通过；当前阶段只执行本地开发门禁，不新增 Windows CI，不执行 ZIP、真实 Host、安装/卸载或发布封板。

## 1. V1 目标与固定用户闭环

### 1.1 用户闭环

```text
显式选择参考图 A 和待判断图 B
    ↓
顺序解码，并显示文件名、尺寸、格式来源和受控预览
    ↓
按同一视觉归一化规则分别计算 aHash、dHash 和 pHash
    ↓
为每种算法显示 8×8 位图、16 位十六进制摘要和算法版本
    ↓
按相同算法计算 XOR、汉明距离和归一化相似度
    ↓
并排显示每种算法的结论、阈值策略名称和适用限制
    ↓
显示“一致接近 / 结果分歧 / 一致不接近”，不生成来源概率
    ↓
可选：从 A 生成受控缩放、JPEG、亮度或轻度裁剪样本
    ↓
查看各算法在扰动强度变化下的距离曲线与越界位置
    ↓
复制摘要或原子导出版本化 JSON，供人工复核
```

### 1.2 固定实施顺序

1. G0 先冻结每个算法的输入尺寸、灰度、Alpha、插值、位序、阈值和报告措辞；
2. G1 再建立不依赖 UI、文件系统和 JSON 的归一化及 64 位值对象；
3. G2 独立实现 aHash、dHash，使用手算和外部独立生成的 Golden Vector 交叉验证；
4. G3 以 `32×32 → 低频 8×8` 的明确版本实现 pHash，并先保护现有 DCT 与水印回归；
5. G4 用窄应用用例组织双图计算、结论和报告，Document 不执行逐像素或 DCT 循环；
6. G5 让 scoped Document 管理两条路径、Session、取消、generation、Revision 和 Bitmap；
7. G6 最后接入指纹位图、结果表、限制说明和键盘可访问交互；
8. G7 复用既有扰动能力完成固定稳定性试验，并以窄观测入口接入 Robustness Lab；
9. G8 执行 locked restore、Debug/Release warn-as-error build/test，补齐专用文档，不执行发布门禁。

### 1.3 V1 决策摘要

| 主题 | V1 决策 |
| --- | --- |
| 输入数量 | 正常比较固定为两张用户显式选择的图片 |
| 指纹宽度 | aHash、dHash、pHash 均输出 64 位 |
| 摘要格式 | 16 个大写十六进制字符，不带 `0x` |
| 位序 | 8×8 行优先；左上为 bit 63，右下为 bit 0；十六进制按高位到低位输出 |
| 距离 | 只比较相同 AlgorithmId；`PopCount(A XOR B)`，范围 0–64 |
| 百分比 | `100 × (64 - distance) / 64`，名称为“位相似度”，明确不是来源概率 |
| 结论 | 每算法独立给出；总览只总结一致、分歧或一致不接近 |
| Alpha | 先以固定白底进行非预乘视觉合成，再进入亮度归一化 |
| aHash | 8×8 亮度均值阈值 |
| dHash | 9×8 水平相邻差异，输出 8×8 共 64 位 |
| pHash | 32×32 亮度 DCT-II，取左上 8×8，AC 中位数为阈值，输出 64 位 |
| 稳定性 | 只使用固定的缩放、JPEG、亮度和轻度中心裁剪单轴试验 |
| wHash | 不进入 V1；等待可复用 DWT 基础通过独立门禁后再加入 |
| 文件范围 | 不扫描目录、不遍历图库、不做重复文件管理 |
| 集成形态 | 第六个 Persistable Document；不是 Tool，不使用 AIFLOW |

## 2. 当前工程基线与缺口

### 2.1 已有事实

当前仓库已经具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集，以及复用同一个 Module 和 DI 入口的 Standalone；
- 水印写入、提取与验证、频域分析器、图像比较实验室、鲁棒性实验室五个 Persistable Document；
- `PixelImage`、`ImageSize`、16,000,000 像素上限和 64 MiB 编码输入上限；
- BT.601 全范围 Y/Cb/Cr、抗混叠分析代理、PNG/JPEG 正式编解码和原子写入；
- 固定 8×8 正交 DCT-II/IDCT、全局 FFT、比较指标和既有扰动算子；
- scoped Document、generation、取消、迟到结果保护、轻量快照与 Bitmap 释放惯例；
- xUnit、Avalonia Headless、组合根与持久化自动测试；
- 2026-08-30 实际复跑的 Debug 122/122 通过、零跳过基线；
- 当前明确不使用 AIFLOW，且发布门禁与 Windows CI 延期。

### 2.2 可直接复用

- `IImageCodec`：顺序解码用户显式选择的两张图片；
- `PixelImage` / `ImageSize`：明确拥有 RGBA8888 缓冲区与安全尺寸；
- `ColorSpaceConverter` 已冻结的 BT.601 系数：作为不含 Alpha 时的亮度公式事实；
- `Dct8x8Transform` 的正交 DCT-II 约定、余弦基和归一化方向：作为 pHash 新低频变换的数值基线；
- `ImageAnalysisProxyProjector` / `ImagePreviewProjector` 的有界预览思想：用于 UI 预览，不作为指纹输入；
- 鲁棒性实验室中已经验证的缩放、亮度、裁剪与 JPEG 往返能力：用于 G7 固定试验适配；
- `IAtomicFileWriter`、`ITextClipboard` 和意图隔离的文件对话框模式；
- `IDocumentLifetime`、Scope、快照、取消和 Headless View 测试惯例。

### 2.3 当前缺口

- 没有视觉指纹领域、算法稳定身份、64 位规范格式或汉明距离模型；
- 没有处理透明像素隐藏 RGB 的固定视觉归一化规则；
- 现有抗混叠代理只接受 512/1024/2048 最大边，不能替代 8×8、9×8、32×32 的算法输入；
- 现有 `Dct8x8Transform` 不能直接计算标准 pHash 所需的 32×32 输入低频系数；
- 没有阈值校准数据、算法分歧语义、局限说明或版本化报告；
- 没有双图指纹 Session、指纹位图投影或稳定性距离曲线；
- Robustness Lab 只观察水印和质量结果，尚无指纹观测入口；
- Module、Standalone、组合根和快照只认识五个 Document。

### 2.4 主工程约束

- Plugin Module 继续作为贡献登记的唯一事实源；
- 感知指纹登记为第六个 Persistable Document，不登记 singleton Tool；
- Domain 不依赖 Avalonia、文件路径、JSON、DI、JPEG 编码器或 Robustness Lab；
- Application 只能通过窄端口协调解码、算法、试验与导出；
- Infrastructure 适配图片编解码、JPEG 往返、既有扰动算子和文件写入；
- Feature 只管理实例状态、命令、Bitmap、取消、Revision 与展示；
- 不复制现有 DCT 数学、图片编解码、扰动公式或原子写入；
- 原则上不增加第三方 NuGet；不引入图像哈希库、反射插件算法发现或脚本运行时；
- 不使用 AIFLOW，不登记 Workflow Action 或 Workbench Command；
- 不新增 Windows CI，不执行 ZIP、真实 Host 或发布阶段门禁。

## 3. 产品范围与非目标

### 3.1 V1 必须完成

- 两张显式图片的 aHash、dHash 和 pHash；
- 每个算法的稳定 AlgorithmId、64 位值、16 位十六进制摘要和 8×8 位图；
- 同算法汉明距离、位相似度、独立结论和限制说明；
- 对三种算法结论的可解释汇总，不输出虚假的综合概率；
- 图片尺寸、算法输入规格、Alpha 规则和处理耗时的可见说明；
- 缩放、JPEG、亮度、轻度中心裁剪四类固定单轴稳定性试验；
- 将 aHash/dHash/pHash 作为 Robustness Lab 可选观测值，并保持默认实验行为不变；
- 取消、迟到结果拒绝、快照恢复、两个 Scope 隔离和 Session 释放；
- JSON 摘要、剪贴板文本、文件名隐私和原子输出；
- 完整本地单元测试、集成测试、Headless View 门禁及专用中文文档。

### 3.2 明确不实现

- 目录扫描、图库遍历、重复文件发现、自动清理或文件管理；
- 后台监视目录、数据库索引、近似最近邻检索或海量指纹库；
- 文件 SHA/MD5、EXIF、文件名、路径或编码字节相同判断；
- 图片自动下载、剪贴板历史扫描或处理未由用户明确选择的内容；
- 人脸、Logo、物体识别、来源网站搜索或反向图片搜索；
- 旋转不变、镜像不变、任意裁剪不变或透视不变的绝对承诺；
- 把位相似度称为“同源概率”“置信概率”或证据结论；
- 自动学习阈值、远程模型、AI 分类、AIFLOW 或通用工作流；
- V1 中的 wHash、DWT、水印 DWT 或 Wavelet Lab；
- Windows CI、正式 ZIP、真实 Host、安装/卸载和发布封板。

### 3.3 为什么 V1 固定两张输入

两张图片足以形成“参考—候选—逐算法距离—人工复核”的完整闭环，也与现有图像比较实验室的状态和生命周期模型一致。三到八张图片会引入矩阵选择、排序、重复路径、两两比较数量和结果持久化问题；这些问题不是哈希算法本身的必要条件。

后续若确有需求，可新增“最多 8 张显式图片”的小集合矩阵，但仍不得演变为目录扫描。V1 不预留未使用的集合接口或复杂矩阵模型。

## 4. Document 形态与状态所有权

### 4.1 贡献形态

| 字段 | 固定值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.image-fingerprint` |
| 显示名称 | `感知指纹` |
| 描述 | `使用 aHash、dHash 和 pHash 比较两张显式图片的感知相似性与稳定性` |
| 分类 | `图像分析` |
| Host 注册 | `AddPersistableDocument<ImageFingerprintDocument, ImageFingerprintView>` |
| 实例基数 | 多实例；每实例独立路径、Session、试验、取消令牌和结果 |

选择 Persistable Document 而不是 Tool 的原因：

- 两张图片、算法结论、稳定性配方和当前选中结果构成独立工作上下文；
- 用户可能同时比较多组图片，singleton Tool 会错误共享路径和结果；
- 计算与试验需要明确取消、关闭和大对象释放边界；
- 路径和轻量交互参数适合恢复，但图片像素和派生指纹应重新显式计算；
- 该能力是主工作内容，不是依附其他文档的全局辅助面板。

### 4.2 持久状态

- 参考图路径、候选图路径；
- 当前选中的算法结果行；
- 是否显示指纹位图、归一化预览和限制说明；
- 结果表排序只允许固定的算法顺序或距离顺序；
- 稳定性试验类型、强度列表和当前选中样本；
- 面板折叠、曲线尺度和说明区域状态。

### 4.3 运行时派生状态

- 两张完整 `PixelImage`，仅在当前 Session 中持有；
- 两张最大边 1024 的显示代理；
- 三种算法的归一化亮度小矩阵、64 位指纹和比较结果；
- 指纹 8×8 位图、归一化预览 Bitmap 和当前稳定性曲线；
- 稳定性试验的当前样本预览；
- 进度、取消源、generation、错误、耗时与资源统计。

### 4.4 快照与恢复

- 快照 schema 从 1 开始，只保存路径和轻量 UI/试验参数；
- 不保存图片像素、归一化矩阵、指纹、报告、曲线或 Bitmap；
- 恢复后显示“路径已恢复，请重新计算”，不得自动读取磁盘；
- 空路径、非法枚举、过长强度列表和越界面板值必须回退到安全默认；
- 修改任一路径立即取消当前计算、释放 Session、清空报告并使导出失效；
- Pointer 悬停和临时 tooltip 不推进 Dirty Revision。

## 5. SOLID 架构与依赖方向

### 5.1 分层

```text
Features/ImageFingerprint
  ImageFingerprintDocument       路径、命令、状态、Revision、取消和生命周期
  ImageFingerprintView           布局、绑定、帮助和无障碍文本
  FingerprintBitmapControl       只绘制 8×8 位图并转发选择
  FingerprintStabilityControl    只绘制距离曲线并转发 Pointer
                 │
                 ▼
Application/Fingerprinting
  IPrepareFingerprintComparisonUseCase
  IRunFingerprintStabilityUseCase
  IExportFingerprintReportUseCase
  FingerprintComparisonSession   Document 私有大对象所有者
                 │
          ┌──────┴──────────┐
          ▼                 ▼
Domain/Fingerprinting   Domain/Imaging + Domain/Frequency
  值对象、算法、距离     PixelImage、亮度事实、DCT 共享数值基元
  结论策略、稳定性结果
          ▲
          │
Infrastructure
  Avalonia 图片编解码、JSON、文件对话框、原子写入
  固定稳定性信道对既有扰动/JPEG 能力的适配
```

依赖只允许由外向内。`Domain/Fingerprinting` 不知道文件路径、图片格式、Avalonia、JSON、DI、Document 或鲁棒性配方；`Application/Fingerprinting` 不知道 Bitmap；Document 不直接 new 算法、文件流、编码器或 `ServiceProvider`。

### 5.2 单一职责

- `FingerprintLumaNormalizer`：只把 `PixelImage` 转成指定尺寸的确定性视觉亮度矩阵；
- `AverageHashAlgorithm`：只实现 aHash；
- `DifferenceHashAlgorithm`：只实现 dHash；
- `PerceptualHashAlgorithm`：只编排 32×32 归一化、低频 DCT 和阈值位；
- `LowFrequencyDctTransform`：只计算指定左上低频系数，不知道哈希或 UI；
- `ImageFingerprint`：只拥有算法身份和 64 位值；
- `FingerprintDistanceCalculator`：只验证算法身份并计算距离；
- `FingerprintDecisionPolicy`：只把距离映射为版本化描述，不重新计算哈希；
- `PrepareFingerprintComparisonUseCase`：只协调解码、算法、预览和 Session；
- `FingerprintReportSerializer`：只序列化稳定 DTO；
- Document 只管理当前实例，View/Control 只展示和转发交互。

禁止建立万能 `ImageFingerprintService`、算法抽象工厂、反射扫描、事件总线、命令总线、Visitor、Decorator 链或通用 DAG。

### 5.3 开闭原则与朴素 Strategy

aHash、dHash 和 pHash 确实共享同一种“输入图片、输出指纹”的替换点，因此只允许一个小型 Strategy：

```csharp
internal interface IImageFingerprintAlgorithm
{
    FingerprintAlgorithmId Id { get; }

    ImageFingerprint Compute(
        PixelImage source,
        CancellationToken cancellationToken = default);
}
```

约束：

- 三个实现由组合根显式按固定顺序登记，禁止反射扫描；
- AlgorithmId 唯一，组合测试检查重复、缺失和顺序；
- 算法参数不是运行时任意字典，不建立通用参数编辑器；
- 每个算法类自己声明固定输入规格，但共同复用亮度归一化器；
- 新算法必须先有稳定 ID、数学说明、Golden Vector、阈值校准和局限文案；
- 不为三个 Strategy 再叠加 Factory、Provider、Resolver 或 Builder。

### 5.4 接口隔离

建议新增三个应用接口和一个基础设施端口：

```csharp
internal interface IPrepareFingerprintComparisonUseCase
{
    Task<FingerprintComparisonSession> ExecuteAsync(
        FingerprintComparisonRequest request,
        CancellationToken cancellationToken);
}

internal interface IRunFingerprintStabilityUseCase
{
    Task<FingerprintStabilityResult> ExecuteAsync(
        FingerprintComparisonSession baseline,
        FingerprintStabilityRecipe recipe,
        IProgress<FingerprintProgress>? progress,
        CancellationToken cancellationToken);
}

internal interface IExportFingerprintReportUseCase
{
    string CreateJson(FingerprintReport report);
    string CreateHumanReadableText(FingerprintReport report);
    Task ExecuteAsync(FingerprintReport report, string path, CancellationToken cancellationToken);
}

internal interface IFingerprintReportFileDialog
{
    Task<string?> PickJsonOutputAsync(string suggestedName, CancellationToken cancellationToken);
}
```

图片选择继续复用 `IImageFileDialog`，文本复制继续复用 `ITextClipboard`，写入继续复用 `IAtomicFileWriter`。纯数学类不为了 Mock 而机械增加接口。

## 6. 统一视觉归一化

### 6.1 输入与颜色规则

所有算法先读取同一个 `PixelImage`，但各自请求固定目标尺寸。每个源像素按以下顺序形成视觉亮度：

1. RGBA 视为未预乘 8 位通道；
2. 使用固定白底 `(255,255,255)` 合成，避免完全透明像素的隐藏 RGB 改变指纹；
3. 合成按线性数值公式逐通道执行，并在进入亮度前保留 `double`；
4. 使用项目既有 BT.601 全范围亮度：`Y = 0.299R + 0.587G + 0.114B`；
5. 将亮度按覆盖面积确定性缩放到算法目标尺寸；
6. 中间值保留 `double`，只在展示归一化预览时裁切到 byte。

白底合成公式：

```text
a = A / 255
Cvisual = a × C + (1 - a) × 255
```

选择白底是算法版本的一部分，不提供 UI 开关。若允许用户选择底色，同一图片会产生多个不可直接比较的摘要，且快照与报告必须额外携带底色；V1 不引入这种歧义。

### 6.2 缩放规则

- aHash：直接归一化为 8×8；
- dHash：直接归一化为 9×8；
- pHash：直接归一化为 32×32；
- 采用按目标像素覆盖源像素面积加权的确定性缩小；
- 小图放大时采用像素中心对齐的双线性插值，不能把“面积缩小”错误用于无覆盖面积的放大；
- 算法定义允许非等比拉伸，不补边、不裁剪；这与常见感知哈希的固定矩阵输入一致；
- UI 预览代理不能作为哈希输入，哈希必须始终从完整解码图片归一化；
- 循环按目标行检查取消，不在逐像素热路径中使用 LINQ 或对象分配。

归一化器必须显式记录 `fingerprint-luma-bt601-white-matte-area-bilinear-v1`。改变白底、颜色公式、像素中心、插值、舍入或拉伸规则都必须升算法版本，不能静默改变同一 AlgorithmId 的输出。

### 6.3 Alpha 与异常输入门禁

- 两张视觉相同、仅透明区域隐藏 RGB 不同的图片应得到相同指纹；
- Alpha=255 时结果与直接 BT.601 一致；
- Alpha=0 时结果等价于纯白像素；
- 1×1 图片可以归一化，不因目标尺寸更大而越界；
- 16M 像素上限继续由 `PixelImage` / 编解码端口统一执行；
- 非法尺寸、缓冲区长度和取消使用现有失败模型，不吞异常后返回全零哈希。

## 7. 64 位指纹规范

### 7.1 值对象

```csharp
internal readonly record struct ImageFingerprint(
    FingerprintAlgorithmId AlgorithmId,
    ulong Bits)
{
    public string ToCanonicalHex();
}
```

值对象规则：

- `AlgorithmId` 不能为空或未知；
- `Bits` 是完整 64 位，不使用可变 `BitArray`；
- 位图行优先：`(x=0,y=0)` 对应 bit 63，`(x=7,y=7)` 对应 bit 0；
- 规范十六进制为 `X16`、大写、不带前缀，如 `0123456789ABCDEF`；
- 解析器只接受恰好 16 个十六进制字符，可接受大小写但输出统一大写；
- 不覆盖为文件哈希，不与 SHA、MD5 或水印摘要混用命名。

### 7.2 汉明距离

```text
distance = PopCount(bitsA XOR bitsB)
bitSimilarityPercent = 100 × (64 - distance) / 64
```

- 只有 AlgorithmId 完全相同才允许比较；
- 不同算法即使都是 64 位也必须返回结构化不兼容，不能计算伪距离；
- 距离使用整数 0–64；百分比只作可读换算，保留两位小数即可；
- 距离 0 表示该算法输出相同，不表示文件相同或一定同源；
- 距离 64 表示所有指纹位相反，不表示内容一定完全无关。

### 7.3 指纹位图

- 使用固定 8×8 网格；bit 1 为深色，bit 0 为浅色；
- 每格有可访问文本，如“第 2 行第 3 列：1”；
- 位图只是摘要的可视化，不能从缩放后的屏幕截图重新计算距离；
- `FingerprintBitmapProjector` 输出领域无关的 8×8 byte/布尔矩阵，Avalonia Control 只负责绘制；
- 高对比主题下仍应区分 0/1，并提供十六进制文本作为非视觉替代。

## 8. aHash 规范

### 8.1 算法身份

`ahash-8x8-mean64-luma-v1`

### 8.2 计算步骤

1. 按统一规则归一化为 8×8 亮度矩阵；
2. 使用 64 个 `double` 亮度值的算术平均；
3. 对每个位置按行优先判断 `value >= mean`；
4. 真写 1，假写 0，形成 64 位结果。

使用 `>=` 而不是 `>` 是协议事实。均匀图片的 64 位因此全部为 1；这不是错误，但说明 aHash 对平坦图片区分力弱，UI 必须显示该限制。

### 8.3 适用与限制

- 对统一缩放、轻度重编码和整体亮度平移通常较稳定；
- 只保留相对平均亮暗布局，无法区分许多结构不同但粗略分布相似的图片；
- 对裁剪、旋转、镜像、局部遮挡和边界变化敏感；
- 平坦图、极低对比图可能出现碰撞，不应单独作为最终结论。

## 9. dHash 规范

### 9.1 算法身份

`dhash-horizontal-9x8-64-luma-v1`

### 9.2 计算步骤

1. 按统一规则归一化为宽 9、高 8 的亮度矩阵；
2. 每行比较八组水平相邻值；
3. 固定判断 `left > right`；
4. 按 `y=0..7`、`x=0..7` 写入 8×8 共 64 位。

相等时写 0。禁止在后续优化中改成 `>=`，否则大面积平坦区的指纹会静默变化。

### 9.3 适用与限制

- 关注水平方向亮度梯度，对统一亮度偏移较稳定；
- 对缩放和轻度 JPEG 通常比像素比较稳定；
- 对垂直结构、镜像、旋转、裁剪和局部重排敏感；
- 水平 dHash 不等价于垂直 dHash，V1 不把两者混成 128 位或隐藏组合分数。

## 10. pHash 与现有 DCT 复用

### 10.1 算法身份

`phash-dct32-low8-median64-luma-v1`

### 10.2 计算步骤

1. 按统一规则归一化为 32×32 亮度矩阵；
2. 计算二维正交 DCT-II 的左上 8×8 系数，不需要保存完整 32×32 频率矩阵；
3. 从 63 个 AC 系数中排除 `(0,0)` DC，取确定性中位数；
4. 64 个低频系数都与该 AC 中位数比较，固定使用 `coefficient >= median`；
5. 按 `(v,u)` 行优先写 64 位，因此 DC 位通常为 1，但仍保留在摘要中。

N=32 的一维正交缩放：

```text
α(0) = 1 / √N
α(k) = √(2 / N), k > 0

F(u,v) = α(u) α(v) Σx Σy
         f(x,y)
         cos((2x+1)uπ/(2N))
         cos((2y+1)vπ/(2N))
```

pHash 输入不减 128；中位数只来自 AC，避免整体亮度对应的 DC 主导阈值。常量平移只改变 DC，63 个 AC 及其中位数不变。
现有 `Dct8x8Transform` 仍在自己的入口减 128，因为水印和频域检查已经冻结了该约定；共享的是余弦基与正交缩放，
不是强迫两个产品算法共享不同语义的输入中心化。

### 10.3 复用方式

现有 `Dct8x8Transform` 直接服务水印和频域检查，不能为了 pHash 改成语义不明确的“万能变换服务”。建议只抽取一个无状态 `OrthogonalDctBasis` 数值基元：

- 负责按尺寸生成余弦表与 `α(k)`；
- `Dct8x8Transform` 保持原有循环顺序、入口减 128、输出布局和异常语义；
- 新 `LowFrequencyDctTransform` 使用同一基元，但只计算 N×N 输入的左上 K×K；
- 不引入 FFT-DCT、第三方数学库、缓存注册中心或运行时尺寸反射；
- V1 只允许 `(N=32,K=8)` 的 pHash 调用，其他尺寸不作为公开产品参数。

如果抽取基元导致既有 8×8 DCT Golden Vector、频域测试、水印载体字节或提取结果发生变化，G3 必须失败并回滚；不能通过扩大误差或更新水印期望值来迁就重构。

### 10.4 中位数规则

63 个 AC 值排序后取索引 31 的值。数据量固定且很小，使用数组复制和 `Array.Sort` 比引入 QuickSelect 更清晰；这里不为微小性能收益增加复杂算法。

### 10.5 适用与限制

- pHash 观察低频结构，通常比 aHash/dHash 更能容忍缩放、JPEG 和轻度颜色变化；
- 它仍不是裁剪不变、旋转不变、镜像不变或几何配准算法；
- 低纹理和高度对称图片可能碰撞；
- 不保证与未采用同一归一化、位序、中位数和 DC 规则的第三方 pHash 互操作；
- UI 和报告必须显示完整 AlgorithmId，不能只写模糊的“pHash”。

## 11. 结论策略与阈值校准

### 11.1 不使用虚假综合分数

三种算法的位并不独立，简单平均三个百分比会制造精度假象。V1 固定：

- 每种算法单独显示距离、位相似度、结论和限制；
- 总览只显示“一致接近”“结果分歧”“一致不接近”；
- 不显示 0–100 的“同源总分”；
- 不输出“有 92% 概率来自同一图片”一类措辞。

### 11.2 版本化策略

`FingerprintDecisionPolicyId` 与算法 ID 分开版本化。每算法至少具有四种结果：

| 结果 | 含义 |
| --- | --- |
| `ExactFingerprintMatch` | 距离为 0；仅说明该算法摘要相同 |
| `NearUnderReferencePolicy` | 距离未超过经校准的 V1 参考阈值 |
| `NotNearUnderReferencePolicy` | 距离超过参考阈值 |
| `NotComparable` | 算法 ID 不同、缺少结果或计算失败 |

阈值不能由开发者凭感觉写入 UI。G0 必须先建立校准清单，G2/G3 用实际数据冻结 `fingerprint-reference-policy-v1`：

- 正样本：同一基础图片的缩放、PNG/JPEG 往返、轻度亮度、轻度对比度和轻度中心裁剪；
- 负样本：结构明显不同、粗略亮度相似、平坦图、渐变图、棋盘图、重复纹理和自然图片对；
- 所有测试图片必须来源清楚、可提交、无敏感内容且可离线重现；
- 同一原图的多个扰动不能被错误当成彼此独立的大样本；
- 记录每算法正负距离分布、阈值下假接近/假不接近案例及选择理由；
- 阈值和算法 ID 一起写入报告；策略改变必须升 PolicyId。

若 G0 数据不足以支持可靠阈值，V1 仍可发布距离和位相似度，但结论只能显示“距离解释待校准”，不得伪造阈值。实施者不能为了赶进度删除这个安全降级路径。

### 11.3 总览规则

- 三种算法均为 `ExactFingerprintMatch` 或 `NearUnderReferencePolicy`：`一致接近`；
- 三种算法均为 `NotNearUnderReferencePolicy`：`一致不接近`；
- 其他组合：`结果分歧，需要查看图片和算法限制`；
- 任一算法 `NotComparable` 时总览标记 `结果不完整`；
- 总览规则不覆盖单算法结果，也不参与 Robustness Lab 的原始距离曲线。

## 12. 双图应用工作流与资源预算

### 12.1 计算链

```text
校验两条非空显式路径
    ↓
顺序解码 A，再解码 B，控制解码峰值
    ↓
分别生成最大边 1024 的显示代理
    ↓
按固定顺序计算 A/B 的 aHash、dHash、pHash
    ↓
逐算法计算距离、位相似度和策略结论
    ↓
生成不可变摘要与算法限制列表
    ↓
建立 Document 独占 Session
    ↓
Document 通过 generation、路径和 Session 身份检查后提交结果
```

两图尺寸允许不同；不同尺寸正是感知指纹的目标情形，不得沿用 Image Compare Lab 的同尺寸阻断。不得为了指纹比较修改 `ImagePairValidator` 的语义。

### 12.2 Session 所有权

`FingerprintComparisonSession` 长期持有：

- 两张完整 `PixelImage`；
- 两张最大边 1024 的显示代理；
- 两组各三条 64 位指纹；
- 一份不可变比较摘要；
- 可选的当前稳定性结果，但不长期保存每个扰动的完整图片。

Dispose 后用 1×1 空图切断完整图片和代理引用，所有读取入口先 `ThrowIfDisposed`。算法 singleton 绝不能持有图片、归一化矩阵或上次结果。

### 12.3 资源预算

- 输入继续受 16M 像素和 64 MiB 编码数据上限保护；
- 两张完整 RGBA 图片是主要长期内存；
- 预览每张最大边 1024；
- 最大算法工作矩阵为 32×32 `double`，低频矩阵 8×8；
- 指纹和比较摘要为常量级内存；
- 稳定性试验串行执行，最多保留基线、当前扰动图片和当前预览；
- 不缓存所有扰动图片，不使用无界 Task 并行；
- 取消在解码、归一化目标行、DCT 频率行和稳定性案例边界检查。

## 13. 稳定性试验

### 13.1 产品语义

稳定性试验回答“这张明确选择的参考图在某种受控变化下，三个指纹会怎样变化”。它不是批处理器，也不修改用户原文件。所有样本只在内存中生成，用户可以查看当前样本预览，但 V1 不提供批量另存。

### 13.2 固定试验类型

| 类型 | V1 扫描轴 | 固定规则 |
| --- | --- | --- |
| 缩放 | 长边比例 | 等比例缩小后再以相同规则计算指纹；记录实际尺寸 |
| JPEG | 质量 100→40 的受控列表 | 使用现有正式编码—解码往返；透明输入可见阻断 |
| 亮度 | `-20%..+20%` 的固定列表 | 复用既有亮度算子，Alpha 不变 |
| 轻度裁剪 | 每边中心裁剪比例 0–10% | 中心裁剪后直接按算法规则归一化，不隐藏配准 |

每次只选择一种试验和一个强度轴。最多 21 个点，串行执行；不允许把四类操作自由组合成通用链，因为 Robustness Lab 已经拥有受控链职责。

### 13.3 复用既有扰动的边界

`Domain/Fingerprinting` 不引用 `Domain/Robustness`。应用层定义窄的 `IFingerprintStabilityChannel`，Infrastructure 适配器显式调用既有缩放、亮度、裁剪和 JPEG 实现。这样既不复制数学，也不把鲁棒性配方、随机试验和水印语义泄漏进指纹领域。

适配器只暴露四个冻结操作，不转发任意 `PerturbationParameters` 字典，也不建立第二个通用扰动链。若既有算子行为改变，鲁棒性与指纹两套 Golden/回归门禁都必须可见失败。

### 13.4 结果

每个点记录：

- 试验类型、请求强度和实际参数；
- 输出尺寸；
- aHash/dHash/pHash 的摘要、汉明距离、位相似度和策略结论；
- 算法首次超过参考阈值的位置；
- 当前样本能否生成预览；
- 失败、取消或不适用原因；
- JPEG 时的真实编码字节长度，但不保存编码字节。

裁剪曲线必须附带“算法没有几何配准，距离上升是预期现象”。JPEG 对含透明像素图片显示 `NotApplicable/AlphaUnsupported`，不能静默铺底后冒充原编码语义。

## 14. Robustness Lab 观测入口

### 14.1 目标

允许用户在既有鲁棒性实验中勾选 aHash、dHash、pHash 观察值，比较每个案例最终图与该 Profile 未扰动水印基线图之间的指纹距离。该入口是观测，不改变扰动、扫描、水印恢复、BER、质量或成功率语义。

### 14.2 朴素集成

建议在 `Application/Robustness` 增加一个窄接口：

```csharp
internal interface IFingerprintObservationProbe
{
    FingerprintObservation Observe(
        PixelImage reference,
        PixelImage candidate,
        IReadOnlyList<FingerprintAlgorithmId> algorithms,
        CancellationToken cancellationToken);
}
```

- 默认未勾选时不调用，既有结果和性能保持不变；
- 实现复用三个算法 singleton，不复制哈希；
- 结果只追加 AlgorithmId、两个摘要、距离和位相似度；
- 不把指纹结论混入水印恢复成功率；
- Profile A/B/C 各自使用自己的未扰动水印基线，不能错误使用原始载体；
- 报告 schema 通过新版本或明确可选字段扩展，旧报告读取不受影响；
- 不把 Robustness Lab 改造成通用观测插件系统或反射指标注册中心。

### 14.3 集成门禁

- 未启用指纹观测时，既有 122 项基线和既有报告字段逐项回归；
- 启用后，同一图片距离为 0；
- 尺寸变化仍可计算指纹，但全参考 PSNR/SSIM 继续保持原有 N/A 语义；
- 指纹距离不能改变案例水印成功/失败分类；
- 取消和资源上限不因新增观测失效；
- 旧 snapshot 中没有指纹字段时按未启用恢复。

## 15. 界面与交互设计

### 15.1 总体布局

```text
┌────────────────────────────────────────────────────────────┐
│ 参考图 [选择] [路径/文件名]   候选图 [选择] [路径/文件名] │
│ [计算指纹] [交换 A/B] [取消] [复制摘要] [导出 JSON]       │
├───────────────────────┬────────────────────────────────────┤
│ A/B 并排预览          │ 结论总览                           │
│ 尺寸、Alpha、缩放说明 │ 一致接近 / 分歧 / 一致不接近      │
├───────────────────────┴────────────────────────────────────┤
│ 算法  A 摘要  B 摘要  距离/64  位相似度  结论  [限制]     │
│ aHash ...                                                  │
│ dHash ...                                                  │
│ pHash ...                                                  │
├───────────────────────┬────────────────────────────────────┤
│ A 的 8×8 指纹位图     │ B 的 8×8 指纹位图与 XOR 位图      │
├───────────────────────┴────────────────────────────────────┤
│ 稳定性试验：[类型] [强度列表] [运行]  距离曲线/样本详情   │
└────────────────────────────────────────────────────────────┘
```

### 15.2 状态与命令

- 未选择两张图片时禁用计算；
- 路径改变立即标记旧结果过期，不能继续导出；
- 计算中禁用会改变输入或算法语义的命令，但保留取消；
- “交换 A/B”会清除 Session 后重新计算；汉明距离对称，但报告角色和预览必须同步交换；
- 选择算法行只更新位图和限制文本，不重新计算；
- 运行稳定性试验时保留双图比较结果，试验结果单独有 generation；
- 失败信息区分解码失败、取消、资源拒绝、算法失败和报告失败；
- 不用绿色/红色作为唯一结论编码，文字和图标必须同时存在。

### 15.3 限制说明

每个算法行都必须可展开显示：

- 它观察的是平均亮暗、水平梯度还是低频结构；
- 当前 AlgorithmId、输入规格和阈值策略；
- 对缩放、JPEG、亮度、裁剪、旋转和镜像的预期；
- 距离 0 不是文件相同证明；
- 位相似度不是概率；
- 阈值来自 V1 参考校准，不适用于所有图片分布。

### 15.4 View 与代码隐藏边界

- AXAML 负责布局、绑定、样式和 AutomationProperties；
- Code-behind 只处理 Pointer 坐标、键盘导航和自绘控件失效；
- Document 负责命令与状态，不在属性 setter 中运行重算法；
- 自绘控件只消费不可变绘制 DTO，不持有 Session 或启动计算；
- 所有可点击位图/曲线位置都有键盘替代和文本详情。

## 16. 报告与隐私

### 16.1 JSON schema 1

建议顶层字段：

- `schemaVersion`；
- `completedAtUtc`；
- `referenceName`、`candidateName`，默认只保留文件名；
- 两图尺寸和 Alpha 是否存在；
- `normalizationId`；
- `decisionPolicyId`；
- 三种算法的 AlgorithmId、A/B 十六进制摘要、距离、位相似度和结论；
- 总览结论和固定免责声明；
- 可选稳定性 recipe、点结果和完成状态；
- 插件版本只作诊断，不代替 schema/算法版本。

### 16.2 不导出内容

- 绝对路径；
- 原图或预览像素；
- PNG/JPEG 编码字节；
- EXIF、文件 SHA、系统用户名或异常堆栈；
- 自动扫描得到的其他文件；
- “同源概率”或未经校准的法律结论。

### 16.3 原子输出

- 先把稳定 DTO 序列化为 UTF-8，再调用 `IAtomicFileWriter`；
- 取消或失败不留下半个正式目标；
- 建议文件名只使用 UTC 时间和 `fingerprint-report`；
- 导出失败不销毁 Session；
- JSON 非有限数必须显式编码或避免产生，不能生成非法 JSON；
- 剪贴板文本与 JSON 使用同一领域摘要，不各自重新计算结论。

## 17. G0–G8 实施包

### G0：产品、数值与校准基线

目标：在生产代码前冻结会改变摘要的全部事实。

- 确认双图范围、免责声明、输入/输出术语和稳定 ID；
- 冻结 Alpha、BT.601、缩放、位序、比较符号、中位数和十六进制格式；
- 建立可提交的合成图与授权自然图片校准清单；
- 统计三算法正负样本距离并冻结或延期参考阈值；
- 冻结资源上限、快照、报告隐私和回滚策略；
- 建立 `history/README.md` 与 G0 实际记录；
- 复跑 122 项起始测试并记录实际结果。

门禁：没有代码改动；算法语义无二义性；阈值有证据或明确降级为只显示距离；既有 122 项不回归。

### G1：归一化与 64 位领域核心

目标：建立纯数值、无 UI 的公共基础。

- 实现 `FingerprintLumaNormalizer`、白底 Alpha 合成和固定尺寸缩放；
- 实现 AlgorithmId、`ImageFingerprint`、解析/格式化和位图映射；
- 实现同算法汉明距离和不同算法结构化阻断；
- 实现位相似度格式与取消；
- 建立 1×1、透明、棋盘、渐变和极端尺寸测试。

门禁：Domain 零 Avalonia/JSON/文件依赖；位序 Golden Vector 固定；源图不变；隐藏透明 RGB 不影响结果。

### G2：aHash 与 dHash

目标：完成两种低成本指纹并冻结算法身份。

- 实现两个朴素 Strategy；
- 建立均匀图、水平/垂直渐变、左右反转和单像素边界测试；
- 使用与生产实现不同路径计算的 Golden Vector；
- 验证 `>=` / `>` 的相等分支；
- 建立取消、确定性和跨实例无状态测试；
- 写入数学原理与限制草稿。

门禁：同输入重复结果完全一致；摘要与位图一致；aHash/dHash 不共享错误阈值逻辑。

### G3：pHash 与 DCT 复用

目标：实现标准化低频 pHash，同时保护现有数值基础。

- 抽取最小 `OrthogonalDctBasis`；
- 实现只计算 32×32 左上 8×8 的 `LowFrequencyDctTransform`；
- 实现 AC 中位数和 64 位输出；
- 用常量、冲激、余弦波和手工小矩阵验证 DCT 方向与缩放；
- 与独立参考脚本或可信参考实现交叉核对固定向量；
- 完整回归 `Dct8x8Transform`、频域分析、水印嵌入/提取和协议测试。

门禁：既有 DCT 数值与水印行为不变；pHash 不使用完整 32×32 频率缓存；算法 ID 与实际公式一致。

### G4：双图比较、结论与报告

目标：形成不依赖 Document 的完整应用闭环。

- 实现顺序解码、预览、三算法计算和 Session；
- 实现版本化 DecisionPolicy 与总览规则；
- 实现 JSON DTO、Serializer、剪贴板文本和原子导出用例；
- 对不同尺寸、透明图、路径错误、取消和算法失败建立测试；
- 验证报告只有文件名、无像素/绝对路径/堆栈。

门禁：结论不称概率；不同算法不互算距离；Session Dispose 后拒绝访问；报告 schema 1 可解析。

### G5：Document 生命周期

目标：接入第六个多实例 Persistable Document。

- 增加稳定 Document ID、Module 贡献和 DI 显式登记；
- 实现路径、命令、generation、两类取消、Revision 和错误状态；
- 实现快照 schema 1 与安全恢复；
- 图片变化、交换、关闭和迟到结果均正确失效；
- 两个 Scope 的路径、Session、取消与 Bitmap 互不影响；
- Standalone 通过真实 Module 自动显示第六个 Document。

门禁：Module 六个 Persistable Document、零普通 Document、零 Tool；恢复不自动读取；关闭释放所有资源。

### G6：UI、位图与解释

目标：完成可操作、可解释、可访问的比较界面。

- 实现双图预览、算法结果表、三类位图和限制说明；
- 实现复制、导出、交换、取消和状态提示；
- 实现键盘算法选择、位单元格说明和高对比模式；
- 增加 Headless View/Control 加载和关键绑定测试；
- 检查窄窗口、长文件名、中文错误和 200% 缩放布局。

门禁：View 不含算法和文件写入；颜色不是唯一提示；位图与十六进制 Golden 映射一致。

### G7：稳定性与 Robustness Lab 观测

目标：完成受控实验和跨能力复用，不扩大为通用工作流。

- 实现四种固定 recipe 和最多 21 点验证；
- 建立 `IFingerprintStabilityChannel` 基础设施适配；
- 串行运行、进度、取消、曲线聚合和当前样本预览；
- 增加 `IFingerprintObservationProbe` 到 Robustness 应用层；
- 扩展报告/快照的可选指纹观测字段；
- 证明未启用时既有鲁棒性行为逐项不变。

门禁：不复制扰动算法；不保存所有样本图；指纹不改变水印成败；无 AIFLOW 或通用指标注册中心。

### G8：本地集成与开发封板

目标：完成本地开发证据和整套专用文档。

- locked-mode restore；
- Debug/Release warn-as-error build/test；
- 复核所有新增/既有测试的零失败、零跳过和实际总数；
- 运行 Standalone 的本地人工场景并记录未执行项；
- 同步根 README、docs 索引、未来能力、公共领域边界和部署状态；
- 完成 README、guide、user-manual、mathematical-principles、testing、report-schema 和 G0–G8 记录；
- 明确记录 Windows CI、ZIP、真实 Host 与发布门禁未执行。

门禁：本地证据可复现；文档只声称实际完成内容；无发布完成措辞。

## 18. 预计代码、测试与文档落点

### 18.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Domain/Fingerprinting/
│  ├─ FingerprintAlgorithmId.cs
│  ├─ ImageFingerprint.cs
│  ├─ FingerprintLumaNormalizer.cs
│  ├─ AverageHashAlgorithm.cs
│  ├─ DifferenceHashAlgorithm.cs
│  ├─ PerceptualHashAlgorithm.cs
│  ├─ FingerprintDistanceCalculator.cs
│  ├─ FingerprintDecisionPolicy.cs
│  └─ FingerprintResults.cs
├─ Domain/Frequency/
│  ├─ OrthogonalDctBasis.cs
│  ├─ LowFrequencyDctTransform.cs
│  └─ Dct8x8Transform.cs                  # 只做受门禁保护的最小复用调整
├─ Application/Fingerprinting/
│  ├─ FingerprintContracts.cs
│  ├─ FingerprintComparisonUseCases.cs
│  ├─ FingerprintStabilityUseCase.cs
│  └─ FingerprintReportExportUseCase.cs
├─ Infrastructure/Fingerprinting/
│  ├─ FingerprintStabilityChannel.cs
│  └─ FingerprintReportSerializer.cs
├─ Features/ImageFingerprint/
│  ├─ ImageFingerprintDocument.cs
│  ├─ ImageFingerprintView.axaml
│  ├─ ImageFingerprintView.axaml.cs
│  ├─ FingerprintBitmapControl.cs
│  ├─ FingerprintStabilityControl.cs
│  └─ ImageFingerprintHelpCatalog.cs
├─ Constants/PluginIds.cs
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs
```

实际实施时可以合并只含少量紧密值对象的文件，但不能合并职责。不得为了匹配本树而创建只有转发作用的空壳类型。

### 18.2 测试

```text
tests/ImageLabPlugin.Tests/
├─ FingerprintNormalizationTests.cs
├─ AverageAndDifferenceHashTests.cs
├─ PerceptualHashTests.cs
├─ FingerprintDistanceAndPolicyTests.cs
├─ FingerprintComparisonUseCaseTests.cs
├─ FingerprintStabilityTests.cs
├─ ImageFingerprintDocumentTests.cs
├─ ImageFingerprintViewTests.cs
└─ RobustnessFingerprintObservationTests.cs
```

组合根、持久化和 Standalone 的既有测试直接扩展，不另建一套重复测试基类。

### 18.3 专用文档

```text
docs/design/image-fingerprint/
├─ README.md
├─ implementation.md
├─ testing.md
├─ guide.md
├─ user-manual.md
├─ mathematical-principles.md
├─ report-schema.md
└─ history/
   ├─ README.md
   └─ g0...g8 实际记录
```

本文先建立 `implementation.md`。其余文档必须随对应 G 包写实际行为，不能在功能未实现时复制未来式计划并假装已经可用。

## 19. 单元测试与本地质量门禁

### 19.1 SOLID 与结构门禁

- `Domain/Fingerprinting` 不引用 Avalonia、JSON、文件、DI、Infrastructure 或 Robustness；
- 每个算法单责，Strategy 只有一个且显式登记；
- Document 不出现逐像素、缩放、DCT、PopCount、JSON 或 JPEG 逻辑；
- Infrastructure 适配既有扰动，不复制公式；
- Module 固定贡献六个 Persistable Document、零普通 Document、零 Tool；
- 无 AIFLOW、Workflow Action、Workbench Command、反射扫描、Service Locator 或通用 DAG。

### 19.2 归一化门禁

- 1×1、2×2 到 8×8/9×8/32×32 的放大坐标 Golden；
- 大图面积缩小的覆盖权重与边界；
- Alpha 0、1、254、255 和白底合成公式；
- 透明隐藏 RGB 不影响指纹；
- BT.601 极值、舍入前 double 和显示裁切；
- 非等比拉伸、源图不变、取消和确定性；
- 热循环无逐像素 LINQ 和无界分配。

### 19.3 aHash/dHash 门禁

- 均匀图 aHash 全 1；
- 均值相等位置走 `>=`；
- 水平递增/递减 dHash 的全 0/全 1 Golden；
- 相等相邻值走 0；
- 垂直变化不被错误当成水平比较；
- 8×8 位序、`X16` 摘要和解析往返；
- 同输入、不同实例和不同执行顺序结果一致。

### 19.4 pHash 与 DCT 门禁

- N=32 常量输入只有 DC 显著；
- 单像素冲激的独立参考系数；
- 已知余弦波的能量落在预期 `(u,v)`；
- 只返回左上 8×8，布局和符号方向正确；
- 63 个 AC 中位数索引、重复值和 `>=` 分支；
- pHash Golden 摘要与独立参考实现一致；
- 既有 `Dct8x8Transform` 全部数值测试不变；
- 水印嵌入、检测、提取、频域 DCT 检查不放宽阈值、不更新协议期望。

### 19.5 距离、策略与报告门禁

- 距离 0、1、32、63、64；
- `PopCount` 与逐位独立实现交叉核对；
- 不同 AlgorithmId 结构化拒绝；
- 位相似度端点和两位小数；
- Exact/Near/NotNear/NotComparable 分支；
- 三种总览组合与结果不完整；
- PolicyId、AlgorithmId、NormalizationId 均进入 JSON；
- UTF-8、schema 1、绝对路径/像素/堆栈隐私和原子写入。

### 19.6 用例、Document 与 UI 门禁

- A 后 B 的顺序解码；不同尺寸仍可比较；
- 任一路径失败不提交半个 Session；
- Session Dispose 后所有访问失败；
- 路径变化、交换、取消、关闭和忽略取消的迟到结果；
- 快照只含路径与轻量参数，恢复不自动计算；
- 两个 DI Scope 完全隔离，算法 singleton 无状态共享；
- 第六个 View 与两个自绘 Control 可在 Headless 环境加载；
- 键盘导航、AutomationProperties 和非颜色提示存在；
- Standalone 使用真实 Module，未复制 Document/View。

### 19.7 稳定性与 Robustness 回归门禁

- 四种 recipe 的参数边界、排序、去重和 21 点上限；
- 每个点从同一参考图开始，案例顺序不影响结果；
- JPEG Alpha 阻断，尺寸与编码长度记录；
- 取消点不进入完整曲线，不保存所有完整样本；
- 首次越阈值位置准确，未校准策略显示 NotAvailable；
- Robustness 未启用观测时结果与 schema 兼容；
- 启用观测时同图距离 0，尺寸变化可计算；
- 指纹观测不改变水印成功率、BER、质量 N/A 和失败分类。

### 19.8 回归与资源门禁

- 现有 122 项是起始基线，最终记录实际总数，不预估“新增后应有多少项”；
- 不降低任何水印、频域、图像比较或鲁棒性断言；
- 最大输入结构测试证明长期完整图数量受控；
- 稳定性 21 点测试证明不缓存 21 张完整图；
- 性能使用结构和宽松预算，不使用机器相关严格毫秒断言；
- 所有测试零跳过；不能用注释、截图或覆盖率百分比代替关键分支断言。

### 19.9 本地开发命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

本地 Release 只是第二编译配置的开发回归，不表示发布。G8 不新增或运行 Windows CI，不构建 ZIP，不部署真实 Host；发布时再按 `docs/design/shared/deployment-and-release.md` 启用相应门禁。

## 20. 人工验收场景

### 20.1 基础双图

1. 打开两个感知指纹 Document，分别选择不同图片对；
2. 确认两个 Scope 的路径、结果和取消互不影响；
3. 比较同一 PNG 的复制文件，三种距离为 0；
4. 比较同一内容的 PNG/JPEG，查看三算法距离和限制；
5. 交换 A/B，确认距离不变、角色和报告名称交换。

### 20.2 缩放、亮度和裁剪

1. 比较原图与等比例缩小图；
2. 比较轻微增亮图，确认位相似度不是“概率”；
3. 比较 5% 中心裁剪图，观察算法分歧与裁剪限制；
4. 比较旋转或镜像图，确认界面没有不变性承诺；
5. 比较两个粗略亮度相似但内容不同的图片，复核 aHash 碰撞风险。

### 20.3 位图与结论

1. 逐行核对 8×8 位图、XOR 位图、十六进制和距离；
2. 用键盘选择算法和位单元格；
3. 在高对比主题下确认 0/1 可区分；
4. 展开三种算法限制，确认 AlgorithmId 和 PolicyId 可见；
5. 制造三算法分歧，确认总览要求人工复核而不是输出平均分。

### 20.4 稳定性、取消与恢复

1. 分别运行缩放、JPEG、亮度、中心裁剪曲线；
2. 运行中取消，确认已有双图结果仍可查看，曲线标记未完成；
3. 修改路径，确认旧曲线和导出资格立即失效；
4. 保存布局后关闭恢复，确认路径恢复但没有自动读图和计算；
5. 导出 JSON，确认只有文件名、算法事实和距离，没有绝对路径或像素。

### 20.5 Robustness Lab 集成

1. 在未启用指纹观测时运行既有实验，确认结果不变；
2. 启用三算法并运行缩放/JPEG，查看每案例距离；
3. 确认尺寸不匹配时质量仍为 N/A，而指纹距离可用；
4. 确认水印成功率不会因指纹接近或不接近而改变；
5. 导出报告并检查可选观测字段的 schema 与隐私。

Standalone 只用于本地布局、命令、曲线和资源释放检查，不能替代真实 Host、ZIP、Windows CI 或发布验收。未执行的人工场景必须在 G8 实际记录中写“延期”。

## 21. 兼容、迁移与回滚

### 21.1 兼容规则

- 既有五个 Document ID、贡献顺序和行为保持不变，第六个 ID 一旦进入布局不得改名；
- AlgorithmId 同时冻结归一化、尺寸、阈值位规则和位序；任何变化都必须产生新 ID；
- DecisionPolicyId 独立于 AlgorithmId，阈值变化只升策略版本；
- 报告 schema、Document snapshot schema 和 Robustness report schema 分别版本化；
- 不修改水印协议、DCT-QIM、Profile、图像比较同尺寸语义或鲁棒性成功定义；
- 第三方同名 pHash 只有完整规格一致时才可称互操作，不能只按“64 位”判断。

### 21.2 回滚顺序

1. 可先隐藏 Module 中第六个 Document 贡献，同时保留类型供旧布局安全识别；
2. 再关闭 Robustness Lab 中默认关闭的指纹观测入口；
3. 再移除 Feature UI 和专用应用用例登记；
4. 再移除指纹算法与报告实现；
5. 最后评估共享 DCT 数值基元，但必须让原 `Dct8x8Transform` 保持可独立恢复；
6. 已导出的用户 JSON 不删除、不覆盖。

### 21.3 无破坏迁移

V1 首次实现没有旧指纹快照或报告需要迁移。开发期 schema 改动仍必须显式处理版本；未知字段可忽略，未知 AlgorithmId、PolicyId 或改变摘要语义的字段必须可见阻断，不能捕获所有异常后返回全零指纹。

## 22. 中文注释与实施纪律

- 新增跨层契约、算法类、数值基元、Session 和报告 DTO 必须有详细中文 XML 注释；
- 注释重点解释“为什么采用该规则、单位是什么、边界在哪里、谁负责释放”，不逐行翻译代码；
- Alpha 白底、BT.601、面积缩小、双线性放大、DCT 归一化、中位数、位序和 PopCount 必须写公式或准确语义；
- 所有兼容性敏感比较符号必须注释，例如 aHash/pHash 用 `>=`、dHash 用 `>`；
- pHash 注释必须说明为什么中位数排除 DC、为什么仍输出 DC 位、为什么不保证第三方互操作；
- Session 注释必须说明完整图、代理、指纹和 Bitmap 的所有者及 Dispose 行为；
- 结论策略注释必须明确位相似度不是概率、阈值不是普适真理；
- Robustness 观测注释必须说明它不改变水印成功、BER 或质量语义；
- 复杂循环先用中文概述数据流、取消点和内存预算，代码本身保持直接；
- 不通过降低断言、增加重试、扩大误差、更新既有 Golden 或跳过测试来封板；
- 不使用 AIFLOW，不新增 Windows CI 或发布门禁；
- 设计模式只在真实替换点使用，禁止为了“模式数量”增加层级。

建议关键注释采用以下风格：

```csharp
/// <summary>
/// 把规范化亮度矩阵的左上低频 DCT 系数投影为 64 位 pHash。
/// </summary>
/// <remarks>
/// 阈值中位数只使用 63 个 AC 系数，避免整体亮度对应的 DC 系数支配阈值；
/// 但输出仍保留 DC 位，以维持固定 8×8、64 位和行优先布局。
/// 比较符号固定为大于等于，改变它会使重复系数的摘要不兼容，因此必须升 AlgorithmId。
/// </remarks>
```

## 23. V1 开发封板检查清单

### 产品与语义

- [ ] 只处理两张用户显式选择的图片，不扫描目录；
- [ ] aHash、dHash、pHash 的输入、位序、摘要和算法 ID 已冻结；
- [ ] 汉明距离与位相似度准确，且没有概率措辞；
- [ ] 三算法独立结论、分歧和限制可见；
- [ ] 阈值有校准证据，或安全降级为只显示距离；
- [ ] wHash 明确等待 DWT 基础，不以占位代码进入 V1。

### SOLID 与生命周期

- [ ] Domain/Application/Infrastructure/Feature 依赖方向正确；
- [ ] 只有一个朴素算法 Strategy，没有反射、工厂堆叠或通用 DAG；
- [ ] Document 不执行算法、解码、JSON、JPEG 或逐像素循环；
- [ ] 两个 Scope、取消、generation、关闭和迟到结果安全；
- [ ] Session、完整图片、预览和 Bitmap 的所有权清楚；
- [ ] 第六个 Persistable Document 身份和快照稳定。

### 数值与资源

- [ ] Alpha、BT.601、插值、DCT、中位数和位序有 Golden Vector；
- [ ] 现有 DCT、频域和水印协议行为零回归；
- [ ] 算法摘要与独立参考实现交叉验证；
- [ ] 两张 16M 像素图片和 1024 代理的资源边界受控；
- [ ] 稳定性最多 21 点、串行执行且不缓存所有完整图片；
- [ ] Robustness 观测不改变原成功、BER 和质量语义。

### 测试与文档

- [ ] 现有 122 项起始回归未放宽，最终实际测试总数已记录；
- [ ] Domain、用例、Document、报告、Headless UI 和组合根门禁齐全；
- [ ] locked restore 与 Debug/Release warn-as-error build/test 全部通过；
- [ ] JSON、快照、隐私、取消、迟到结果和 Scope 隔离测试齐全；
- [ ] 根 README、docs 索引、未来能力和公共领域边界已同步；
- [ ] README、指南、新手说明、数学原理、测试、报告 schema 和 G0–G8 记录齐全；
- [ ] 无 AIFLOW、Workflow Action、Workbench Command；
- [ ] 无 Windows CI、ZIP、真实 Host 或发布完成声明。

只有所有开发项都有实际证据后，计划状态才可改为“开发实现与本地自动门禁完成”。这仍不等于已经发布；发布阶段必须另行执行真实 Host、正式包、目标设备和发布验收。

## 24. V1.1：wHash 的进入条件

wHash 不作为 G0–G8 的完成条件。只有以下条件全部满足后，才新建设计阶段并增加 AlgorithmId：

- ImageLab 已有经独立设计与测试的二维 DWT 公共基础，而不是为 wHash 临时复制一份 Haar 代码；
- DWT 明确边界扩展、奇偶尺寸、分解层数、归一化、系数布局和逆变换误差；
- wHash 冻结输入尺寸、小波族、层数、保留子带、中位数、位序和 64 位降采样规则；
- 有独立 Golden Vector 和与可信参考实现的交叉验证；
- 有针对缩放、JPEG、亮度、裁剪和纹理图片的阈值校准；
- 新算法接入现有 Strategy 即可，不要求修改 aHash/dHash/pHash；
- 报告和快照用新 AlgorithmId 表达，不复用 `phash` 或含糊的 `whash-v1`；
- 仍不扫描目录、不建立图库索引、不使用 AIFLOW。

在这些条件满足前，界面不显示灰色 wHash 按钮，不建立空实现，不在枚举中预留“以后再说”的无效成员。
