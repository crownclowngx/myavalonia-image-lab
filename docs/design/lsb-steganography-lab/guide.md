# LSB 隐写与统计实验准确指南

## 固定范围

- 输入：一张 PNG/JPEG，沿用 64 MiB 编码和 16,000,000 像素上限。
- 输出：隐写结果只允许 PNG；JSON/CSV 是不含敏感内容的摘要。
- Payload：严格 UTF-8 文本或二进制，`0..65,536` 字节；空 Payload 合法。
- 通道：R、G、B 或固定 R→G→B；不写 Alpha、Y、Cb/Cr。
- 位平面：bit 0 或 bit 1；只使用 Alpha=255 像素。
- 位置：行优先顺序或 `splitmix64-sparse-fisher-yates-v1`。

## 运行与失败状态

容量预检先于图片复制和位置数组分配。提取明确区分 `Success`、`InsufficientSlots`、`MagicMismatch`、`UnsupportedVersion`、`UnsupportedFlags`、`UnknownPayloadKind`、`HeaderCrcMismatch`、`LengthOutOfRange`、`PayloadCrcMismatch` 和 `InvalidUtf8`。Header CRC 失败时不会信任声明长度。

写入必须通过内存回读和 PNG 编码—真实解码回读。任一边界失败都不会提交可导出结果。PNG 发布使用同目录临时文件后原子替换；正常路径没有 JPEG 输出选项。

## 统计与可视化

位置代理最大边 1024：蓝色表示 Header 选择、黄色表示 Payload 选择、红色表示实际字节变化、深灰表示未使用。bit 前后代理按像素代理单元聚合目标 bit 的 1 比例。颜色旁始终给出等价计数和文本。

统计包含位 0/1 数、one ratio、二元熵、PoV χ²/df/p、水平与垂直 `00/01/10/11` 及 transition。所有结果带 Scope，Cover/Stego 样本数必须相同。

## 受控预设

JPEG 95/80/60、75%/50% 双线性缩放往返、Gaussian σ=0.6/1.2、Median 3×3。实现调用既有 `IImagePerturbationOperator`；每次从同一 stego 开始。JPEG 和缩放往返要求全不透明，避免 Alpha 语义漂移。这里没有参数扫描、组合 DAG 或自动攻击搜索。

## SOLID 与资源

Frame、容量、布局、位置、写入、提取、统计、投影、用例和序列化各有单一变化原因。只有顺序/伪随机和既有扰动使用朴素 Strategy；没有 Factory 链、Mediator、事件总线或 Service Locator。Session 属于一个 scoped Document，释放时清零 Frame 并断开 source/stego/attacked；配置变化通过 generation 使迟到结果失效。

快照 schema 1 保存载体路径、Payload 类型、通道、bit、位置、seed、Scope 和扰动预设，不保存 Payload 内容、Frame、完整图片、Bitmap、统计、输出路径或异常堆栈。
