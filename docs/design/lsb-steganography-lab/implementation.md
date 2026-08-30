# ImageLabPlugin V1 LSB 隐写与统计实验实施计划

> 计划状态：开发实现与本地自动门禁完成；发布阶段延期<br>
> 基线日期：2026-08-30<br>
> 产品名称：LSB Steganography Lab／LSB 隐写与统计实验<br>
> 技术基线：.NET 10、C# 14、Avalonia 12.1、Managed Plugin SDK 3.3<br>
> 起始自动基线：2026-08-30 实际复跑 locked restore；Debug/Release build 均零警告、零错误；两配置均 191/191 通过、零跳过<br>
> 核心路线：独立像素域 LSB Frame + 顺序/确定性伪随机槽位 + PNG 无损输出 + 变化与位平面联动 + 卡方/位分布/邻接统计 + 受控脆弱性实验<br>
> 实施原则：SOLID 是首要规定；设计模式只用于真实替换点；中文注释详细解释数值、所有权和取舍；不使用 AIFLOW；Windows CI 与发布门禁延期

| 实施包 | 当前状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 已完成 | 冻结产品边界、教学措辞、Frame、槽位、统计和资源协议 | `history/g0-product-protocol-and-statistics-baseline.md` |
| G1 | 已完成 | 建立载荷、Frame、容量、位序和 Golden Vector | `history/g1-frame-and-capacity.md` |
| G2 | 已完成 | 完成通道策略、顺序槽位和确定性伪随机槽位 | `history/g2-slot-layout-and-placement.md` |
| G3 | 已完成 | 完成 LSB 写入、提取、CRC、自检和像素变化事实 | `history/g3-embedding-and-extraction.md` |
| G4 | 已完成 | 完成位置图、位分布、PoV 卡方和邻接统计 | `history/g4-visualization-and-statistics.md` |
| G5 | 已完成 | 复用受控扰动完成重编码、缩放和滤波脆弱性验证 | `history/g5-fragility-experiments.md` |
| G6 | 已完成 | 建立窄用例、Session、报告、取消和资源边界 | `history/g6-application-session-and-report.md` |
| G7 | 已完成 | 完成第八个 Persistable Document、快照和多 Scope 隔离 | `history/g7-document-lifecycle.md` |
| G8 | 已完成 | 完成教学型联动 UI、无障碍和 Headless View 门禁 | `history/g8-ui-and-explanation.md` |
| G9 | 已完成 | 完成本地双配置门禁、专用文档和开发阶段封板 | `history/g9-local-sealing.md` |

本文定义 ImageLab 的下一项能力和第八个 Persistable Document。它只研究用户显式提供图片上的像素域 LSB replacement，
让用户看到“写了哪些槽位、实际改了哪些像素、统计量怎样变化、经过普通图像处理后为什么容易失效”。它不是安全通信产品，
不提供不可检测、匿名、抗取证或鲁棒传输承诺。

本能力必须与现有 DCT-QIM 频域隐式水印保持产品、协议、代码和结论四重隔离。LSB Frame 不使用水印 V1 Magic、Header、
Profile、Control/Data Channel、DCT 映射、Reed-Solomon、AES-GCM 或 PBKDF2；现有水印提取器也不得尝试识别 LSB Frame。

本文是实施阶段的唯一总计划。每个 G 包完成后必须新增对应历史记录，写明实际修改、测试证据、偏差、性能、遗留风险和回滚方式。
截至 2026-08-30，G0–G8 已有生产代码与新增测试证据；起始 191 项仍只作为回归基线，不冒充 LSB 新能力证据。

## 1. V1 用户闭环与固定实施顺序

### 1.1 用户闭环

```text
显式选择一张 PNG 或 JPEG 图片作为实验载体
    ↓
解码 RGBA8888，显示尺寸、不透明像素数和理论/受限容量
    ↓
选择文本或不超过 64 KiB 的二进制载荷
    ↓
选择 bit 0 或 bit 1、R/G/B/RGB 通道策略、顺序或伪随机写入
    ↓
预检独立 LSB Frame、所需槽位、容量、预期最大像素改变量和风险提示
    ↓
执行写入并立即从内存结果回读，显示 Header/CRC/载荷状态
    ↓
联动观察写入位置、实际变化、位平面、局部热力图和像素探针
    ↓
比较载体与结果的位分布、PoV 卡方、邻接统计、PSNR 和变化计数
    ↓
按需执行 JPEG 重编码、缩放往返或滤波，并验证 Frame、CRC 与原始 BER
    ↓
原子导出无损 PNG；按需导出不含载荷、密钥或绝对路径的实验报告
```

### 1.2 固定实施顺序

1. G0 先冻结教学边界、Frame 字节布局、bit 顺序、通道顺序、透明像素、统计口径和失败状态；
2. G1 用手算 Frame 与容量 Golden 证明协议，不写 UI；
3. G2 分别证明顺序与伪随机槽位无重复、可复现、可逆和资源有界；
4. G3 完成纯领域写入/提取和编码后 PNG 回读，自检不通过时禁止导出；
5. G4 在相同样本范围上比较 cover/stego，统计层不输出“安全/不可检测”结论；
6. G5 只接入 JPEG、缩放往返和滤波三类受控实验，不复制 Robustness Lab；
7. G6 用窄用例管理 Session、报告、取消和敏感数据，不让 Document 持有算法；
8. G7/G8 最后接入 Document、快照、界面、无障碍和 Standalone；
9. G9 执行本地 locked restore、Debug/Release warn-as-error build/test 并同步专用文档，不执行发布门禁。

### 1.3 V1 决策摘要

| 主题 | V1 决策 |
| --- | --- |
| 输入 | 一张用户显式选择的 PNG/JPEG；沿用 64 MiB 编码、16,000,000 像素上限 |
| 输出 | 只导出 PNG；JPEG 仅作为脆弱性扰动，不是隐写结果格式 |
| 载荷 | UTF-8 文本或二进制；原始载荷最多 65,536 字节 |
| Frame | 独立 `ILSB` V1，20 字节固定 Header，Payload CRC32，不压缩、不加密、不纠错 |
| 通道 | `R`、`G`、`B` 或固定 `R→G→B` 的 `RGB`；不写 Alpha、Y、Cb、Cr |
| 位平面 | bit 0 或 bit 1；一次实验只选择一个位平面 |
| 可用像素 | Alpha=255 的像素；透明/半透明像素不作为槽位且逐字节不改 |
| 写入策略 | 行优先顺序写入；或基于显式 64 位 seed 的确定性伪随机无重复写入 |
| seed 语义 | 复现实验参数，不是密码、密钥或安全证明，可持久化但必须显式提示 |
| bit 顺序 | Frame 内 byte 按文件顺序；每个 byte 从 bit 7 到 bit 0 写入 |
| 统计 | 变化计数、位分布/熵、PoV 卡方及 p 值、水平/垂直邻接四格和转移率 |
| 脆弱性 | JPEG、缩放往返、Gaussian/Median 滤波的有限预设；不提供任意攻击 DAG |
| 集成 | 第八个多实例 Persistable Document；零 Tool、零 Workflow Action、零 Workbench Command |
| 结论 | 只报告观测事实；统计结果既不能证明存在隐写，也不能证明不可检测 |

## 2. 当前工程基线与可复用能力

### 2.1 已有事实

当前仓库已经具备：

- 一个真实 `ImageLabPlugin.Plugin` 插件程序集、一个复用真实 Module/DI 的 Standalone 和一个 xUnit/Headless 测试项目；
- 七个 Persistable Document、独立 Scope、`IDocumentLifetime`、轻量快照、generation、取消和 Bitmap 释放惯例；
- 自有未预乘 RGBA8888 `PixelImage`、`ImageSize`、16,000,000 像素与 64 MiB 编码输入上限；
- 正式 PNG/JPEG 解码、PNG 编码、图片/载荷/报告文件对话框与 `IAtomicFileWriter`；
- `Domain.BitPlanes` 的五通道抽取、`BytePlane`、`BitMask8`、八位统计、像素探针和有界预览坐标；
- `Domain.Comparison` 的 PSNR、SSIM、RGB/Alpha 误差、局部网格和有界差异投影；
- `Domain.Robustness` 的 JPEG、缩放、Gaussian Blur、Median Blur 等不修改输入的单责扰动 Strategy；
- 稳定实验随机性与密码学随机源分离的既有设计经验；
- 2026-08-30 本计划编写前实际复跑：locked restore 成功，Debug/Release 构建零警告零错误，两配置 191/191、零跳过；
- 当前明确不使用 AIFLOW，Windows CI、ZIP、真实 Host 与发布验收均延期。

### 2.2 复用规则

- 直接复用 `PixelImage`、`ImageSize`、`IImageCodec`、`IAtomicFileWriter`、文件对话框和图片质量计算；
- 复用 `BitMask8` 的位编号事实和位平面投影思路，但 LSB 写入拥有独立 `Domain.Steganography`，不能把可变写入塞进只读观察器；
- 复用现有扰动实现，而不是复制 JPEG、缩放或滤波公式；新用例用显式 allowlist 暴露有限预设；
- 把纯计算的 CRC32 小原语从水印专属目录提升到协议中立位置时，必须先固定现有水印向量并让两个协议共同复用；只共享校验算法，不共享 Frame、Magic 或读取状态；
- 若位平面观察器已有类型不能在不改变原语义的前提下复用，先保留局部实现，不为“复用率”扩大公共接口；
- 不复用 `FrequencyWatermarkCarrier`、`WatermarkFrameProtocol`、`DeterministicPermutation` 或水印密码学端口；名称相似不代表协议相同；
- 不修改现有 191 项断言阈值；共享原语调整前先增加兼容回归。

### 2.3 需要新增的能力

- 独立、版本化、有限长度的 LSB Frame 与结构化读取状态；
- 通道策略、可用像素规则、逻辑槽位布局和容量预检；
- 顺序与确定性伪随机两种可逆槽位顺序；
- 不原地修改输入的 LSB replacement 写入器和只读提取器；
- 写入位置、实际变化、Frame 区域、位平面和局部变化热力图；
- PoV 卡方、位分布、邻接统计及 cover/stego 差值；
- 受控脆弱性运行、Frame/CRC/BER 观察和失败分类；
- 实验 Session、报告、Document、View、组合根、Standalone 入口及专用测试/文档。

## 3. 产品范围、教学边界与非目标

### 3.1 V1 必须完成

- 文本与二进制载荷、65,536 字节硬上限和容量不足的运行前阻断；
- R/G/B 单通道和 RGB 固定轮转策略；
- bit 0 与 bit 1 的单平面 LSB replacement；
- 顺序写入与显式 seed 的确定性伪随机写入；
- 完整 Frame 回读、Header CRC、Payload CRC、载荷类型和精确长度验证；
- 源图/结果图、写入位置图、实际变化图、bit 前后图、16×16 局部变化网格和像素探针；
- cover/stego 的位分布、PoV 卡方、邻接统计、变化数、变化率、MSE/PSNR；
- JPEG、缩放往返、Gaussian Blur 和 Median Blur 的有限脆弱性实验；
- 原始 Frame bit 对比得到的 BER，以及 Header/CRC/载荷恢复状态；
- PNG 原子导出与编码后真实回读；JSON/CSV 报告不包含载荷、绝对路径或伪装安全结论；
- 多实例隔离、取消、迟到结果拒绝、快照安全恢复、资源释放、Headless View 和本地双配置门禁；
- 界面显著显示“教学与实验用途”“不保证不可检测”“不是频域鲁棒水印”。

### 3.2 明确不实现

- 不可检测、安全通信、匿名、反取证、规避审查或绕过检测承诺；
- 密码、加密、签名、密钥派生、隐写密钥管理、Payload 压缩或 Reed-Solomon/ECC；
- 自动判断“存在隐写/不存在隐写”、可疑分数、取证结论或机器学习分类；
- RS analysis、SPA、样本对分析、富模型、深度学习隐写分析；这些必须有独立算法与数据集门禁后再设计；
- Alpha、Y、Cb/Cr、调色板索引、16 位/HDR/RAW 或多位平面同时写入；
- 外部隐写软件协议兼容、自动猜测通道/bit/seed、密码爆破或目录扫描；
- 任意大文件、拆分多图、批处理、网络传输、剪贴板监控或隐藏执行；
- 任意攻击链、参数网格、自动攻击搜索；完整扫描继续属于 Robustness Lab；
- JPEG 隐写输出、DCT 系数隐写或对现有频域水印协议的任何修改；
- AIFLOW、通用 DAG、Workflow Action、Workbench Command、反射发现、脚本运行时；
- Windows CI、ZIP、真实 Host、安装/卸载和发布完成声明。

### 3.3 术语与安全措辞

- “写入”表示替换指定 RGB 字节的一个低位，不表示加密或安全保存；
- “伪随机”表示由 seed 可复现的槽位顺序，不表示攻击者无法推测；
- “CRC 通过”只表示无意损坏下的完整性，不表示来源可信、未被恶意篡改或经过认证；
- “p 值”描述当前统计模型下的观测，不是“图片含隐写的概率”；
- “未检测到统计变化”不等于不可检测；“统计变化明显”也不等于已经证明存在隐写；
- “脆弱性成功/失败”只针对本工具的 Frame、当前参数和当前扰动，不外推到其他协议。

## 4. 独立 LSB Frame V1

### 4.1 固定字节布局

Frame 使用固定 20 字节 Header，所有多字节整数按 little-endian 编码：

| 偏移 | 长度 | 字段 | 规则 |
| ---: | ---: | --- | --- |
| 0 | 4 | Magic | ASCII `ILSB`，不得与水印 V1 Magic 相同 |
| 4 | 1 | Version | 固定 `1` |
| 5 | 1 | PayloadKind | `1=Utf8Text`，`2=Binary` |
| 6 | 2 | Flags | V1 固定 `0`；未知 bit 必须拒绝 |
| 8 | 4 | PayloadLength | `0..65,536` |
| 12 | 4 | PayloadCrc32 | 对原始 Payload 字节计算 |
| 16 | 4 | HeaderCrc32 | 对偏移 0..15 计算 |

完整 Frame 为 `Header || Payload`。V1 不把通道、bit、位置策略或 seed 写入 Header，因为读取 Header 之前就必须知道这些映射参数；
它们属于实验配方并在界面、快照和报告中显式记录。提取参数不匹配应表现为 `MagicMismatch`、`HeaderCrcMismatch` 或
`InsufficientSlots`，不能回退为猜测。

### 4.2 位序与写入公式

Frame byte 按数组顺序处理，每个 byte 从 bit 7 到 bit 0 写入。对目标通道字节 `v`、载荷 bit `m∈{0,1}`、
位平面 `b∈{0,1}`：

```text
mask     = 1 << b
embedded = (v & ~mask) | (m << b)
delta    = embedded - v       // 只能为 -2^b、0、+2^b
```

读取使用 `(v >> b) & 1`。所有位序、端序和 CRC 输入范围必须用固定字节 Golden 证明，禁止依赖 `BitConverter` 的平台端序。

### 4.3 读取状态

读取结果至少区分：

- `Success`：Header 和 Payload CRC 均通过；
- `InsufficientSlots`：连固定 Header 都读不全，或声明长度超过剩余槽位；
- `MagicMismatch`、`UnsupportedVersion`、`UnsupportedFlags`、`UnknownPayloadKind`；
- `HeaderCrcMismatch`：不得继续信任长度字段分配大缓冲；
- `LengthOutOfRange`：长度大于 65,536 或容量；
- `PayloadCrcMismatch`：可报告实际读取长度和 BER，但不得把字节标为成功载荷；
- `Cancelled` 与内部错误由应用层单独表达，不混入领域协议状态。

文本只在 CRC 通过后按严格 UTF-8 解码；非法 UTF-8 返回结构化 `InvalidUtf8`，不使用替换字符伪装成功。二进制提取结果只有
用户显式选择导出时才写盘。

## 5. 槽位、容量与通道策略

### 5.1 可用像素

V1 只把 `Alpha == 255` 的像素加入可用集合。透明与半透明像素可能在重采样、合成或编解码过程中改变隐藏 RGB，且写入不可见
颜色容易误导实验，因此本轮明确跳过。跳过像素的 RGBA 四字节必须保持不变。

图像扫描按 `y=0..height-1`、`x=0..width-1` 行优先。通道策略固定：

- `Red`：每个可用像素一个 R 槽；
- `Green`：每个可用像素一个 G 槽；
- `Blue`：每个可用像素一个 B 槽；
- `RgbRoundRobin`：每个可用像素依次产生 R、G、B 三个槽。

RGB 的逻辑槽位顺序必须是 `pixel0.R, pixel0.G, pixel0.B, pixel1.R...`，不能受 UI 语言、枚举整数或 DI 登记顺序影响。

### 5.2 容量

```text
eligibleSlots    = opaquePixelCount × selectedChannelCount
frameCapacity    = floor(eligibleSlots / 8)
payloadCapacity  = max(0, frameCapacity - 20)
effectiveLimit   = min(payloadCapacity, 65,536)
requiredBits     = checked((20 + payloadLength) × 8)
bitsPerPixel     = requiredBits / totalPixelCount
bitsPerSlot      = requiredBits / eligibleSlots
```

容量计算使用 `long` 和 checked 运算；UI 同时显示理论载荷容量、V1 上限、实际 Frame 开销、使用率、bits/pixel 和 bits/slot。
空 Payload 是合法教学案例，但仍写入 Header。容量不足时必须在复制图片或生成位置序列前阻断，不自动截断 Payload、切换通道或降级策略。

### 5.3 顺序位置

顺序策略取逻辑槽位 `[0, requiredBits)`。它适合观察局部集中修改：位置图应明显显示从左上开始的覆盖区域，统计面板应同时显示
全图和已写入范围，避免全图统计稀释局部变化。

### 5.4 确定性伪随机位置

伪随机策略从全部 `eligibleSlots` 中无放回选择 `requiredBits` 个槽。V1 使用版本化 `SplitMix64-v1` 产生 64 位值，并通过
拒绝采样避免取模偏差；使用稀疏 partial Fisher-Yates，仅为已选择前缀保存交换表和最终 `int[]` 位置，不分配完整槽位数组。
当前 16,000,000 像素和三通道上限产生的 48,000,000 个逻辑槽仍在 `int` 正范围内；容量运算本身继续使用 `long`。

约束：

- 相同图片像素、通道策略、bit、seed 和 Frame 必须得到逐槽一致的顺序；
- 不同执行顺序、取消重试或 UI 刷新不得改变结果；
- 槽位不得重复或越界；写入与提取必须由同一位置实现驱动，不能维护两份算法；
- seed 默认可由安全随机源生成以减少偶然重复，但一旦生成只是公开实验参数；
- 不使用 `System.Random`、`Random.Shared` 或水印 `IRandomSource`；
- 更换 PRNG、无偏采样或槽位序列属于协议兼容变化，必须新增 PlacementVersion，不能静默修改 V1。

## 6. 写入、提取与变化事实

### 6.1 写入器

`LsbEmbeddingEngine` 只接收 `PixelImage`、已经编码的 Frame、经过验证的槽位配方和取消令牌，返回新 `PixelImage` 与不可变事实；
它不得读取文件、编码 PNG、生成 Bitmap 或修改输入缓冲。

返回事实至少包括：

- Frame/Header/Payload bit 数和使用率；
- 写入槽位数、实际改变槽位数、未改变槽位数；
- `-2^b/0/+2^b` 计数、每通道改变数和每 bit 区域改变数；
- 紧凑的槽位索引序列、Header/Payload 分界和 16×16 局部变化网格；
- 原图与结果的 MSE、PSNR；无变化时 PSNR 为正无穷并用结构化值表达；
- 源图哈希/结果图哈希只能用于内存会话关联，不写入含用户路径的日志。

### 6.2 提取器与立即自检

`LsbExtractionEngine` 使用同一 `LsbSlotLayout` 和 `ILsbSlotOrder` 依次读取 Header，再在 Header 合法且长度受限后读取 Payload。
写入用例必须在以下两个边界自检：

1. 从内存中的 stego `PixelImage` 立即提取，必须逐字节一致；
2. PNG 编码后从字节真实解码并提取，必须逐字节一致，尺寸与 RGBA 也必须符合 PNG 无损预期。

任一自检失败都不得显示“写入成功”或允许发布该输出。JPEG 不进入正常导出路径。

### 6.3 像素探针与位置可视化

探针输入原图坐标，返回原始/结果 RGBA、是否可用、是否属于 Frame、槽位序号、通道、bit、消息 bit、写入前后目标 bit、
字节差值和局部统计。未使用像素必须明确显示 `NotSelected`，不能用空值与错误混淆。

有界预览最大边 1024，并共用位平面观察器已经证明的原图坐标映射思想。位置图至少区分：未使用、Header 槽位、Payload 槽位、
选中但值未变化、实际发生变化；同时提供图例和等价计数表，不能只靠颜色传达。

## 7. 统计实验协议

### 7.1 样本范围

每个统计结果都必须带 `Scope`：

- `EligibleImage`：所选通道中所有 Alpha=255 样本；
- `SelectedSlots`：本次 Frame 实际选择的槽位；
- `SequentialPrefix`：只对顺序策略提供，覆盖从开头到最后一个写入槽位的连续区域。

cover 与 stego 必须在完全相同的通道、bit、坐标和 Scope 上比较。不同样本数不允许相减或显示“变化”。RGB 策略先报告 R/G/B 分项，
再报告带样本数的聚合；不能把三个通道先平均后伪装成一张灰度图。

### 7.2 位分布与熵

对选定位平面统计 `zeroCount`、`oneCount`、`oneRatio` 和二元熵：

```text
p  = oneCount / sampleCount
H2 = -p log2(p) - (1-p) log2(1-p)
```

`p=0/1` 的对应项按 0 处理。报告 cover、stego 和 delta。接近 0.5 或熵接近 1 不是隐写证据，也不是安全证据；UI 必须显示这条限制。

### 7.3 Pair of Values 卡方

对目标 bit `b`，把仅在该 bit 上不同的字节组成 PoV：`partner(v)=v xor (1<<b)`；只在该 bit 为 0 的值上枚举一次。
对每对计数 `a,bCount`，期望值 `e=(a+bCount)/2`，`e=0` 的对跳过：

```text
χ² = Σ ((a-e)²/e + (bCount-e)²/e)
df = 非空 PoV 对数量
p  = Q(df/2, χ²/2)
```

`Q` 是正规化上不完全 Gamma。实现需要小参数级数、大参数连分式、迭代上限、收敛容差和非有限数防护，并用独立参考值 Golden 验证。
界面同时显示 `χ²`、`df`、`χ²/df`、`p`、样本数和 Scope，不把单个阈值转换成“检测到隐写”。

该指标借鉴 Westfeld/Pfitzmann 的 PoV 卡方思路，但本工具只做受控前后比较；自然图像内容、既有 JPEG 痕迹、样本范围和嵌入率都会影响结果。

### 7.4 邻接统计

对同一通道和 bit，只统计 Alpha=255 且属于当前 Scope 的水平右邻与垂直下邻，分别累计 `00/01/10/11`：

```text
transitionRate = (count01 + count10) / validPairCount
equalRate      = (count00 + count11) / validPairCount
```

水平、垂直和合计必须分开保存，防止方向纹理被平均隐藏。报告 cover/stego/delta、有效邻接对数；没有有效对时返回 `N/A`，不能返回 0。

### 7.5 统计结论边界

- 卡方、bit balance 和邻接统计是不同观察角度，不合成为“隐写概率”；
- 统计只说明当前样本与模型，不替代 RS/SPA 或经标注数据集校准的检测器；
- 顺序与伪随机策略的对比必须使用同一载体、Frame、通道、bit 和 seed/位置事实；
- 报告可写“变化增大/减小/接近”，不得写“安全”“不可检测”“通过检测”；
- 原始数值和样本范围必须可导出，以便复核。

## 8. 脆弱性实验

### 8.1 V1 受控预设

本 Document 只提供单一预设或固定往返，不开放任意链编辑器：

| 类别 | V1 预设 | 说明 |
| --- | --- | --- |
| JPEG | Quality 95、80、60 | 复用正式 codec；仅全不透明图片可运行 |
| 缩放往返 | 75%→原尺寸、50%→原尺寸 | 复用双线性 Scale；两个步骤显式可见；V1 仅允许全不透明图片 |
| Gaussian Blur | 既有合法的轻/中两档参数 | 固定核/σ，沿用 Robustness 算子语义 |
| Median Blur | 3×3 | 复用既有算子 |

如现有算子的稳定参数与本表冲突，以 G0 对当前实现的事实审计为准，并在专用 `guide.md` 写出实际值；不得复制公式造出第二套算子。

### 8.2 观察结果

每次脆弱性运行从同一内存 stego 基线开始，返回：

- 扰动 ID、稳定参数、输入/输出尺寸和是否完成缩放往返；
- Header 状态、Payload CRC、载荷是否逐字节恢复；
- 使用原始 Frame 和同一逻辑槽位重新读取的 Raw BER；
- 错误 bit 数、比较 bit 数、Header/Payload 分项 BER；
- stego→attacked 的 PSNR/SSIM、改变像素率；
- 结果预览和失败解释。

若攻击后可用槽位少于原 Frame，BER 返回 `N/A/InsufficientComparableSlots`，协议读取返回 `InsufficientSlots`；不得把缺失 bit 默认为 0。
二进制载荷只比较字节，不在失败时保存或展示全部内容。

### 8.3 与 Robustness Lab 的边界

LSB Document 只回答“这份 LSB 实验经过几个代表性处理后发生什么”。参数扫描、多 trial、组合链、第一次失败、Profile 矩阵和
完整攻击实验仍由 Robustness Lab 负责。若未来要把 LSB 观察接入 Robustness Lab，应新增只读 `ILsbObservationProbe`，而不是让
Robustness Lab 依赖 LSB Document 或复制提取器。

## 9. SOLID 架构与朴素设计

### 9.1 依赖方向

```text
Features/LsbSteganographyLab
        ↓ 只依赖窄 Application 用例
Application/LsbSteganography
        ↓ 协调领域对象与既有端口
Domain/Steganography ───→ Domain/Imaging / Domain/BitPlanes
        ↑
Infrastructure/Steganography + 既有 Imaging/Persistence/Robustness 适配
```

`Domain.Steganography` 不引用 Avalonia、文件路径、JSON、DI、Bitmap、水印协议或 Host SDK。`Application` 不执行逐像素位运算、
Gamma 特殊函数或 JSON 拼接。`Feature` 不解码图片、不生成槽位、不计算统计、不写文件。

### 9.2 SOLID 具体落实

- SRP：Frame codec、容量、槽位布局、位置顺序、写入、提取、统计、投影和报告分别承担一个变化原因；
- OCP：只有确实存在两个实现的 `ILsbSlotOrder` 使用 Strategy；新增顺序类型时扩展实现和显式登记，不改写入器；
- LSP：所有 SlotOrder 都必须满足“相同输入可复现、无重复、无越界、精确返回 requestedCount”的契约测试；
- ISP：准备、容量预检、写入、统计、脆弱性、导出分别使用窄接口，不创建 `ILsbEverythingService`；
- DIP：应用层依赖既有 `IImageCodec`、`IAtomicFileWriter` 和窄报告/文件对话框端口；Document 依赖用例接口；
- 所有权：Session 属于单个 Document Scope；算法 singleton 无图像缓存；Payload 和提取字节由拥有者释放/清零。

### 9.3 允许的朴素模式

- Strategy：只用于顺序/伪随机槽位和复用的图像扰动算子；
- Value Object：Frame version、通道策略、bit、seed、容量与配方；
- Facade/Use Case：按一次用户操作协调领域服务；
- Adapter：现有 codec、文件对话框、原子写入、报告序列化；
- Session：表达单 Document 的大对象所有权与结果失效。

禁止为模式数量引入 Abstract Factory、Builder 链、事件总线、Mediator、通用 Pipeline、插件式统计发现或 Service Locator。
枚举到显示文字可用显式 `switch`，不必为四个固定通道策略建立类层级。

## 10. 应用用例、Session 与 Document 生命周期

### 10.1 建议窄用例

- `IPrepareLsbExperimentUseCase`：解码载体、建立 Session、统计可用像素；
- `IEstimateLsbCapacityUseCase`：验证配方和 Frame 长度，返回容量事实；
- `IEmbedAndAnalyzeLsbUseCase`：编码 Frame、写入、立即回读、生成变化与统计；
- `IRunLsbFragilityUseCase`：执行一个受控扰动并返回 BER/恢复事实；
- `IExtractLsbPayloadUseCase`：使用显式配方从当前图片读取，严格限制长度；
- `IExportLsbImageUseCase`：PNG 编码、真实回读、自检、原子发布；
- `IExportLsbReportUseCase`：输出版本化 JSON/CSV 摘要；
- Payload 文件读取继续通过窄端口，64 KiB 上限在读取前后都验证。

### 10.2 Session 所有权

Session 长期最多持有：一张 source、一张 stego、当前一张 attacked 图、当前 Frame 的受限字节、紧凑位置索引、统计结果和最大边 1024 的
有限预览。切换载体或关闭 Document 时取消工作，释放 Bitmap，清空 Payload/Frame/提取缓冲并断开完整图引用。

不得长期缓存每个攻击结果的完整图片，不为多个通道/bit 预先创建全尺寸位图，不保存每个槽位的对象 DTO。位置使用数值数组和聚合网格；
UI 行模型只为可见统计建立小对象。

### 10.3 generation、取消与结果失效

- 更换载体、Payload、类型、通道、bit、位置策略或 seed 会推进配方 Revision，并使 stego、统计和攻击结果过期；
- 运行新写入取消旧写入；运行新攻击取消旧攻击；关闭后所有迟到成功/失败均被拒绝；
- 修改纯显示选项不使算法结果过期；
- CPU 密集工作通过用例在后台执行并按行/固定批次检查取消，命令调用必须立即交还 UI 线程；
- 取消不返回半个成功 Frame，不自动导出，不覆盖最后一个完整结果。

### 10.4 Persistable Document

| 字段 | 固定值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.lsb-steganography-lab` |
| 显示名称 | `LSB 隐写与统计实验` |
| 分类 | `图像安全` |
| 描述 | `以像素低位写入、位置可视化、统计对比和受控扰动观察 LSB 隐写的可检测性与脆弱性` |
| 实例基数 | 多实例；每实例独立载体、配方、Payload、结果和取消令牌 |

选择 Document 而不是 Tool：载体、配方、统计和实验结果构成可保存工作上下文；用户需要并排比较策略；完整图片与取消必须按实例释放。

快照 schema 1 只保存：载体路径、PayloadKind、通道策略、bit、位置策略、seed、统计 Scope、当前显示页和攻击预设 ID。
不保存文本、二进制 Payload、Frame、提取字节、图片像素、Bitmap、统计结果、绝对输出路径或错误堆栈。恢复后不自动读文件、不自动写入、不自动攻击；
用户必须重新选择/输入 Payload 并显式运行。

## 11. 界面与交互设计

### 11.1 建议布局

```text
顶部固定警示：教学实验；不保证不可检测；不同于 DCT 鲁棒水印

左侧配置区
  载体与容量
  文本/二进制 Payload
  通道、bit、顺序/伪随机、seed
  预检、写入、导出 PNG

中央联动预览
  Cover | Stego | 写入位置/实际变化 | bit 前后
  同步缩放、坐标探针、Header/Payload 图例

右侧结果区
  回读/CRC/容量/变化摘要
  位分布 | PoV 卡方 | 邻接 | 局部网格
  脆弱性预设与 BER/恢复结果

底部说明
  当前指标定义、样本 Scope、N/A/失败原因、等价数值表
```

### 11.2 交互规则

- 未完成容量预检时禁用写入；Payload 超限、容量不足、透明像素不足和未知配置在运行前显示；
- 写入期间锁定配方，但取消按钮保持可用；切换配置后旧结果以“已过期”保留摘要或清空，不能继续导出；
- 伪随机 seed 可复制、重新生成和手工输入，旁边始终显示“不是密码”；
- bit 1 明确显示单字节最大变化为 2，bit 0 为 1；
- 统计表同时显示 cover、stego、delta、样本数和 Scope；图表下面必须有键盘可达的等价表格；
- p 值不使用红/绿“通过”灯；CRC 与 Frame 状态可以使用图标，但必须有完整文字；
- 攻击只对最近一次完整 stego 运行，结果参数固定可见；
- 提取失败不自动展示乱码或保存损坏二进制；
- 所有预览共享同一源坐标映射，探针不因当前 Tab 改变坐标含义。

## 12. 报告、隐私与兼容

### 12.1 报告

JSON schema 与 CSV 列必须版本化，至少包含：图片尺寸、opaque/slot 数、配方稳定 ID、Frame 长度、容量、变化摘要、每通道统计、Scope、
卡方/df/p、邻接四格、质量指标、攻击参数、BER 和结构化状态。

报告不得包含：Payload 文本/字节、提取内容、Frame 原始字节、绝对输入/输出路径、seed 被称作“密钥”、异常堆栈或机器用户名。
允许包含用户显式勾选后的文件名叶子，但默认不包含。所有非有限数使用显式 `kind` 或空值与原因，不能输出非法 JSON `Infinity/NaN`。

### 12.2 兼容事实

- Frame Magic、version、字段偏移、端序、bit 顺序、CRC 范围；
- 通道策略的稳定 ID 和 RGB 槽位顺序；
- Alpha=255 资格规则、bit 允许集合；
- PlacementVersion、PRNG、无偏采样和 sparse shuffle 规则；
- 统计 Scope、PoV 配对、邻接方向、Gamma 算法精度；
- 报告 schema、Document ID 和快照 schema。

这些事实变更必须显式升版本并保留旧读取测试，不能依赖插件程序集版本推断。

## 13. 中文注释与设计说明规定

- 新增公开或跨层内部契约、Frame 字段、bit/byte 顺序、CRC 范围、槽位映射、统计公式、资源所有权和失败状态必须写详细中文 XML 注释；
- 注释优先解释“为什么这样定义、单位是什么、边界在哪里、谁拥有并清理”，不要逐行翻译代码；
- 位移、端序、checked 容量、partial Fisher-Yates、拒绝采样、Gamma 收敛、PoV 配对和邻接边界必须说明公式与风险；
- 必须说明伪随机 seed 不是密钥、CRC 不是认证、统计不是隐写概率、LSB 与 DCT 水印不是同一协议；
- 复杂像素循环开头用中文说明扫描顺序、取消粒度和内存上限，循环体保持直接；
- 复用 Robustness 算子时说明为何只暴露 allowlist，以及为何不建立第二个攻击框架；
- 不用注释掩盖复杂设计；若注释需要解释多层工厂或路由，应先简化代码。

建议风格：

```csharp
/// <summary>
/// 按 V1 逻辑槽位从全部可用槽中无放回选择指定数量的位置。
/// </summary>
/// <remarks>
/// seed 只保证实验可复现，不具备密码学保密性。实现使用版本化 SplitMix64 和拒绝采样，
/// 不能替换为 System.Random；否则相同实验在运行时升级后可能产生不同位置，历史报告将无法复核。
/// 稀疏交换表只随实际 Frame bit 数增长，避免为 16M 像素的 RGB 槽位分配完整排列。
/// </remarks>
internal sealed class PseudoRandomLsbSlotOrder : ILsbSlotOrder
```

## 14. 单元测试、集成测试与质量门禁

### 14.1 Frame 与容量 Golden

- 固定文本、空 Payload、二进制 0x00/0xFF、最大长度的完整 Header 十六进制 Golden；
- little-endian、byte 内 MSB-first、Header CRC/Payload CRC 范围；
- Magic/version/flags/kind/header CRC/payload CRC/非法 UTF-8 分支；
- Header CRC 失败时不得依据损坏长度分配；
- 0、1、19、20、21 字节容量边界，65,536 上限和 checked 溢出；
- 单/三通道、透明像素跳过、bits/pixel 与 bits/slot 精确值。

### 14.2 槽位与 PRNG

- 1×1、奇数尺寸、全透明、混合 Alpha、单通道和 RGB 行优先 Golden；
- 顺序位置精确；伪随机固定 seed 的前 N 个位置 Golden；
- 相同事实逐槽一致，不同 seed 至少在确定样例中不同；
- 无重复、无越界、请求 0/全部/接近全部和槽位不足；
- 拒绝采样边界、SplitMix64 官方常量向量、sparse swap 正确；
- 写入和读取共享位置实现的契约测试；不得通过“两个错误算法彼此抵消”冒充正确。

### 14.3 写入、提取与像素变化

- bit 0/1 的 `-2^b/0/+2^b` 手算字节；未选通道、Alpha 和跳过像素逐字节不变；
- 输入 `PixelImage` 不变，输出拥有独立缓冲；取消不返回半结果；
- 文本/二进制、顺序/伪随机、四通道策略的端到端回读；
- 参数错配、bit 错配、seed 错配和裁短图片的结构化状态；
- 变化计数、通道计数、Header/Payload 分界、局部 16×16 网格和探针；
- 内存 stego 自检及 PNG 编码—真实解码—提取自检；正常导出路径无法选择 JPEG；
- 原子写入成功、失败、取消和失败后重试。

### 14.4 统计数值

- bit 全 0、全 1、50/50 的比例与熵 Golden；
- bit 0 与 bit 1 的 PoV 配对表，空 pair、单 pair、已知 χ²/df；
- Regularized Gamma Q 与高精度参考值对照，极小/极大 χ²、迭代上限和非有限输入；
- 水平/垂直 `00/01/10/11` 小矩阵 Golden、1×N、N×1、透明间断和无有效邻接 `N/A`；
- cover/stego 必须使用同 Scope；样本数不一致时拒绝 delta；
- R/G/B 分项和 RGB 加权聚合；
- 固定合成图中顺序/随机、bit 0/1 和不同嵌入率的趋势测试只断言稳健方向，不使用脆弱的机器相关阈值；
- 所有呈现文案不包含“不可检测”“安全通过”或“隐写概率”。

### 14.5 脆弱性与复用边界

- JPEG 95/80/60 使用正式 codec，Alpha 非全不透明时可见阻断；
- 75%/50% 缩放往返的步骤、尺寸和插值与既有算子一致；
- Gaussian/Median 只通过现有 Strategy，源图不变；
- 每个预设都从同一 stego 基线开始，不串联上次结果；
- Raw BER、Header/Payload 分项、CRC 状态和不可比较原因；
- 固定小图证明至少一个代表性扰动能暴露 LSB 脆弱性，但不把特定恢复率当跨图片保证；
- LSB 用例不引用 Watermark Frame/Carrier，不新增第二套 JPEG/Scale/Filter 算法。

### 14.6 用例、Document、UI 与资源

- 容量预检先于位置/图片分配；Payload 读取前后双重 64 KiB 门禁；
- 配方变更推进 Revision 并使结果过期，纯显示变更不标脏；
- 新运行取消旧运行，迟到成功/失败、关闭后返回不能覆盖状态；
- 两个 Scope 的载体、Payload、seed、Session、Bitmap 和取消完全隔离；
- 快照不含 Payload/Frame/统计/像素，恢复不自动读取或运行；
- 报告 schema、UTF-8、CSV 转义、非有限数、隐私字段和原子写入；
- 第八个真实 View 在 Avalonia Headless 加载，编译绑定、键盘、警示、N/A、图例和等价表格可断言；
- Module 恰好登记八个唯一 Persistable Document，零 Tool/Workflow Action/Workbench Command；
- 最大 Payload 的位置存储受控；完整图长期数量有结构测试；热点循环无逐像素 LINQ、逐像素对象和无界并行。

### 14.7 本地开发门禁命令

G0 开始前和 G9 封板时都从仓库根目录执行：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

门禁要求：两配置构建零警告零错误；测试零失败零跳过；总数必须大于起始 191；不得删除、跳过或放宽既有测试；所有协议、位运算、
统计和映射都有确定值断言。性能使用结构/预算门禁，不写依赖机器速度的严格毫秒断言。

本轮不创建 GitHub Actions、Azure Pipelines 或其他 Windows CI；本地 Release 只是另一编译配置回归，不表示发布。

## 15. G0–G9 交付与验收

### G0：产品、协议与统计基线

交付：冻结第 1–8 节；审计可复用类型和扰动参数；建立 Frame、槽位、统计 Golden 表、风险措辞与内存预算；复跑 191 项基线。

验收：没有未决定的 Frame 字段、端序、bit 顺序、Alpha、通道顺序、seed、Scope、p 值或失败状态；历史记录写实际证据。

### G1：Frame 与容量

交付：`LsbPayload`、Frame header/codec、CRC、容量值对象和读取状态。

验收：纯领域 Golden 全过；损坏 Header 不触发不受控分配；Domain 无 Avalonia/文件/JSON/DI；敏感字节所有权明确。

### G2：槽位布局与位置策略

交付：可用像素扫描、四个通道策略、逻辑槽位转换、`ILsbSlotOrder`、顺序与伪随机实现、PlacementVersion。

验收：契约测试证明无重复/越界、可复现与可逆；最大 Payload 内存有界；不使用水印随机源或 `System.Random`。

### G3：写入与提取

交付：不变输入的写入器、严格提取器、变化聚合、Frame BER 比对和 PNG 内存回读。

验收：所有策略端到端逐字节回读；未选字节不变；错误分类、取消和 CRC 测试齐全；不依赖 DCT 水印代码。

### G4：可视化与统计

交付：位置/变化/bit 代理、探针、16×16 网格、位分布、PoV 卡方、Gamma Q、邻接分析和对比 DTO。

验收：小矩阵 Golden 和参考特殊函数值通过；全图/选择/前缀 Scope 不混淆；没有自动检测结论。

### G5：脆弱性实验

交付：四类显式 allowlist 预设、攻击用例、BER/CRC/质量观察与结果代理。

验收：只复用既有扰动；每次从同一 stego 开始；尺寸/Alpha/N/A 规则可见；不复制 Robustness Lab 的扫描框架。

### G6：应用、Session 与报告

交付：七个窄用例、Session 所有权、取消/generation、JSON/CSV schema、文件端口和隐私门禁。

验收：Document 可用替身覆盖全部分支；应用层不依赖 Bitmap；报告零 Payload/绝对路径；失败后仍可观察和重试。

### G7：Document 生命周期

交付：稳定 ID、Module/DI 登记、schema 1、命令状态、多 Scope、Bitmap 替换与关闭释放。

验收：第八个贡献唯一；快照轻量；恢复不自动运行；两个实例隔离；迟到结果门禁完整。

### G8：UI 与教学解释

交付：配置、四类预览、统计表/图、攻击面板、警示、帮助目录、键盘/高对比和 Headless View 测试；Standalone 复用真实 Module。

验收：不靠颜色独占信息；p/CRC/BER/N/A 文字准确；Document/View 无像素算法、文件访问或协议拼装。

### G9：本地封板与文档

交付：执行 14.7 全门禁；同步根入口和公共边界；补齐专用文档与 G0–G9 历史；记录有限人工验收的实际完成/延期。

验收：实际测试数、零跳过、未执行事项和回滚方式可追踪；无 AIFLOW、Windows CI、发布脚本或发布完成声明。

## 16. 预计代码、测试与文档落点

### 16.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Domain/Checksums/
│  └─ Crc32.cs
├─ Domain/Steganography/
│  ├─ LsbFrameModels.cs
│  ├─ LsbFrameCodec.cs
│  ├─ LsbCapacityCalculator.cs
│  ├─ LsbSlotLayout.cs
│  ├─ LsbSlotOrders.cs
│  ├─ LsbEmbeddingEngine.cs
│  ├─ LsbExtractionEngine.cs
│  ├─ LsbChangeAnalyzer.cs
│  └─ LsbStatisticsAnalyzers.cs
├─ Application/LsbSteganography/
│  ├─ LsbExperimentContracts.cs
│  ├─ LsbExperimentUseCases.cs
│  ├─ LsbFragilityUseCase.cs
│  └─ LsbReportExportUseCase.cs
├─ Infrastructure/Steganography/
│  └─ LsbExperimentReportSerializer.cs
└─ Features/LsbSteganographyLab/
   ├─ LsbSteganographyLabDocument.cs
   ├─ LsbSteganographyLabView.axaml
   ├─ LsbSteganographyLabView.axaml.cs
   ├─ LsbPlacementPreviewControl.cs
   ├─ LsbStatisticsControl.cs
   └─ LsbSteganographyHelpCatalog.cs
```

文件名可按实际职责小幅调整，但不得把 Frame、写入、统计、攻击、报告和 Document 堆进单一巨型类；也不得反向拆成大量只有转发逻辑的接口。

### 16.2 测试

```text
tests/ImageLabPlugin.Tests/
├─ LsbFrameAndCapacityTests.cs
├─ LsbSlotLayoutAndOrderTests.cs
├─ LsbEmbeddingAndExtractionTests.cs
├─ LsbStatisticsTests.cs
├─ LsbFragilityTests.cs
├─ LsbUseCaseAndReportTests.cs
├─ LsbSteganographyLabDocumentTests.cs
└─ LsbSteganographyLabViewTests.cs
```

测试可因规模合并或拆分，但每个 14 节门禁必须有清晰可查的归属。

### 16.3 专用文档

实施过程中按现有能力目录惯例，在 `docs/design/lsb-steganography-lab/` 同步：

- `README.md`：能力入口、阅读顺序、当前状态和教学警示；
- `user-manual.md`：面向新手解释载体、LSB、容量、seed、统计和脆弱性；
- `guide.md`：准确描述参数、Frame、通道、位置、状态、导出和限制；
- `mathematical-principles.md`：LSB replacement、容量、熵、PoV 卡方、Gamma Q、邻接、BER、PSNR；
- `protocol.md`：`ILSB` V1 字节布局、端序、bit 顺序、CRC、PlacementVersion 与兼容；
- `report-schema.md`：JSON/CSV 字段、版本、N/A 和隐私边界；
- `testing.md`：命令、实际测试数、Golden 来源、已证明与未证明事项；
- `implementation.md`：本文，持续反映计划与实际状态；
- `history/README.md` 与 G0–G9：实际实施证据，不替代当前指南。

还需同步：仓库 `README.md`、`docs/README.md`、`docs/design/README.md`、`docs/future-capabilities.md`、
`docs/design/shared/image-domain-boundaries.md` 和必要的项目/窗口职责说明。同步必须跟随每个 G 包，不允许到 G9 才一次性补写。

规划阶段公共入口只能标注“规划中”；生产闭环与本地门禁全部完成后才可写“开发实现与本地自动门禁完成”。

## 17. 有限人工验收清单

1. 用小型不透明 PNG、已知 UTF-8 文本完成 R/bit0/顺序写入，检查 Frame、位置、变化和回读；
2. 切换 G/B/RGB、bit0/bit1，核对单字节变化上限和每通道计数；
3. 用相同 seed 重跑伪随机策略，确认位置与输出一致；更换 seed 后位置改变；
4. 打开含透明/半透明像素的 PNG，确认这些像素不计容量且逐字节不变；
5. 输入 0、边界容量、65,536 和超限 Payload，确认空载荷合法、超限在运行前阻断；
6. 比较顺序/伪随机和不同通道下的位分布、卡方、邻接及 Scope，检查没有“安全/不可检测”结论；
7. 点击 Header、Payload、未变化选中槽和未选像素，核对探针与图例；
8. 导出 PNG 后重新载入并按同配方提取，确认严格 UTF-8/二进制结果；
9. 分别执行 JPEG 95/80/60、75%/50% 缩放往返、Gaussian 和 Median，查看 BER/CRC/失败原因；
10. 快速切换配方、取消写入/攻击并换图，旧结果不得闪回或被导出；
11. 保存/恢复 Document，确认 Payload、Frame 和结果未持久化，且不会自动读取或运行；
12. 同时打开两个实例，确认载体、seed、Payload、结果和取消互不影响；
13. 在键盘、高对比和无颜色辨识条件下完成主要流程并读懂等价数值表；
14. 检查顶部和导出报告均明确说明教学用途、统计局限及与 DCT 水印的区别。

人工清单在 Standalone 中只证明开发期交互，不证明真实 Host、ZIP、安装或发布行为。未执行项必须在 G9 记录为延期。

## 18. 回滚与兼容策略

1. 可先从 Module 隐藏第八个贡献，同时保留 Document 类型和稳定 ID 供开发期快照安全识别；
2. 再移除 Feature View/Document 与专用应用用例注册；
3. 再移除报告和脆弱性协调；
4. 最后移除独立 Steganography 领域；不得删除被位平面、比较或鲁棒性能力继续使用的共享原语；
5. 不修改或回滚现有 DCT 水印协议、Bit Plane Viewer 和 Robustness Lab 的稳定行为；
6. 已导出的 PNG/JSON/CSV 是用户文件，回滚不得删除或覆盖；
7. 开发期 schema 变化使用显式版本分支测试，不通过捕获所有异常并返回空结果来“兼容”。

## 19. 完成定义

只有同时满足以下条件，才可把状态改为“开发实现与本地自动门禁完成”：

- G0–G9 均有实际历史记录，待办没有预先勾选；
- 文本/二进制、四通道策略、bit0/1、顺序/伪随机、写入/提取/PNG 自检形成完整闭环；
- 位置、变化、位平面、卡方、位分布、邻接和有限脆弱性结果可见且定义准确；
- 独立 LSB Frame 与 DCT 水印在协议、代码和产品措辞上完全隔离；
- SOLID 分层、朴素 Strategy、窄接口、Session 所有权、取消和 generation 都有自动测试；
- 新代码中文注释覆盖算法语义、设计思路、数值风险、资源所有权和安全边界；
- Debug/Release locked 本地门禁零失败、零跳过、零警告，实际总数大于 191；
- 专用文档、公共入口、未来能力状态与共享边界同步；
- 生产代码和贡献清单中没有 AIFLOW、Workflow Action、Workbench Command 或通用 DAG；
- 没有新增 Windows CI，也没有声称完成真实 Host、ZIP、安装/卸载或发布验收。

## 20. 发布阶段明确延期

以下内容不属于本轮开发完成条件，准备正式发布时再按 `docs/design/shared/deployment-and-release.md` 执行：

- Windows CI 与目标平台矩阵；
- 正式 ZIP、manifest、哈希、依赖闭包和可复现打包；
- 真实 Host Catalog/Dock、多实例恢复、安装、升级、卸载和回滚；
- 不同 Windows 版本、DPI、主题、GPU 和权限环境；
- 授权自然图片数据集上的统计校准、误报/漏报研究和长期性能；
- 大图长时间内存、取消、资源泄漏和多实例压力；
- 安全评审、发布说明和对外兼容承诺。

本地 Release 配置只表示第二编译配置回归，不等于发布。V1 即使通过全部开发门禁，也只能描述为“教学和实验用的像素域 LSB 工具”，
不能宣传为安全、不可检测或鲁棒的隐写协议。

## 21. 研究依据与解释边界

PoV 卡方设计参考 Andreas Westfeld 与 Andreas Pfitzmann 的
[*Attacks on Steganographic Systems*](https://www2.htw-dresden.de/~westfeld/publikationen/ihw99.pdf)。其核心用途是说明简单 LSB 替换会改变成对值统计，不应被反向宣传为
“低位天然不可检测”。

更强的 LSB 检测可参考 Jessica Fridrich、Miroslav Goljan 与 Rui Du 的
[*Reliable Detection of LSB Steganography in Color and Grayscale Images*](https://dde.binghamton.edu/publications/acm_2001_03.pdf)。
该工作说明 RS 等方法属于独立隐写分析技术；V1 不实现 RS，
也不把当前三类统计包装成等价替代品。

外部论文只提供方法背景。实际公式、数值容差、样本 Scope、中文解释和产品结论仍以本计划冻结的 V1 协议及仓库 Golden 测试为准。
