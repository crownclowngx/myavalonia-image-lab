# ImageLabPlugin V1 频域隐式水印实施计划

> 计划状态：G0–G8 开发能力已实施，G9 本地集成已完成；真实 Host、ZIP、Windows CI 与发布封板按当前要求延期
> 基线日期：2026-08-30
> 产品名称：频域隐式水印（Frequency-domain Invisible Watermark）
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3
> 核心路线：8×8 DCT + 中频系数 + QIM + ECC + 交织/扰码 + 冗余映射
> 实施原则：先证明可逆、容量和协议正确，再接入加密与 UI，最后验证轻度重编码鲁棒性

| V1 实施包 | 状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 完成 | 冻结产品、威胁模型、质量口径、协议边界与依赖决策 | [实施记录](../plan-history/frequency-watermark/g0-product-protocol-baseline.md) |
| G1 | 完成 | 用两个真实 Document 替换模板示例并建立 Standalone 贡献浏览基线 | [实施记录](../plan-history/frequency-watermark/g1-two-document-shell.md) |
| G2 | 完成 | 建立可供后续图像工具复用的 Imaging/Frequency Domain | [实施记录](../plan-history/frequency-watermark/g2-imaging-frequency-domain.md) |
| G3 | 完成 | 冻结版本化二进制 Frame、压缩、加密和安全边界 | [实施记录](../plan-history/frequency-watermark/g3-frame-and-security.md) |
| G4 | 完成 | 完成容量规划、DCT-QIM 嵌入和输出图生成 | [实施记录](../plan-history/frequency-watermark/g4-embedding-engine.md) |
| G5 | 完成 | 完成检测、提取、ECC 恢复和验证报告 | [实施记录](../plan-history/frequency-watermark/g5-extraction-and-verification.md) |
| G6 | 完成 | 完成“水印写入”Document 的真实交互闭环 | [实施记录](../plan-history/frequency-watermark/g6-embed-document.md) |
| G7 | 完成 | 完成“提取与验证”Document 的真实交互闭环 | [实施记录](../plan-history/frequency-watermark/g7-inspect-document.md) |
| G8 | 完成（开发门禁） | 建立质量、鲁棒性、错误检测、性能和安全门禁 | [实施记录](../plan-history/frequency-watermark/g8-quality-and-robustness.md) |
| G9 | 本地集成完成；发布延期 | 完成 Standalone、本地构建测试与文档；真实 Host、ZIP、CI、发布封板未执行 | [实施记录](../plan-history/frequency-watermark/g9-integration-and-sealing.md) |

本文是 ImageLabPlugin 的第一个产品能力总计划。V1 不把需求定义成“在 FFT 图上画字”，而是定义成：

> 把任意但受图片容量限制的二进制 Payload，以肉眼不明显的方式嵌入图片，并由同一协议检测、恢复、
> 校验和可选解密。

本文中的“不可见”仅表示感知层面不明显，不表示统计上不可检测。V1 是数字水印与隐式数据通道，不宣称
能够对抗掌握算法的主动移除者，也不把扰码或位置映射描述成密码学加密。

每个 G 包完成后，必须把实际修改、测试结果、性能与质量数据、偏差、遗留风险和回滚方式写入对应的
`docs/plan-history/frequency-watermark/` 记录。本文定义目标与门禁，不预填完成结论。

## 1. V1 目标与固定实施顺序

V1 的两个用户闭环为：

```text
水印写入

选择源图片
    ↓
输入文本、JSON 或任意文件 Payload
    ↓
估算容量并选择隐蔽/均衡/鲁棒配置
    ↓
可选压缩和密码保护
    ↓
DCT-QIM 嵌入并生成新图片
    ↓
使用正式提取器自动回读自检
    ↓
显示质量报告并原子保存输出
```

```text
提取与验证

选择待检查图片
    ↓
扫描公共控制信道
    ↓
判断未发现 / 已发现 / 需要密码 / 已损坏
    ↓
QIM 解调、投票、去交织和 ECC 恢复
    ↓
可选解密、解压与签名验证
    ↓
预览或导出原始 Payload
    ↓
显示完整验证与损伤报告
```

V1 固定按以下顺序实施：

1. G0 先冻结协议语义、威胁模型、质量口径和第三方依赖；没有这些事实时不得写生产 Frame；
2. G1 先建立两个 Document 的真实生命周期和稳定身份，删除模板示例语义；
3. G2 只建立被当前算法证明需要的公共 Imaging/Frequency 领域，不预建通用图像平台；
4. G3 冻结 Frame 与安全处理顺序，并用 golden vectors 固化线格式；
5. G4/G5 先形成无 UI 的完整往返引擎，再让 G6/G7 接入真实界面；
6. G8 使用图片语料和攻击矩阵决定哪些质量配置可以成为正式能力；
7. G9 才执行真实 Host、确定性打包和 V1 封板，不以 Standalone 通过替代 Host 验收。

## 2. 当前基线与已有事实

### 2.1 当前工程基线

当前仓库已经从 Managed Plugin 模板演进为频域水印开发基线：

- `ImageLabPlugin.Plugin` 是唯一正式插件程序集；
- `ImageLabPlugin.Standalone` 直接承载插件真实 View；
- `ImageLabPlugin.Tests` 覆盖 Domain、协议、安全、真实图片字节回读、Document 生命周期和组合根；
- `PluginIds` 已冻结两个水印 Document 身份，模板 `MainDocument` 与模板命令已删除；
- `ImageLabPluginModule` 只登记两个 `IPersistablePluginDocument`，不登记 Host Tool；
- 已实现 Avalonia PNG/JPEG 编解码、Y-only 8×8 DCT-QIM、RS(255,223)、PBKDF2-SHA256 与 AES-256-GCM；
- Protocol/Profile 仍处于首次发布前开发期；只有执行延期的 G9 发布门禁后才形成外部兼容承诺。

### 2.2 主工程约束

V1 必须继续遵守以下 Host/Plugin 事实：

- Document Model 每实例独立 DI Scope，可以多实例并在真正关闭时释放；
- Tool Model 是插件级 singleton，关闭通常表示隐藏而不是销毁；
- `ImageLabPluginModule.Configure` 是贡献和服务登记的唯一事实源；
- View、Model、业务服务和插件私有资源只放在 Plugin 项目；
- Standalone 不复制第二份业务、ViewModel 或贡献清单；
- 插件只依赖公开 Plugin SDK/UI SDK，不引用 Host、Dock 或 Host 内部实现；
- manifest、依赖闭包和 ZIP 由 Build 包生成，不手工维护；
- 新增私有 NuGet 运行时依赖时，同时更新中央版本、Plugin 引用和正式包私有依赖清单。

### 2.3 产品与 Host 术语

“频域隐式水印工具”是产品能力名称，不等于 Host 的 `Tool` 贡献。V1 登记两个 Document、零个 Tool：

| 稳定身份 | 显示名称 | 职责 |
| --- | --- | --- |
| `myavalonia.plugin.image.lab.document.watermark.embed` | 水印写入 | 生成带隐式 Payload 的新图片 |
| `myavalonia.plugin.image.lab.document.watermark.inspect` | 提取与验证 | 检测、恢复并验证 Payload |

V1 不登记“频谱面板”Tool。频谱、容量、系数置信度和验证报告都属于当前图片实例，放入对应 Document。
只有未来出现跨 Document 的批量队列或全局任务摘要时，才重新评审任务中心 Tool。

## 3. Document 拆分与状态所有权

### 3.1 水印写入 Document

一句话目标：把一段 Payload 嵌入选定图片并生成经过自检的新图片。

分类：

- 意图：转换型；
- 实例基数：多实例；
- 时间尺度：会话级，单次执行可取消；
- 状态敏感度：高。

实例私有状态：

- 源图片引用、指纹和解码摘要；
- Payload 来源、内容类型和当前大小；
- 配置档位、输出格式和高级参数；
- 当前容量估算、阶段、进度和错误；
- 输出图片、质量指标和回读自检结果；
- 本次输入的密码和短生命周期派生密钥。

### 3.2 提取与验证 Document

一句话目标：判断图片是否包含受支持的水印，并恢复、校验和导出 Payload。

分类：

- 意图：消费/分析型；验证报告是其内部参考步骤；
- 实例基数：多实例；
- 时间尺度：会话级，单次执行可取消；
- 状态敏感度：高。

实例私有状态：

- 待检查图片引用、指纹和解码摘要；
- 控制信道扫描结果和协议识别结果；
- 提取阶段、进度、置信度和 ECC 修复统计；
- 用户输入的密码、临时派生密钥；
- 恢复后的 Payload、内容类型和验证报告。

### 3.3 不再拆分的内部步骤

以下均不创建额外 Document：

- RGB/YCbCr 转换、DCT、IDCT、QIM 调制和解调；
- 压缩、解压、加密、解密、签名和验签；
- ECC 编码、纠错、交织、扰码和副本投票；
- 频谱显示、差异图、容量估算和质量计算；
- 提取后的完整性检查与来源验证。

这些步骤没有脱离父目标的独立动机和独立产物，应由 Application 用例协调并由无界面服务执行。

### 3.4 两个 Document 的关联

写入完成后，界面提供“在提取与验证中打开”意图。实现只能使用公开 Host Port 或插件已获准的私有消息/交接
机制，不保存另一个 Document 实例，也不引用 Host 内部服务。若 G1 证明当前 SDK 没有安全入口，V1 降级为：

1. 写入 Document 保存输出；
2. 提取 Document 通过文件选择器打开该输出；
3. UI 显示明确的下一步提示和输出路径。

不得为了消除一次文件选择而破坏 Host 边界。

## 4. 产品范围与威胁模型

### 4.1 V1 必须完成

- PNG、JPEG 输入解码；
- 默认 PNG 输出，可选择 JPEG 输出；
- 保持原始宽高，不执行自动缩放或裁剪；
- 文本、JSON 和任意文件输入统一转换为受容量限制的二进制 Payload；
- 写入前容量估算，容量不足时在修改像素前失败；
- 版本化 Control Channel 与 Data Channel；
- 8×8 DCT、中频系数选择和 QIM 调制；
- ECC、交织/扰码与冗余映射；
- 可选压缩；
- 可选密码保护，使用认证加密；
- 图片水印检测、恢复、错误修复和完整验证报告；
- 输出前回读自检、原子保存、取消和资源释放；
- 基于真实图片语料的感知质量、轻度 JPEG 重编码和少量噪声测试。

### 4.2 V1 条件完成

以下能力只有在 G0/G3 完成依赖、安全和兼容评审后才能进入正式配置：

- Argon2id 密码派生；若依赖、许可证或发布验证不通过，使用平台原生 PBKDF2 的经基准参数；
- 数字签名；V1 必须预留 Frame 能力位，但可以在 V1.x 才提供真实密钥导入与信任模型；
- Cb/Cr 或 Y+Cb/Cr 混合嵌入；只有通过不同 JPEG 编码器和色度抽样矩阵后才能成为正式配置；
- JPEG 直接输出；若目标质量下正式提取器无法稳定回读，则 V1 只正式支持 PNG 输出，JPEG 保持实验入口或关闭。

条件不满足时必须关闭入口或明确标记实验性，不能用测试桩冒充完成。

### 4.3 V1 明确不实现

- 任意 resize、crop、rotation 和 perspective 校正；
- 截图、手机拍屏和 screen-camera watermark；
- 重度滤镜、降噪、锐化、色彩重映射和 AI 重绘后的恢复；
- 不可检测性、抗统计隐写分析或主动攻击者模型；
- 在一张普通图片中隐藏大文件；
- 跨图片分卷、云端密钥托管、证书颁发和组织级信任中心；
- 批量任务队列和全局任务中心 Tool；
- 新 Host SDK、Document envelope、layout schema 或插件间公共 SDK；
- 把 FFT 频谱上可见绘字作为正式数据协议。

### 4.4 安全目标

V1 分别定义四种能力，不得混用术语：

| 能力 | 机制 | V1 承诺 |
| --- | --- | --- |
| 感知隐蔽 | 中频 DCT-QIM 与受控嵌入密度 | 肉眼不明显，必须由质量数据支持 |
| 机密性 | 密码派生密钥 + AES-256-GCM | 无正确密钥不能得到明文 |
| 完整性 | ECC 后的 Frame 校验 + AEAD Tag | 区分可修复传输错误和认证失败 |
| 来源真实性 | 数字签名 | 条件能力；未实现时不得显示“来源可信” |

Scrambling、Mapping Seed、Magic、CRC 和 SHA-256 摘要均不是来源认证。无密钥摘要可被攻击者重新计算；
CRC 只用于发现随机损坏和控制头误识别。

## 5. 目标架构与依赖方向

### 5.1 分层与边界

```text
Features/WatermarkEmbed       Features/WatermarkInspect
             \                    /
              \                  /
                   Application
        Embed / Estimate / Inspect / Extract / Verify
                         ↓
                       Domain
          Imaging + Frequency + Watermarking
                         ↑
                    Infrastructure
       Codecs / Compression / Crypto / ECC / Persistence
```

依赖规则：

1. Features 只负责状态投影、命令和用户意图，不实现 DCT、QIM、加密或文件格式；
2. Application 组织阶段、进度、取消、失败映射和提交点，不依赖 Avalonia Control；
3. Domain 不依赖 Avalonia、文件系统、图片编解码库、DI、日志或 Host SDK；
4. Infrastructure 实现图片编解码、密码学、ECC、原子文件和必要的高性能计算适配；
5. Plugin Module 是唯一组合根；不得在 ViewModel 中创建全局 ServiceProvider 或服务定位；
6. 领域类型默认 `internal`；“公共 Domain”表示插件内跨 Feature 共享，不表示跨插件公共 API。

### 5.2 `Domain.Imaging`：通用图像语义

只放当前与后续图像工具能共同使用的稳定概念：

- `ImageSize`、`PixelFormat`、`ColorSpace`、`ColorChannel`；
- `ImagePlane`、`ImageBuffer`、`BlockSize`、`BlockGrid`；
- Alpha 处理策略、像素数和内存预算；
- `ImageQualityMetrics`、`PsnrValue`、`SsimValue`；
- 规范化像素、通道和块坐标的值对象。

文件路径、PNG/JPEG 元数据、Avalonia Bitmap 和具体编解码器不进入该 Domain。

### 5.3 `Domain.Frequency`：公共频域核心

负责与具体水印协议无关的频域概念和纯计算语义：

- `TransformKind`、`TransformBlockSize`；
- `FrequencyCoordinate`、`FrequencyBand`；
- `FrequencyPlane`、`CoefficientBlock`；
- 正向/逆向块变换；
- 中频候选选择的通用坐标规则；
- 频谱显示所需的归一化只读投影。

DCT 属于 Frequency；QIM 对 bit 的编码规则、控制信道和副本映射属于 Watermarking。不得建立保存“当前图片”
的 singleton `SpectrumService`；变换服务应无状态，工作集由 Document Scope 或单次用例拥有。

### 5.4 `Domain.Watermarking`：产品专属领域

负责协议和业务规则：

- `WatermarkPayload`、`PayloadContentType`；
- `WatermarkProtocolVersion`、`WatermarkFrameHeader`；
- `EmbeddingProfileId`、`EmbeddingProfile`；
- `CapacityEstimate`、`EmbeddingPlan`；
- `ControlChannelPlan`、`DataChannelPlan`；
- `QimParameters`、`RedundancyPlan`、`MappingPlan`；
- `WatermarkDetectionResult`、`ExtractionReport`；
- `IntegrityStatus`、`AuthenticityStatus`、`DamageAssessment`。

Domain 记录算法 ID、约束和结果语义，不实现密码库特有类型，也不暴露可变 `byte[]`。Payload 和 Frame 数据
优先使用不可变值或 `ReadOnlyMemory<byte>`，所有长度在构造边界验证。

### 5.5 Application 用例与窄端口

计划建立以下用例入口：

- `EstimateWatermarkCapacity`：只分析，不改变源图；
- `EmbedWatermark`：生成内存或临时输出并返回质量与自检结果；
- `SaveWatermarkedImage`：只负责最终原子发布；
- `InspectWatermark`：扫描公共控制信道并返回检测状态；
- `ExtractWatermarkPayload`：按已识别协议恢复数据；
- `VerifyWatermark`：形成完整性、认证和可选签名结论；
- `CompareImages`：在有原图时计算 PSNR/SSIM 和差异投影。

按实现证据引入的窄端口包括：

- `IImageDecoder` / `IImageEncoder`；
- `IBlockFrequencyTransform`；
- `IPayloadCompressor`；
- `IPayloadProtector` / `IKeyDeriver`；
- `IErrorCorrectionCodec`；
- `IRandomSource`；
- `IAtomicImageWriter`。

不得为每个具体类建立接口。只有存在外部依赖、多个算法实现、测试替换或明确生命周期边界时才增加端口。

## 6. 版本化 Watermark Frame

### 6.1 双信道设计

V1 使用两个逻辑信道：

```text
Image
├─ Control Channel：小、公开、固定定位、强 ECC、强冗余
└─ Data Channel：容量较大、按 Header 和 Mapping Seed 定位、普通冗余
```

Control Channel 负责让读取器回答：

- 是否存在本产品支持的水印；
- 使用哪个协议版本和正式配置；
- Data Channel 有多长；
- 是否压缩、加密或签名；
- 需要哪个 ECC、KDF 和 Mapping 规则；
- 是否需要用户提供密码或密钥。

Control Channel 是公开信息，不能放密码、派生密钥、明文 Payload、私钥或敏感业务元数据。

### 6.2 Control Header 语义字段

G3 必须冻结以下语义及精确字节序、位宽和最大值：

- Magic；
- Protocol Version；
- Header Length；
- Flags；
- Transform/Profile ID；
- Compression/Encryption/KDF/ECC/Signature Algorithm ID；
- Encoded Data Length；
- 原始 Payload 长度或受保护的长度信息；
- KDF Salt；
- AEAD Nonce；
- 非秘密 Mapping Seed 或密钥派生方式标识；
- Header CRC；
- Header ECC 数据。

本文不提前冻结具体字节数。G3 必须先用最小图片容量和强冗余预算证明 Header 可用，再用 golden vectors 冻结
线格式。协议解析一律使用显式端序和有界长度，不直接序列化 CLR 对象。

### 6.3 Data Frame 处理顺序

```text
Payload bytes
    ↓
Payload Envelope（类型、原始长度、必要元数据）
    ↓
可选压缩（只有实际变小时才保留）
    ↓
可选签名（签名规范化内容；签名材料进入受保护数据）
    ↓
可选 AES-256-GCM（Header 稳定字段作为 AAD）
    ↓
分块 Reed-Solomon 编码
    ↓
Bit/Byte Interleaving
    ↓
Scrambling 与位置映射
    ↓
副本展开
    ↓
DCT-QIM Data Channel
```

读取严格反向执行。ECC 必须先修复密文，再执行 AES-GCM 认证；加密后的数据不可再压缩。签名具体签署明文规范化
内容还是公开密文摘要，由 G0/G3 根据“公开验签是否必须”决定并写入协议，不允许实现中临时改变顺序。

### 6.4 密码与密钥分离

密码模式：

```text
Password + random Salt
        ↓
KDF
        ↓
Master Key
        ↓ HKDF / 等价的上下文分离
        ├─ Encryption Key
        └─ Mapping Key/Seed
```

约束：

- Salt 和 Nonce 每次随机生成，不能从图片名、时间戳或 Payload 推导；
- Encryption Key 与 Mapping Key 不复用；
- 错误密码与被篡改密文都可能导致 AEAD 认证失败，UI 不得声称能够可靠区分；
- 生产随机数只使用密码学安全随机源；测试通过显式测试随机源获得可重复向量；
- 密码、主密钥、派生密钥和明文不得进入日志、异常文本、Document 快照或遥测；
- 敏感缓冲区在所有权结束时尽可能清零，不能被长期 singleton 缓存。

### 6.5 协议兼容规则

- 读取器按 Protocol Version 和 Profile ID 分派，不用“当前默认参数”猜测；
- 未知主版本安全拒绝，未知可选 Flag 按协议规定拒绝或忽略；
- 旧版本读取行为冻结，新版本写入器不得静默改变旧 Profile；
- Header 声明的长度、KDF 参数、ECC 参数和块数必须经过白名单与资源上限校验；
- Magic + Header ECC + CRC 共同降低误识别，不能只凭 Magic 报告“已检测到”；
- 所有正式版本必须具有编码、解码和错误样本 golden vectors。

## 7. 图像与 DCT-QIM 技术设计

### 7.1 图像规范化

V1 处理链：

```text
文件解码
→ 验证尺寸、像素数和内存预算
→ 规范化为受支持的 RGB/RGBA 像素格式
→ RGB 转 YCbCr
→ 生成 8×8 Block Grid
→ 排除透明度或纹理条件不满足的块
```

V1 不改变分辨率。Alpha 原样保留；完全透明或透明度不足的块默认不承载数据，避免不可见 RGB 被编码器改写后
破坏映射。边缘不足 8×8 的区域使用明确的排除或 padding 规则，并由协议版本冻结。

### 7.2 正式通道策略

V1 正式基线采用 Y 通道：

- Control Channel：Y 通道中频，低密度、强 ECC、更多副本；
- Data Channel：Y 通道中频，按 Profile 控制密度、QIM 步长和副本数；
- Cb/Cr 混合嵌入先作为实验配置，不进入 V1 兼容承诺。

该选择优先降低不同 JPEG 色度抽样和编码器造成的不确定性。G8 如果证明混合通道在视觉质量和回读率上均有稳定
优势，可通过新的 Profile ID 启用，不能修改既有 Y-only Profile。

### 7.3 Block 与系数选择

每个候选块必须满足版本化规则：

- 不使用 DC；
- 不使用极低频主体结构；
- 不依赖最容易被 JPEG 清除的极高频；
- 使用固定中频候选集合；
- 可按块纹理或局部能量排除不适合修改的块；
- Control 和 Data 的块集合互不冲突；
- 位置选择由 Profile、图片几何和 Mapping Seed 确定且可重现；
- 不依赖遍历字典、线程调度或平台浮点偶然顺序。

### 7.4 QIM 调制与软判决

写入不使用“系数大于阈值即 1”或简单加减常量。每个 bit 映射到两套量化格点，写入时将系数推到目标集合中
最近的合法格点，读取时计算到两套格点的距离并产生：

- hard bit；
- 距离差或归一化置信度；
- 不确定判决标志。

副本投票优先消费软置信度，不只做无权多数票。QIM 步长、可用系数和限幅规则属于 Profile 的兼容事实。

### 7.5 容量模型

执行写入前必须形成不可变 `CapacityEstimate`：

```text
Eligible coefficients
÷ 每 bit 副本数
− Control Channel 固定开销
− 对齐与交织开销
= 可用编码 bit
→ 扣除 ECC、AEAD Tag、Envelope 和签名
= 最大原始 Payload 容量
```

UI 同时展示：

- 图片原始可用系数；
- Control Channel 占用；
- 压缩前后 Payload 大小；
- 加密、ECC、交织和冗余开销；
- 最终余量或缺口。

容量不足必须在创建输出工作集和修改 DCT 系数前失败。不得截断 Payload，也不得自动降低用户选择的鲁棒性来
“勉强写入”。

### 7.6 输出与自检

- 默认输出 PNG；
- JPEG 输出必须使用明确质量设置且不覆盖源文件；
- 编码完成后重新从实际输出字节解码，使用正式提取器回读；
- 自检失败时不得显示成功，也不得自动发布目标文件；
- 保存采用同目录临时文件、flush 和原子替换/移动；
- 目标已存在时必须明确确认或选择新名称；
- 取消、失败和退出清理受本次操作拥有的临时文件，不删除源图或已完成文件。

## 8. 正式质量配置

用户只选择版本化配置，低层参数默认折叠：

| 配置 | QIM 强度 | Block 密度 | ECC | 副本 | 目标 |
| --- | --- | --- | --- | --- | --- |
| Stealth / 隐蔽 | 低 | 低 | 中 | 少 | 视觉变化最小，容量和鲁棒性较低 |
| Balanced / 均衡 | 中 | 中 | 中高 | 中 | V1 默认推荐配置 |
| Robust / 鲁棒 | 高 | 高 | 高 | 多 | 轻度重编码后恢复率优先 |

配置不是几个 UI 数字的临时集合。每个正式 Profile 必须冻结：

- 通道策略；
- Block 过滤规则；
- 中频坐标集合；
- QIM 步长和限幅；
- Control/Data ECC；
- 副本数和投票规则；
- 交织与映射版本；
- 支持的输出编码条件；
- 质量与鲁棒性验收阈值。

G8 以前 Profile 只能称为实验参数。G8 必须根据语料数据决定正式阈值，不能在本文中用未经实测的 PSNR、SSIM
或 JPEG 质量数字制造完成标准。

## 9. 检测、提取与验证状态模型

### 9.1 固定处理链

```text
图片解码与规范化
→ 8×8 DCT
→ 固定位置扫描 Control Channel
→ QIM 软判决与副本合并
→ Control ECC + CRC + 协议校验
→ 获取 Data Channel 计划
→ Data QIM 解调与副本合并
→ 去扰码、反交织和 Reed-Solomon 解码
→ 可选 AES-GCM 解密认证
→ 可选签名验证
→ 解压并恢复 Payload Envelope
→ 形成 ExtractionReport
```

### 9.2 用户可见状态

检测与提取不能压缩成一个 `bool Success`。至少区分：

- `NoSupportedWatermark`：未检测到受支持的控制头；
- `DetectedKeyRequired`：控制头有效，数据需要密码或密钥；
- `DetectedReady`：控制头有效，可以继续提取；
- `RecoveredWithCorrections`：ECC 修复后恢复成功；
- `RecoveredIntegrityValid`：Payload 完整性/AEAD 认证通过；
- `RecoveredSignatureValid`：签名通过且信任来源已识别；
- `UnsupportedVersionOrProfile`：识别到协议族但版本或配置不支持；
- `UnrecoverableDamage`：证据足以表明存在水印，但数据不足；
- `AuthenticationFailed`：错误密钥或数据被改变，无法可靠区分；
- `MalformedOrResourceRejected`：Header 非法或请求超出安全资源上限。

### 9.3 验证报告

`ExtractionReport` 至少包含：

- 协议版本和 Profile；
- 控制信道和数据信道平均/最低置信度；
- 发现的副本数、冲突数和投票结果摘要；
- ECC 块数、修复符号数和无法修复块数；
- 压缩、加密、签名和 Payload 类型；
- CRC、AEAD、摘要和签名分别的结果；
- 恢复的原始长度和导出建议；
- 不含密码、密钥、明文摘要和敏感文件内容的诊断信息。

没有原图时不能计算有参照的 PSNR/SSIM；检查 Document 只显示水印通道置信度和恢复健康度。用户可选提供原图时，
再调用 Compare 用例显示质量指标。

## 10. 界面与交互设计

### 10.1 水印写入 Document

```text
┌──────────────────────────────────────────────────────────────┐
│ 打开源图 | 配置：均衡 | 估算容量 | 写入 | 保存输出           │
├────────────────────────────────┬─────────────────────────────┤
│ 原图 / 输出 / 差异 / 频谱      │ Payload                     │
│                                │ 文本 / JSON / 文件           │
│                                │ 当前大小 / 最大容量          │
│          图片主预览            ├─────────────────────────────┤
│                                │ 安全                        │
│                                │ 不加密 / 密码保护 / 签名     │
│                                ├─────────────────────────────┤
│                                │ 质量配置与高级参数           │
├────────────────────────────────┴─────────────────────────────┤
│ 阶段 | 进度 | PSNR/SSIM | 回读自检 | 错误与恢复建议          │
└──────────────────────────────────────────────────────────────┘
```

交互规则：

- 拖放和文件选择进入同一加载命令；
- 更换图片、Payload 或 Profile 后使旧容量和输出结果失效；
- 写入期间禁用会改变计划的输入，保留取消；
- 密码使用显式显示/隐藏和确认输入，不提供“记住密码”；
- 容量不足显示分项开销和可执行建议，不自动降低安全或鲁棒配置；
- 默认不覆盖原图，危险保存操作明确显示目标；
- 写入成功仍需通过正式回读自检后才能显示“可用输出”。

### 10.2 提取与验证 Document

```text
┌──────────────────────────────────────────────────────────────┐
│ 打开图片 | 检测 | 输入密码 | 提取 | 导出 Payload             │
├────────────────────────────────┬─────────────────────────────┤
│ 图片 / 频谱 / 系数置信图       │ 检测结论                    │
│                                │ 协议 / Profile / 是否需密钥  │
│          检查主视图            ├─────────────────────────────┤
│                                │ Payload 预览                 │
│                                │ 文本 / JSON / Hex / 文件     │
│                                ├─────────────────────────────┤
│                                │ ECC / AEAD / 签名验证报告    │
├────────────────────────────────┴─────────────────────────────┤
│ 最终结论 | 置信度 | 修复量 | 错误与恢复建议                 │
└──────────────────────────────────────────────────────────────┘
```

交互规则：

- 打开图片后可自动执行轻量 Control 扫描；完整提取由用户启动；
- 只有 Header 表示需要密钥时才显示密码区；
- 文本和 JSON 预览有长度上限，超限 Payload 只提供摘要与导出；
- 未知二进制不得尝试作为文本完整渲染；
- 导出不根据不可信文件名直接写磁盘，文件名需清理并由用户确认；
- `AuthenticationFailed` 使用“密码错误或数据已改变”表述；
- 签名不存在时显示“未签名”，不能显示红色“签名失败”。

### 10.3 渐进披露与可访问性

- 首屏只显示源图、Payload、Profile、安全选项和主动作；
- QIM 步长、系数集合、ECC 比例和副本数默认折叠；
- 所有只用颜色表示的状态同时提供文字和图标；
- 键盘可完成打开、切换 Payload 类型、估算、执行、取消、保存和导出；
- 长中文路径、长文件名、窄 Dock 和 125%–200% 缩放不能遮挡主动作；
- 频谱与差异图是诊断辅助，屏幕阅读器使用等价摘要，不朗读全部系数。

## 11. Document 持久化与敏感数据

两个 Document 最终可实现 `IPersistablePluginDocument`，但快照是版本化作业配方，不是 ViewModel 序列化。

允许保存：

- 输入/输出文件引用和非敏感文件指纹；
- Payload 类型、外部 Payload 文件引用；
- Protocol/Profile 和非敏感参数；
- 当前工作阶段、最后成功动作和非敏感结果摘要；
- 用户明确选择保存的内联 Payload。

默认不保存：

- 密码、主密钥、派生密钥和私钥；
- 解密后的 Payload；
- 未经用户明确选择的内联明文；
- 输出图片二进制和完整频域工作集；
- 临时文件路径中的随机秘密或诊断转储。

恢复快照时若源文件消失、指纹变化或 Payload 文件变化，Document 进入明确的“需要重新选择/重新确认”状态，
不能把旧质量、自检或验证结果继续显示为当前事实。

G1 可以先使用非持久 Document 完成生命周期；持久化只有在 G6/G7 明确 schema、敏感边界和恢复失败语义后启用。

## 12. G0–G9 实施包

### G0：产品、协议与质量基线

目标：把产品承诺、非目标、威胁模型、正式格式和可测指标冻结为可执行基线。

实施：

- 确认正式名称为“频域隐式水印”，文案不使用“无法检测”；
- 冻结两个 Document、零 Tool 和稳定 ID；
- 冻结 V1 Y-only、8×8 DCT、中频、QIM、双信道和 Profile 方向；
- 确认密码保护默认策略、明文 Payload 快照默认策略和签名是否进入 V1；
- 建立最小/典型/大图片与 Payload 容量样本；
- 评审图像编解码、ECC、KDF/加密依赖的 API、许可证、AOT/发布和私有打包；
- 建立测试图片来源、许可证和禁止使用个人敏感图片的规则；
- 定义性能测试设备、最大像素/内存预算和 G8 数据记录格式。

门禁：所有未决产品项有书面结论；依赖可以锁定恢复并进入正式 ZIP；没有代码宣称未经验证的鲁棒性。

回滚：只删除基线草案和未使用依赖，不产生用户文件或协议兼容承诺。

### G1：两个 Document 与真实生命周期外壳

目标：删除模板示例语义，建立两个真实 Document 的注册、状态隔离和 Standalone 预览入口。

实施：

- 将 `MainDocument/MainView` 替换为 `WatermarkEmbedDocument/View` 和 `WatermarkInspectDocument/View`；
- 替换模板 `MainDocument`、命令和菜单 ID；
- Module 从同一事实源登记两个 Document；
- Standalone 扩展为最小贡献浏览器或显式切换两个真实 View；
- 每次打开创建独立 Scope，关闭取消并释放；
- 先使用假 Application 结果展示完整 UI 状态枚举，不伪装算法完成；
- 测试稳定 ID、Descriptor、实例隔离、取消、关闭和无 Host 内部依赖。

门禁：两个 Document 可在 Standalone 独立打开多个实例；模板命令和“示例文档”不再进入生产贡献。

回滚：恢复模板外壳不会影响用户数据，因为 G1 尚未写入正式 Frame。

### G2：Imaging 与 Frequency 公共 Domain

目标：以最小纯领域模型支持当前水印，并为后续频谱类图像工具留下真实复用点。

实施：

- 建立 Imaging/Frequency 值对象、维度和资源边界；
- 实现 RGB/YCbCr 转换与可逆误差测试；
- 实现 8×8 DCT/IDCT，验证基准向量和往返误差；
- 实现 Block Grid、边缘和 Alpha 排除规则；
- 形成频谱可视化只读投影，不依赖 Avalonia Bitmap；
- 使用池化或明确所有权的连续缓冲区，支持取消和释放；
- 不建立 FFT、DWT、滤波器注册中心或通用算法插件系统。

门禁：数值基准、往返误差、尺寸边界、Alpha、非 8 倍数图片、取消和内存所有权测试通过。

回滚：Watermarking 尚未依赖线格式，公共 Domain 可以整体回滚。

### G3：Frame、压缩、加密与安全

目标：冻结与图像载体分离的 Payload 协议，并完成安全的编码/解码往返。

实施：

- 定义 Control Header 和 Data Envelope 的精确线格式；
- 定义显式端序、长度上限、Algorithm ID 和未知版本行为；
- 实现只在实际缩小时采用的压缩；
- 实现 AES-256-GCM、AAD、随机 Salt/Nonce 和密钥分离；
- 按 G0 结论实现 Argon2id 或 PBKDF2；
- 实现 Frame 分块 Reed-Solomon、交织和反交织；
- 定义签名字段；若签名进入 V1，完成规范化签名与信任状态；
- 建立 golden vectors、截断、篡改、错误密码、资源放大和未知算法测试。

门禁：Frame 往返、跨进程 golden vectors、安全随机、AEAD 篡改拒绝、长度上限和敏感信息脱敏通过。

回滚：未发布前可删除协议；一旦 G9 发布 V1，旧 Protocol/Profile 读取器不得删除。

### G4：容量规划与嵌入引擎

目标：从 Frame 和图片形成确定性嵌入计划，生成通过正式回读的输出图片。

实施：

- 实现候选块分析、Control/Data 分区和容量估算；
- 实现 Profile、QIM 调制、位置映射和副本展开；
- 实现像素重建、限幅、Alpha 保留和输出编码；
- 默认 PNG，JPEG 按 G0 条件接入；
- 实现 PSNR/SSIM 与差异投影；
- 执行后调用正式提取器自检；
- 自检成功后才允许原子保存；
- 全链观察取消、进度和临时资源清理。

门禁：容量不足零修改、确定性计划、PNG 回读、源图不覆盖、Alpha 不变、自检失败不发布均有测试。

回滚：关闭写入入口，不影响 G2/G3 的纯领域与 Frame 测试。

### G5：检测、提取与验证引擎

目标：从未知图片安全检测 V1 Watermark，恢复 Payload 并输出可解释报告。

实施：

- 实现 Control Channel 扫描、软判决、投票、ECC 和 CRC；
- 实现 Data Channel 定位、解调、去扰码、反交织和 ECC；
- 接入解密、解压、Payload Envelope 和可选验签；
- 建立完整状态模型与 `ExtractionReport`；
- 对无水印图片控制误报；
- 对损坏、错误密码、未知版本和恶意 Header 安全失败；
- 不在失败日志中输出密文、明文、密码或完整路径。

门禁：原始输出、PNG 重编码、截断 Frame、bit 错误、错误密码、篡改、随机图片和资源攻击测试通过。

回滚：关闭提取入口；不得让写入 UI 继续宣称自检能力可用。

### G6：水印写入 Document

目标：形成从选择图片到安全保存的完整写入体验。

实施：

- 接入图片选择、拖放、Payload 文本/JSON/文件适配器；
- 接入容量分项、Profile、安全选项和高级参数；
- 接入原图/输出/差异/频谱预览；
- 接入进度、取消、错误和恢复建议；
- 完成密码输入、清理和不可持久化边界；
- 完成目标冲突确认和原子保存；
- 按第 11 节定义可选 Document 快照 schema；
- 若 SDK 支持，增加“在提取与验证中打开”的窄交接。

门禁：Headless UI、命令状态、迟到结果、快速换图、并发点击、关闭取消、脏状态和敏感快照测试通过。

回滚：隐藏写入贡献；保留已生成图片，不删除用户文件。

### G7：提取与验证 Document

目标：形成从打开图片到预览/导出 Payload 的完整检查体验。

实施：

- 打开后执行有界 Control 扫描并显示检测状态；
- 按 Header 动态显示密码输入和支持能力；
- 接入完整提取、取消、置信度、ECC 和认证报告；
- 按内容类型提供文本、JSON、Hex 摘要或二进制导出；
- 限制预览长度，清理不可信建议文件名；
- 可选加载原图并显示 PSNR/SSIM；
- 按第 11 节定义检查快照 schema；
- 明确无水印、损坏、认证失败和未知版本文案。

门禁：状态不可混淆、迟到结果不覆盖新图片、导出不越界、密码不保存、签名语义正确和键盘操作测试通过。

回滚：隐藏检查贡献，不改变任何图片和已导出的 Payload。

### G8：质量、鲁棒性、性能与安全门禁

目标：用可复现实验决定 V1 正式 Profile 和产品承诺。

实施：

- 建立照片、插画、渐变、纹理、低对比、高对比、透明图等授权语料；
- 为不同尺寸、Payload 大小和 Profile 记录 PSNR、SSIM、容量和耗时；
- 对 PNG 重存、JPEG 质量梯度、少量噪声执行提取矩阵；
- 测试随机无水印图片误报率；
- 测试局部 bit/块损坏与 ECC 修复边界；
- 测试大图片、取消、并发 Document、峰值内存和资源释放；
- 审计 KDF、Nonce、Key 分离、AEAD、日志和快照；
- 根据数据冻结正式 Profile；未达标配置降级为实验或删除。

门禁：所有正式质量声明均有语料、命令、原始结果和阈值；不得只展示最佳样本或用自编码图片代替真实输出。

回滚：移除未达标 Profile；Balanced 不达标时 V1 不得封板。

### G9：集成、打包与 V1 封板

目标：把 Domain、引擎、两个 Document、Standalone、测试、文档和正式插件包签署为同一 V1 基线。

实施：

- Standalone 使用 Module 唯一注册事实展示两个真实 Document；
- Debug/Release locked restore、零警告构建和全部测试；
- 生成确定性 ZIP，验证 manifest、私有依赖和无 Standalone/Tests；
- 部署到干净真实 Host，验证两个 Document 多实例、关闭、取消、保存和布局；
- 真实 Host 中执行写入→保存→提取→验证闭环；
- 更新 README、部署、测试、协议、隐私、安全和故障排除文档；
- 审计模板示例、调试入口、测试资产、临时文件和敏感日志；
- 记录 V1 兼容承诺、已知限制和后续 V2 同步标记方向。

门禁：构建、测试、质量矩阵、真实 Host、ZIP、资源释放、文档链接和安全检查全部通过。

回滚：卸载或回退整个插件版本，不修改用户原图；已生成的 V1 图片仍需由兼容读取器支持。

## 13. 预计代码与文档落点

### 13.1 生产代码

```text
src/ImageLabPlugin.Plugin/
  Constants/
    PluginIds.cs
  Domain/
    Imaging/
    Frequency/
    Watermarking/
  Application/
    Watermarking/
      EstimateCapacity/
      Embed/
      Inspect/
      Extract/
      Verify/
  Infrastructure/
    Imaging/
    Compression/
    Cryptography/
    ErrorCorrection/
    Persistence/
  Features/
    WatermarkEmbed/
    WatermarkInspect/
  Plugin/
    ImageLabPluginModule.cs
    ImageLabPluginServices.cs
```

实际目录按实现规模调整。不得为了逐字匹配计划制造空目录、一类型项目或无消费者接口，也不得把 Domain、图片编解码、
密码学和 ViewModel 重新塞入一个 `WatermarkService`。

### 13.2 测试

```text
tests/ImageLabPlugin.Tests/
  Architecture/
  Domain/Imaging/
  Domain/Frequency/
  Domain/Watermarking/
  Application/Embedding/
  Application/Extraction/
  Infrastructure/Frame/
  Infrastructure/Security/
  Features/WatermarkEmbed/
  Features/WatermarkInspect/
  Integration/
  TestAssets/
```

测试文件可以按职责合并，但测试名称必须能追溯到 G 包、协议版本和用户行为。图片资产必须记录来源、许可证、用途
和校验值；生成资产必须记录生成脚本或确定性参数。

### 13.3 协议与计划文档

```text
docs/
  design/
    frequency-watermark-v1-implementation-plan.md
  frequency-watermark/
    protocol-v1.md
    quality-profiles-v1.md
    security-and-threat-model.md
    test-assets.md
    troubleshooting.md
  plan-history/frequency-watermark/
    g0-*.md
    ...
    g9-*.md
```

## 14. 自动测试与验收矩阵

### 14.1 Domain 与数值正确性

- [x] RGB/YCbCr 的 Y-only 重建、未修改像素和 Alpha 边界受控；
- [x] 8×8 DCT/IDCT 往返误差受控；
- [x] 非 8 倍数尺寸、极小图片、透明块和非法尺寸安全处理；
- [x] Block/Coefficient/Mapping 选择跨运行确定；
- [x] QIM 对 0/1 的格点和软置信度正确；
- [x] PSNR/全局 SSIM 实现与输出门禁已经建立；发布前仍需扩大授权照片语料。

### 14.2 Frame 与安全

- [x] V1 golden vectors 跨运行一致；
- [x] 压缩仅在结果更小时启用；
- [x] Salt/Nonce 随机且不复用；
- [x] Encryption/Mapping Key 分离；
- [x] 错误密码、密文或 AAD 篡改导致认证失败；
- [x] 截断、超长、未知版本、未知算法和资源放大安全拒绝；
- [x] 密码、密钥和明文不进入 Document 快照，敏感短生命周期缓冲区主动清理；
- [x] V1 明确不支持签名，报告固定为 `NotSigned`，带 Signed Flag 的 Frame 被拒绝。

### 14.3 嵌入、提取与质量

- [x] 容量不足在像素修改前失败；
- [x] PNG 输出由正式读取器完整恢复；
- [x] `Robust + JPEG 100` 直接输出和一次 JPEG 95 重编码通过开发回归矩阵；
- [x] ECC 能在声明边界内修复并报告修复量；
- [x] 超出修复边界不返回错误 Payload；
- [x] 确定性随机普通图不被识别为有效水印；发布前仍需扩大授权无水印语料；
- [x] Alpha、尺寸、透明块、非 8 倍边缘和源对象不被意外改变；
- [x] 输出实际字节必须由正式提取器自检，失败时用例不返回可保存结果；
- [x] 三个开发 Profile 已通过内存量化往返；正式市场质量承诺留待发布语料封板。

### 14.4 Document、UI 与生命周期

- [x] 两种 Document 同类型多实例状态隔离；
- [x] 快速重复操作会取消旧工作，迟到结果在提交前再次观察取消；
- [x] 执行状态与取消生命周期可观察；
- [x] 关闭 Document 取消工作并释放 Scope、图片、输出和敏感缓冲区；
- [x] 文件选择、保存和导出进入端口与相同应用用例；拖放未作为 V1 必要入口；
- [x] 保存由 Host 文件选择器确定目标，并使用同目录原子替换；
- [x] Headless 环境可加载两个真实 View，标准 Avalonia 控件支持默认键盘焦点导航；
- [x] Standalone 使用 Module 的注册与服务入口；真实 Host 验收按要求延期。

### 14.5 性能与资源

- [x] 编码文件 64 MiB 上限在整体读取前拒绝，16,000,000 像素上限在领域缓冲区分配前拒绝；
- [ ] 发布设备上的峰值内存基准按要求延期；
- [x] 并发 Document Scope 不共享可变 UI 或图像工作集；
- [x] DCT 块循环、编解码和应用用例观察取消；
- [x] 临时文件、流、Bitmap、密码派生密钥和 Payload 缓冲区在正常生命周期内释放；
- [ ] 发布前仍需执行长时间多轮泄漏观测。

### 14.6 回归命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Release --no-build --no-restore
dotnet msbuild src/ImageLabPlugin.Plugin/ImageLabPlugin.Plugin.csproj `
  -t:BuildManagedPluginPackage -p:Configuration=Release
```

正式封板时还必须按部署文档执行干净插件目录和真实 Host 验收。不得通过删除测试、降低质量阈值、只测试同一内存
工作集或把正式 Adapter 替换为固定成功实现通过门禁。

## 15. 人工验收场景

### 15.1 水印写入

1. 打开普通 JPEG 照片，输入短文本，选择 Balanced，检查容量分项并生成 PNG；
2. 比较原图、输出、差异和频谱，确认没有明显视觉异常；
3. 使用接近容量上限的文件 Payload，确认成功或在修改前准确拒绝；
4. 输入超过容量的 Payload，确认不会截断、降级 Profile 或覆盖原图；
5. 开启密码保护，确认密码不出现在标题、日志、快照或错误文本；
6. 保存到已存在目标，确认明确展示覆盖对象；
7. 写入过程中取消或关闭 Document，确认无最终文件和临时文件遗留；
8. 同时打开两个写入 Document，确认图片、Payload、进度和结果互不覆盖。

### 15.2 提取与验证

1. 打开刚生成的 PNG，确认识别协议、恢复原文并通过完整性检查；
2. 打开加密图片，确认先显示“需要密码”，正确密码恢复成功；
3. 输入错误密码，确认显示“密码错误或数据已改变”，不泄漏明文；
4. 打开普通无水印图片，确认不会误报为有效 Payload；
5. 打开轻度 JPEG 重编码图片，确认符合对应 Profile 的 G8 承诺；
6. 打开超出 ECC 边界的损坏图片，确认不会输出看似成功的错误数据；
7. 导出二进制 Payload，确认文件名清理、路径确认和内容精确；
8. 同时打开多个检查 Document 并快速换图，确认迟到结果不串实例。

### 15.3 Host 与打包

1. 在 Standalone 打开两个 Document 的多个实例并逐个关闭；
2. 部署 Release 插件到干净 Host，确认两个稳定入口和 Descriptor 正确；
3. 在真实 Host 完成写入→保存→新建检查 Document→提取→导出闭环；
4. 关闭正在执行的 Document，确认取消、Scope 和图片资源释放；
5. 检查 ZIP 只含正式插件和允许的私有依赖，不含 Standalone、Tests、测试图片或开发密钥；
6. 重复构建验证 manifest、文件清单和 ZIP 确定性。

## 16. 兼容、迁移与回滚

### 16.1 稳定身份与线格式

- Plugin ID `myavalonia.plugin.image.lab` 保持不变；
- G1 冻结两个 Document ID 后不得因类名、显示名或目录调整而改变；
- 模板 `document.main` 在首次产品发布前删除，不建立无价值兼容别名；
- Protocol V1 和正式 Profile 一经 G9 发布，读取兼容即成为长期承诺；
- 新算法使用新 Algorithm/Profile ID，不修改旧 ID 的含义；
- Document 快照 schema 与 Watermark Frame version 独立演进；
- 旧快照不能恢复秘密，缺失外部文件时进入可理解的降级状态。

### 16.2 功能回滚顺序

若 V1 集成出现问题，按以下顺序缩小：

1. 关闭签名；
2. 关闭实验 Cb/Cr 或 JPEG 输出；
3. 只保留 Balanced 正式 Profile；
4. 关闭 Document 持久化，保留会话级使用；
5. 关闭写入入口，保留对已发布 V1 图片的读取；
6. 若读取器存在安全缺陷，禁用受影响版本并显示明确安全错误，不尝试猜测解析；
7. 整体卸载插件，但不删除用户原图、输出图或已导出 Payload。

回滚不得使用破坏式 Git 命令、批量删除用户图片、覆盖源图或清空用户选择目录。

### 16.3 不可普通回退的安全约束

以下约束完成后不能作为普通功能开关关闭：

- 容量和资源上限；
- AEAD 认证失败拒绝输出明文；
- Salt/Nonce 随机和密钥分离；
- 输出自检和原子发布；
- 密码、密钥、明文与私钥脱敏；
- 不可信长度、版本、算法和导出文件名验证；
- 默认不覆盖源图。

## 17. 实施纪律

1. 严格按 G0→G9 推进；前一包门禁未过不得把后一包标记完成；
2. 每个 G 包形成独立 plan-history，记录实际数据而不是复制本文；
3. Frame 未经 G3 golden vectors 冻结，不进入 UI 和正式图片资产；
4. 引擎未形成无 UI 完整往返，不用 ViewModel 拼装算法步骤；
5. View code-behind 只转发拖放、文件选择和视觉手势，不决定容量、安全或协议合法性；
6. 不建立笼统 `Common`、万能 `WatermarkService`、根 ServiceProvider 或字符串算法路由；
7. 不把测试随机源、固定密钥、开发密码或演示 Payload 编入正式插件；
8. 不把扰码、Seed、CRC 或 SHA-256 描述为加密或签名；
9. 不用单一图片、单一编码器或只在内存中回读证明鲁棒性；
10. 自动测试默认不访问网络、不读取用户图片目录、不写正式插件用户数据；
11. 不覆盖与本计划无关的已有工作区修改；
12. 任一质量或安全结论没有测试资产、原始结果和复现命令时，不得写入产品文案；
13. Standalone 通过不等于 Host 通过，Debug 通过不等于 Release ZIP 通过；
14. G9 之前所有 Profile 和协议均为开发中，不能向外承诺兼容。

## 18. V1 最终封板检查清单

全部满足才允许标记 V1 完成：

1. [ ] 产品名称、非目标、威胁模型和质量口径已冻结；
2. [ ] 两个 Document、零 Tool 的注册和生命周期符合 Host 约束；
3. [ ] Imaging/Frequency Domain 不依赖 UI、文件系统或具体编解码器；
4. [ ] Watermarking Domain 没有泄漏密码库和 Avalonia 类型；
5. [ ] Protocol V1、Profile 和 golden vectors 完整；
6. [ ] 容量不足在修改像素前失败；
7. [ ] PNG 正式输出通过真实字节回读；
8. [ ] JPEG 能力只按 G8 实测结论开放；
9. [ ] ECC、AEAD、摘要和签名状态没有混淆；
10. [ ] 错误密码与篡改使用诚实的合并错误语义；
11. [ ] 密码、密钥、明文和私钥不进入日志、快照或插件包；
12. [ ] 正式 Profile 达到冻结的质量、恢复率和误报门禁；
13. [ ] 大图片、取消、并发实例和多轮资源释放通过；
14. [ ] 水印写入和提取验证两个 UI 闭环可由键盘完成；
15. [ ] Standalone 使用 Module 的唯一注册事实，不复制业务实现；
16. [ ] Debug/Release locked restore、零警告构建和全部测试通过；
17. [ ] Release ZIP 确定、依赖完整且不含 Standalone/Tests/测试密钥；
18. [ ] 干净真实 Host 完成多实例、关闭、保存和端到端闭环；
19. [ ] G0–G9 都有实际 plan-history、测试数据、偏差和回滚记录；
20. [ ] README、协议、安全、质量、测试资产和故障排除文档链接有效。

任一项未完成时，只能标记为“开发中”或“实验性集成”，不能宣称 V1 已封板。不得通过降低质量阈值、隐藏
认证失败、截断 Payload、关闭输出自检或只保留最佳样本来宣布完成。
