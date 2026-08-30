# ImageLabPlugin V1 频域分析器实施计划

> 计划状态：待实施；G0–G7 均不得在没有门禁证据时标记完成  
> 基线日期：2026-08-30  
> 产品名称：Spectrum Inspector／频域分析器  
> 技术基线：.NET 10、Avalonia 12、Managed Plugin SDK 3.3  
> 核心路线：六通道分析代理 + 二维 FFT/IFFT + 分块 DCT + 径向能量 + 共轭对称频带遮罩  
> 实施原则：SOLID 优先；先冻结数值与资源边界，再建立纯算法和用例，最后接入 Document 与交互

| 实施包 | 当前状态 | 目标 | 完成后记录 |
| --- | --- | --- | --- |
| G0 | 待实施 | 冻结产品范围、数学语义、资源预算、交互与依赖决策 | `docs/plan-history/spectrum-inspector/g0-product-and-numeric-baseline.md` |
| G1 | 待实施 | 建立六通道、分析代理和图像重建基础 | `docs/plan-history/spectrum-inspector/g1-channel-and-analysis-proxy.md` |
| G2 | 待实施 | 完成可取消、可验证的 FFT/IFFT 公共频域核心 | `docs/plan-history/spectrum-inspector/g2-fft-foundation.md` |
| G3 | 待实施 | 完成全局频谱、分块 DCT、频点查询与径向能量分析 | `docs/plan-history/spectrum-inspector/g3-spectrum-analysis.md` |
| G4 | 待实施 | 完成共轭对称频带遮罩和 IFFT 重建 | `docs/plan-history/spectrum-inspector/g4-band-reconstruction.md` |
| G5 | 待实施 | 完成 Persistable Document、生命周期与快照 | `docs/plan-history/spectrum-inspector/g5-document-lifecycle.md` |
| G6 | 待实施 | 完成联动界面、Standalone 和 PNG 导出闭环 | `docs/plan-history/spectrum-inspector/g6-ui-and-export.md` |
| G7 | 待实施 | 完成自动测试、本地门禁、文档和开发封板 | `docs/plan-history/spectrum-inspector/g7-local-sealing.md` |

本文定义 ImageLab 在频域隐式水印之后的第二个产品能力。它不是普通图片编辑器，也不是把当前水印页面中的
“DCT 对数幅度”简单放大，而是一个可以解释空间位置、颜色通道、8×8 DCT 块和全局频谱关系的独立实验
Document。

本文是实施时的唯一总计划。每个 G 包完成后，必须在对应实施记录中填写实际修改、自动测试、数值证据、
性能数据、偏差、遗留风险和回滚方式；本文不得提前写入完成结论。

## 1. V1 目标与固定实施顺序

### 1.1 用户闭环

```text
选择 PNG/JPEG 图片
    ↓
选择 R、G、B、Y、Cb 或 Cr 通道
    ↓
选择 512、1024 或 2048 分析档位
    ↓
生成分析代理并执行全局二维 FFT
    ↓
查看幅度谱、相位谱、径向能量与频带占比
    ↓
悬停频点，读取坐标、幅值、相位和归一化能量
    ↓
点击原图，检查对应完整 8×8 块的像素、DCT 和 IDCT
    ↓
选择低频、中频、高频或自定义径向频带
    ↓
实时生成共轭对称遮罩并执行 IFFT
    ↓
联动查看原图、频谱、遮罩和重建图
    ↓
按需把当前分析代理的重建结果原子导出为 PNG
```

### 1.2 固定实施顺序

1. G0 先冻结坐标系、归一化、频带、资源和持久化语义；没有这些事实时不得编写生产 FFT；
2. G1 先建立通道与分析代理，保证后续算法只接收受控大小的纯像素数据；
3. G2 用纯数值测试证明 FFT/IFFT、中心化和补零正确，再允许界面消费；
4. G3 在只读分析结果上完成频谱、DCT、悬停和能量曲线；
5. G4 只实现径向、共轭对称的频带遮罩，不提前实现任意频谱绘制；
6. G5 让 Document 通过应用用例消费已验证算法，并完成取消、迟到结果和快照边界；
7. G6 最后接入复杂联动界面、Standalone 和 PNG 导出；
8. G7 执行 Debug/Release 本地门禁并同步文档，不执行发布阶段门禁。

## 2. 当前基线与已有事实

### 2.1 当前工程基线

当前仓库已经具备：

- `ImageLabPlugin.Plugin` 唯一真实插件程序集；
- `ImageLabPlugin.Standalone` 复用真实 Module、View、Document 和 DI 服务；
- 两个 Persistable Document：“水印写入”和“提取与验证”；
- 自有 RGBA8888 `PixelImage`、16,000,000 像素安全上限和 64 MiB 编码图片上限；
- RGB/YCbCr 亮度投影、8×8 DCT-II/IDCT、PSNR、全局 SSIM、差异图和预览缩放；
- Avalonia PNG/JPEG 正式编解码器、文件选择适配器和原子文件写入器；
- 44 个 Domain、协议、编解码、Document 生命周期和组合根自动测试；
- Debug/Release、`--locked-mode`、`--warnaserror` 的本地开发门禁；
- 当前明确延期的真实 Host、ZIP、Windows CI 和发布封板。

### 2.2 可复用能力与已知缺口

可直接复用：

- `PixelImage`、`ImageSize` 和图片编解码端口；
- `Dct8x8Transform` 的固定 8×8 正交 DCT-II/IDCT；
- `ColorSpaceConverter` 中已经冻结的 YCbCr 公式；
- `ImagePreviewProjector` 的有界预览思想；
- Document Scope、`IDocumentLifetime`、取消和防止迟到结果覆盖的现有模式；
- 原子 PNG 输出和 Headless Avalonia View 测试方式。

需要新增或修正：

- 六通道的统一只读平面和选定通道重建；
- 适合频率分析的抗混叠缩小，不能直接依赖最近邻预览；
- 二维复数矩阵、FFT/IFFT、补零、裁剪和中心化坐标；
- 幅度、相位、径向能量、频点信息和频带遮罩；
- 支持六通道的分块 DCT 投影与单块报告；
- 当前 `FrequencySpectrumProjector` 在调用已经自行减 128 的 `Dct8x8Transform` 前再次减 128，需用数值
  回归修正双重中心化；该修正只改变解释性预览，不改变水印载体协议。

### 2.3 主工程约束

- Plugin Module 是贡献和服务登记的唯一事实源；
- Document Model 每个实例拥有独立 DI Scope；同一种 Document 可以同时打开多个实例；
- Tool Model 是插件级 singleton，不适合持有“当前图片”的实例状态；
- 插件只依赖公开 Plugin SDK/UI SDK，不引用 Host、Dock 或 Host 内部实现；
- Domain 不依赖 Avalonia、文件系统、DI、图片编码或窗口；
- View 不拥有算法、文件和 Document 生命周期；
- 本能力不增加第三方数学、图表或原生运行时依赖；
- 不使用 AIFLOW，不登记 Workflow Action 或 Workbench Command。

## 3. Document 形态与状态所有权

### 3.1 贡献形态

“频域分析器”是产品名称，不等于 Host 的 `Tool`。V1 固定登记为第三个 Persistable Document：

| 字段 | 固定值 |
| --- | --- |
| 稳定身份 | `myavalonia.plugin.image.lab.document.spectrum-inspector` |
| 显示名称 | `频域分析器` |
| 描述 | `观察图像通道的全局 FFT、分块 DCT、频带能量与逆变换结果` |
| 分类 | `图像分析` |
| Host 注册 | `AddPersistableDocument<SpectrumInspectorDocument, SpectrumInspectorView>` |
| 实例基数 | 多实例，每个实例独立图片、参数、缓存和取消令牌 |

选择 Document 而不是 Tool 的原因：

- 用户可能同时比较多张图片或同一图片的多个通道；
- 图片路径、选中块、频带和重建结果属于实例工作上下文；
- 关闭一个实例必须释放其大型频谱缓存；
- singleton Tool 会把多个分析任务错误地合并成全局状态。

### 3.2 Document 私有状态

持久状态：

- 源图片路径；
- 选择的颜色通道；
- 512/1024/2048 分析档位；
- 当前频谱视图和显示模式；
- 低频与中频边界；
- 当前频带选择及自定义内外半径；
- 最后选择的原图坐标。

只存在于当前运行实例的派生状态：

- 已解码原图和分析代理；
- 复数频谱、幅度投影、相位投影和 DCT 投影；
- 径向能量曲线与频带统计；
- 当前频带遮罩和 IFFT 重建图；
- Avalonia `Bitmap`；
- 当前操作进度、取消源和错误状态。

瞬时交互状态：

- 鼠标悬停位置和频点提示；
- 面板尺寸和缩放换算结果；
- 防抖期间尚未提交的频带参数。

瞬时悬停不得把 Document 标记为 Dirty。持久参数和选中块发生变化时才推进 Document Revision。

### 3.3 快照与恢复

- 快照 schema 从 `1` 开始；所有枚举按稳定字符串或显式数值写入，不序列化显示文字；
- 不把 Bitmap、像素缓冲、复数矩阵、重建 PNG 或错误堆栈写入快照；
- 恢复时验证枚举、分辨率和频带边界，未知值回退到文档定义的安全默认值；
- 恢复后只显示路径和参数，用户显式点击“分析”后才读取文件和执行 FFT；
- 源文件不存在、不可读或已经超出安全上限时保留参数并显示可恢复错误，不让 Host 恢复失败；
- 关闭 Document 时取消所有工作并释放原图、分析会话、重建结果和 Bitmap。

## 4. V1 产品范围

### 4.1 必须完成

- 显示全局二维 FFT 幅度谱和相位谱；
- 支持 R、G、B、Y、Cb、Cr 六个通道；
- 支持线性、对数和百分位归一化三种幅度显示；
- 显示并增强逐块 DCT 对数幅度图；
- 点击原图位置，显示对应完整 8×8 块的像素、DCT 系数和 IDCT 重建；
- 标注 DC、低频、中频和高频；
- 悬停频率点时显示显示坐标、内部索引、归一化频率、幅值、相位和归一化能量；
- 显示 256 bin 径向频谱能量曲线；
- 分别显示 DC、低频、中频和高频能量占比；
- 支持全部、低频、中频、高频和自定义径向环带；
- 频带变化后实时执行共轭对称遮罩和 IFFT；
- 联动显示原图、频谱、遮罩和重建图；
- 将当前分析代理的重建结果原子导出为 PNG；
- 支持取消、失败状态、迟到结果保护、多 Document Scope 隔离和快照恢复。

### 4.2 明确不实现

- 原分辨率最大 16,000,000 像素的实时 FFT/IFFT；
- 把分析代理的低频结果伪装成原尺寸处理结果；
- 任意画笔、矩形、自由选择、陷波或自动噪声峰检测；
- 在频谱中写入文字、Logo 或二维码；
- Butterworth、Gaussian 等参数化滤波器；
- 多图幅度/相位交换、Hybrid Image 或批处理；
- 保存完整分析会话、复数频谱或专有工程文件；
- 水印 Control/Data Channel 位置、密钥映射或 Payload 地图；
- 修改 DCT 水印 V1 的系数、Frame、Profile、QIM 或读取语义；
- Windows CI、ZIP、正式发布、真实 Host 封板和市场级性能承诺。

上述能力分别属于 Frequency Filter、Frequency Mask Editor、Periodic Noise Removal、Spectral Art、
Magnitude/Phase Swap 和水印载荷地图等后续独立设计，不能为了“顺手”加入 V1。

## 5. SOLID 架构与依赖方向

### 5.1 分层

```text
Features/SpectrumInspector
  SpectrumInspectorDocument       当前实例状态、命令、Revision 和生命周期
  SpectrumInspectorView           纯布局与绑定
  轻量交互 Control                坐标转发、覆盖层和径向曲线绘制
                 │
                 ▼
Application/SpectrumAnalysis
  IAnalyzeSpectrumUseCase          读取图片并建立只读分析会话
  IInspectDctBlockUseCase          从原图和坐标生成单块报告
  IReconstructSpectrumBandUseCase  从缓存频谱生成遮罩和重建结果
                 │
                 ▼
Domain/Imaging + Domain/Frequency
  通道、分析代理、DCT、FFT、频谱、能量、频带和重建
                 ▲
                 │
Infrastructure
  Avalonia 图片编解码、文件对话框、Bitmap 适配、原子写入
```

依赖只允许由上层指向下层抽象。Domain 不引用 Application、Feature 或 Infrastructure；应用用例不知道
Avalonia `Bitmap`；Document 不直接创建算法实现或 ServiceProvider。

### 5.2 单一职责

- `Fft1DTransform`：只做一维原地复数变换；
- `Fft2DTransform`：只协调行、列和取消，不负责通道或图片；
- `FrequencySpectrum`：只拥有尺寸、补零信息和复数数据的只读语义；
- `SpectrumProjector`：把频谱值映射成可显示 RGBA，不执行 FFT；
- `RadialEnergyAnalyzer`：只计算径向 bin 和频带占比；
- `FrequencyBandMaskFactory`：只根据规则生成共轭对称权重；
- `DctBlockAnalyzer`：只检查一个完整 8×8 块；
- `ImageChannelConverter`：只处理六通道抽取和合成；
- 应用用例负责工作流，Document 负责 UI 状态，View 负责展示。

不得创建“万能 ImageService”“滤镜注册中心”“算法 Strategy 容器”或反射扫描。V1 的算法集合固定，使用普通
构造注入和小接口即可。

### 5.3 接口隔离

计划把当前同时包含图片与 Payload 操作的文件对话框端口拆分为：

```csharp
internal interface IImageFileDialog
{
    Task<string?> PickImageAsync(CancellationToken cancellationToken);
    Task<string?> PickOutputImageAsync(string suggestedName, CancellationToken cancellationToken);
}

internal interface IPayloadFileDialog
{
    Task<string?> PickPayloadAsync(CancellationToken cancellationToken);
    Task<string?> PickPayloadExportAsync(string suggestedName, CancellationToken cancellationToken);
}
```

现有 Avalonia 适配器可以同时实现两个接口。水印 Document 只注入自己使用的端口；频域分析器不依赖 Payload
选择能力。该重构是插件内部边界调整，不改变 SDK 或外部 API。

### 5.4 应用用例契约

建议冻结三个窄用例：

```csharp
internal interface IAnalyzeSpectrumUseCase
{
    Task<SpectrumAnalysisResult> ExecuteAsync(
        SpectrumAnalysisRequest request,
        CancellationToken cancellationToken);
}

internal interface IInspectDctBlockUseCase
{
    DctBlockReport Execute(
        SpectrumAnalysisSession session,
        ImagePoint sourcePoint);
}

internal interface IReconstructSpectrumBandUseCase
{
    Task<BandReconstructionResult> ExecuteAsync(
        SpectrumAnalysisSession session,
        FrequencyBandDefinition band,
        CancellationToken cancellationToken);
}
```

`SpectrumAnalysisSession` 是一个 Document 私有、可释放的分析结果所有者。它保存原始 `PixelImage`、分析代理、
通道、补零尺寸、只读复数频谱、径向报告和必要投影元数据，不暴露可写 `Complex[]`。重建用例必须复制到
受控工作缓冲后再应用遮罩，不能修改缓存频谱。

## 6. 图像通道与分析代理

### 6.1 六通道定义

RGB 使用解码后的未预乘 RGBA8888 字节。YCbCr 使用当前 `ColorSpaceConverter` 一致的全范围公式：

```text
Y  =  0.299000 R + 0.587000 G + 0.114000 B
Cb = 128.000000 - 0.168736 R - 0.331264 G + 0.500000 B
Cr = 128.000000 + 0.500000 R - 0.418688 G - 0.081312 B
```

逆变换：

```text
R = Y + 1.402000 (Cr - 128)
G = Y - 0.344136 (Cb - 128) - 0.714136 (Cr - 128)
B = Y + 1.772000 (Cb - 128)
```

规则：

- R/G/B 重建只替换选择的一个字节，另两个颜色字节和 Alpha 逐字节不变；
- Y/Cb/Cr 重建保留另外两个原始分量，再转换回 RGB；
- RGB 逆变换超出 `[0,255]` 时使用四舍五入和裁切，并统计发生裁切的像素数；
- Alpha 从始至终不进入 FFT，不因任何频带处理而变化；
- 透明像素的 RGB 仍按解码后的未预乘原始值分析，界面必须说明这一点；
- 全通遮罩走精确短路，直接克隆分析代理，避免无意义 YCbCr 往返改变字节。

### 6.2 分辨率档位

用户可选：

- `512`：快速观察；
- `1024`：默认档位，与现有分析预览惯例一致；
- `2048`：精细观察，明确提示需要更多内存和时间。

规则：

- 若原图最大边不超过档位，不放大；
- 若原图超过档位，等比例缩小，宽高至少为 1；
- 频率分析必须使用面积平均或等价的抗混叠缩小，不使用最近邻；
- UI 显示“原图尺寸”和“实际分析尺寸”，避免用户误认为频谱来自全分辨率；
- PNG 导出固定使用分析代理尺寸，文件名和状态信息必须明确该事实。

### 6.3 补零与内存上限

- 分析代理生成后，宽高分别补零到不小于自身的最小 2 的幂；
- 补零值为选择通道的中性值：RGB/Y 使用 0，Cb/Cr 使用 128；
- 最大档位保证补零尺寸不超过 `2048×2048`；
- 单个 `Complex[]` 最多包含 `4,194,304` 项，约 64 MiB；
- 缓存保留一份频谱，重建期间最多增加一份同尺寸工作缓冲；
- 不长期缓存独立的全尺寸幅度、相位和能量 double 数组，显示投影按需从频谱读取；
- 关闭或重新分析时先取消旧工作，再释放旧 Session 引用和 Bitmap；
- 自动测试检查结构化缓冲上限，不用易受 GC 和机器环境影响的进程峰值断言。

## 7. FFT/IFFT 数值设计

### 7.1 一维变换

使用 `System.Numerics.Complex` 实现迭代 radix-2 Cooley–Tukey：

1. 验证长度大于零且为 2 的幂；
2. 执行位反转排列；
3. 按 2、4、8……长度进行蝶形运算；
4. 正变换使用负角指数；
5. 逆变换使用正角指数，并在一维结束时除以长度；
6. 无可变实例状态，可作为 singleton 复用。

代码注释必须解释符号方向、归一化位置和位反转原因，不逐行翻译循环语句。

### 7.2 二维变换

- 二维正变换依次处理所有行和所有列；
- 二维逆变换调用相同的一维逆变换，因此总归一化为 `1/(W×H)`；
- 行、列之间使用有界临时缓冲，不为每一行分配新数组；
- 每行、每列开始前观察取消；
- 任意异常或取消都不得把半成品 Session 提交给 Document。

### 7.3 中心化与频率坐标

内部频谱保持 FFT 自然顺序。显示坐标 `(displayX, displayY)` 映射到内部索引：

```text
internalX = (displayX + paddedWidth  / 2) mod paddedWidth
internalY = (displayY + paddedHeight / 2) mod paddedHeight
```

显示中心代表 DC。向用户报告的有符号 bin 坐标为：

```text
kx = displayX - paddedWidth  / 2
ky = displayY - paddedHeight / 2
fx = kx / paddedWidth
fy = ky / paddedHeight
```

其中 `fx/fy` 单位是 cycles/pixel，范围近似 `[-0.5, 0.5)`。归一化径向频率：

```text
ρ = sqrt((fx / 0.5)^2 + (fy / 0.5)^2) / sqrt(2)
```

数值误差造成的微小越界最终裁切到 `[0,1]`。所有悬停、频带、径向曲线和遮罩必须复用同一个坐标转换器，
禁止各自在 UI 中重复公式。

### 7.4 数值门禁

- 常量输入除 DC 外应接近零；
- 单像素冲激的幅度应为常量；
- 整数周期正弦应只在预期共轭频点出现主峰；
- 棋盘格能量应集中在高频区域；
- 一维与二维 FFT/IFFT 往返最大绝对误差不超过 `1e-8`；
- Parseval 能量相对误差不超过 `1e-8`；
- 实值输入的共轭对称误差和全通 IFFT 虚部残差不超过 `1e-8`。

## 8. 频谱显示与频点检查

### 8.1 幅度模式

设幅值为 `m = |F(u,v)|`：

- 线性：`m / max(m)`；
- 对数：`log(1+m) / log(1+max(m))`，默认；
- 百分位归一化：按所有有限幅值的 P99.5 截断，再映射到 `[0,1]`。

全零输入时分母视为零并输出黑色，不允许产生 NaN。百分位使用确定性的排序或选择算法，不随线程调度改变。

### 8.2 相位模式

- 相位使用 `atan2(imaginary, real)`，范围 `(-π, π]`；
- 使用循环色相表示角度，避免 `-π` 和 `π` 在视觉上形成不连续颜色；
- 亮度由对数幅值控制，防止零能量点的随机浮点相位形成噪声；
- 幅值低于相对阈值时显示为中性暗色，悬停文字为“相位无定义”。

### 8.3 悬停信息

鼠标指向频率点时显示：

- 显示像素坐标；
- 内部 FFT 索引；
- 有符号 bin 坐标 `(kx, ky)`；
- `fx/fy` cycles/pixel；
- 归一化半径 `ρ`；
- 原始幅值；
- 相位弧度和角度；
- `|F|² / totalEnergy` 归一化能量；
- 当前所属 DC/低/中/高频区域。

悬停仅读取缓存频谱，不执行 FFT、IFFT、重新编码或文件访问。

## 9. 径向能量与频带

### 9.1 默认频带

默认边界：

- DC：严格为中心点；
- 低频：`0 < ρ ≤ 0.15`；
- 中频：`0.15 < ρ ≤ 0.50`；
- 高频：`0.50 < ρ ≤ 1.00`。

低频遮罩包含 DC，以保持平均亮度/色度；报表仍将 DC 能量单独列出。用户可以调整两个边界，但必须满足
`0 < low < high < 1`。输入非法时 UI 阻止提交，Domain 构造函数再次验证。

### 9.2 径向能量曲线

- 固定生成 256 个等宽半径 bin；
- 每个频点按 `|F|²` 累加到对应 bin；
- 曲线纵轴默认显示该 bin 占总能量的百分比；
- 报告 DC、非 DC 低频、中频和高频占比；
- 总能量为零时四项均为零，不显示虚假的 100%；
- 非零图像的各项之和在浮点容差内必须为 100%。

### 9.3 可选频带

V1 支持：

- 全部：精确短路，不执行数值重建；
- 低频：`0 ≤ ρ ≤ low`；
- 中频：`low < ρ ≤ high`；
- 高频：`high < ρ ≤ 1`；
- 自定义环带：`inner ≤ ρ ≤ outer`，满足 `0 ≤ inner < outer ≤ 1`。

遮罩值固定为 0 或 1。V1 不提供羽化、过渡带、滤波阶数或连续强度，这些参数应由后续 Frequency Filter
设计统一定义。

### 9.4 共轭对称

实值图像满足：

```text
F(u,v) = conjugate(F((-u) mod W, (-v) mod H))
```

径向遮罩天然对称，但实现仍必须通过统一的共轭索引函数测试：

- 任意保留点的共轭点必须获得相同权重；
- DC 和 Nyquist 自共轭点只处理一次；
- IFFT 后记录最大虚部残差；
- 虚部残差超出数值门禁时返回失败，不静默丢弃异常数据。

## 10. 分块 DCT 检查

### 10.1 原图坐标与块规则

- 原图面板显示的是等比例预览，但点击位置必须映射回原图像素坐标；
- 块原点固定为 `(floor(x/8)*8, floor(y/8)*8)`；
- 只有完整落在原图内的 8×8 块可分析；
- 右侧或底部不足 8 像素的余量显示“非完整 DCT 块”，不做补零、镜像或移动块原点；
- 该规则保持与现有水印只使用完整 8×8 块的语义一致；
- 小于 8×8 的图片仍可做全局 FFT，但 DCT 面板显示无可用完整块。

### 10.2 单块报告

`DctBlockReport` 包含：

- 原图块坐标和选择通道；
- 64 个通道像素值；
- 64 个 DCT 系数；
- 64 个 IDCT 重建值；
- 每个位置的绝对误差与最大误差；
- DC、低频、中频和高频分类；
- 8×8 原块和重建块的放大预览。

DCT 分类使用：

- DC：`u+v = 0`；
- 低频：`1 ≤ u+v ≤ 3`；
- 中频：`4 ≤ u+v ≤ 7`；
- 高频：`8 ≤ u+v ≤ 14`。

现有水印使用的 `(2,2)`、`(3,1)`、`(1,3)`、`(3,2)` 均落在中频区，但 V1 不从水印 Infrastructure
读取私有映射，也不显示具体载荷位置。

### 10.3 分块 DCT 全图投影

- 对分析代理中的每个完整 8×8 块执行 DCT；
- 将对应系数的 `log(1+abs(value))` 放回块内相同 `(u,v)` 位置；
- 全图使用一致的显示归一化，不能逐块各自拉伸造成误导；
- 非完整边缘区域使用明确的棋盘或中性色标记；
- 支持六通道；
- 修正当前调用层重复减 128 的问题，并通过常量块只有 DC 非零的测试锁定。

## 11. 频带重建

### 11.1 固定处理链

```text
缓存原始复数频谱
    ↓ 复制到工作缓冲
根据统一频率坐标生成径向遮罩
    ↓
对每个频点乘以 0/1 权重
    ↓
验证共轭对称
    ↓
二维 IFFT
    ↓
裁剪到分析代理尺寸
    ↓
读取实部并检查虚部残差
    ↓
按选定通道合成 RGBA
    ↓
生成遮罩预览、重建预览和裁切/残差报告
```

### 11.2 实时交互与迟到结果

- 频带选择立即触发重建；
- 连续拖动边界使用约 150 ms 防抖；
- 新参数到达时取消旧重建；
- 每次操作携带递增 generation；
- 只有 generation 与当前 Session、通道、档位和参数全部一致时才能提交；
- 取消显示“已取消”但不清除最后一个有效结果；
- 新源图、通道或档位会使旧频谱和重建全部失效；
- 只切换幅度显示模式、相位视图或悬停点时不得重新 FFT。

### 11.3 导出

- 只允许导出当前有效重建结果；
- 输出格式固定 PNG，保持重建图 Alpha；
- 建议文件名：`{source}.frequency-{channel}-{band}-{width}x{height}.png`；
- 通过 `IImageCodec` 编码，再由 `IAtomicFileWriter` 发布；
- 保存对话框取消不算错误；
- 写入失败保留内存结果，可再次选择路径；
- 状态栏明确“已导出分析代理，不是原尺寸图片”。

## 12. 界面与交互设计

### 12.1 总体布局

```text
┌──────────────────────────────────────────────────────────────────────────────┐
│ 选择图片 | 路径 | 通道 | 512/1024/2048 | 分析 | 取消 | 导出 PNG             │
├───────────────────────────────┬───────────────────────────────┬──────────────┤
│ 原图                          │ 频谱                          │ 检查与参数   │
│ 十字准线 + 8×8 块框           │ FFT 幅度/相位/DCT + 频带圆环   │ 块矩阵       │
├───────────────────────────────┼───────────────────────────────┤ 径向能量     │
│ 遮罩                          │ 重建图                        │ 频带选择     │
│ 黑白共轭对称频带              │ 当前通道 IFFT 结果             │ 频点详情     │
├───────────────────────────────┴───────────────────────────────┴──────────────┤
│ 状态、原图尺寸、分析尺寸、进度、虚部残差、裁切像素数                        │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 12.2 交互规则

- 未选择图片时只允许“选择图片”；
- 已选择但尚未分析时允许修改通道和档位；
- 分析期间禁用会启动第二次同类操作的按钮，但允许取消；
- 选择新图片后立即清除旧 Session 和导出资格；
- 原图点击更新 DCT 块，悬停不改变持久选择；
- 频谱悬停显示 tooltip/侧栏，不改变频带；
- 幅度、相位、DCT 是同一频谱面板的视图切换，不同时创建三份大型 Bitmap；
- 低/中频边界使用有界数值输入或 Slider，并显示实际数值；
- 自定义环带只在选择“自定义”时出现；
- 所有颜色标注同时带文字或图例，不能只依赖颜色区分频带；
- 错误使用用户可理解的中文消息，详细异常只进入测试或开发诊断。

### 12.3 View 与代码隐藏边界

- AXAML 负责布局、样式和绑定；
- 轻量自定义 Control 负责曲线与覆盖层绘制；
- code-behind 只把 Pointer 位置转换成归一化 View 坐标并转发给 Document；
- code-behind 不读取文件、不执行算法、不管理取消源、不直接修改 Domain；
- 复杂坐标换算进入可测试的独立映射器；
- 所有绑定使用 `x:DataType`，保持编译绑定门禁。

## 13. G0–G7 实施包

### G0：产品、数学与资源基线

目标：在生产代码之前冻结所有会影响结果解释和兼容性的事实。

交付：

- 审阅并冻结本文；
- 创建 `docs/plan-history/spectrum-inspector/README.md` 和 G0 记录；
- 冻结通道公式、补零、中心化、频率坐标、能量和频带定义；
- 冻结 512/1024/2048 档位、缓冲上限和 PNG 代理导出语义；
- 冻结 Persistable Document、稳定 ID、快照字段和显式重新分析策略；
- 记录不新增 NuGet、不使用 AIFLOW、不执行发布门禁。

门禁：计划中的公式、默认值、错误语义、范围和延期项无未决选择。

### G1：通道与分析代理

目标：建立独立于 UI 和 FFT 的六通道图像基础。

交付：

- `ImageChannel`、通道平面和六通道转换；
- RGB 与 YCbCr 选定通道重建；
- 抗混叠分析代理；
- 原图坐标、代理坐标和像素所有权模型；
- 文件对话框端口接口隔离；
- Alpha、裁切、源对象不变和档位测试。

门禁：六通道确定性测试通过，现有水印编解码和质量测试无回归。

### G2：FFT/IFFT 公共核心

目标：先证明数值变换正确，再允许上层使用。

交付：

- 一维 radix-2 FFT/IFFT；
- 二维行列变换、补零、裁剪和中心化坐标；
- 可取消循环和受控临时缓冲；
- 常量、冲激、正弦、棋盘格、Parseval 和往返 Golden Vector；
- 中文数学与所有权注释。

门禁：第 7.4 节全部数值门禁通过，取消和缓冲上限有测试证据。

### G3：频谱与 DCT 分析

目标：完成不修改图片的全部观察能力。

交付：

- 分析 Session 和全局频谱结果；
- 线性、对数、百分位幅度投影与相位投影；
- 悬停频点 DTO 和统一坐标转换；
- 256 bin 径向能量与频带占比；
- 六通道分块 DCT 全图投影；
- 单块像素/DCT/IDCT 报告；
- 现有 DCT 预览双重中心化修正。

门禁：频点、能量、DCT 和边缘块自动测试通过；水印协议输出不变。

### G4：频带遮罩与重建

目标：完成可解释、实值安全的径向频带实验。

交付：

- 默认频带和自定义环带值对象；
- 共轭对称遮罩与遮罩预览；
- IFFT、裁剪、通道合成和重建报告；
- 全通短路、DC-only、低/中/高频合成图测试；
- generation、取消和工作缓冲释放。

门禁：全通逐字节一致，共轭/虚部/能量门禁通过，Alpha 无变化。

### G5：Document 与持久化生命周期

目标：把算法接入真实 scoped Document，而不破坏分层。

交付：

- `SpectrumInspectorDocument` 与三个应用用例；
- 选择、分析、取消、块检查、频带重建和导出命令；
- Dirty/Revision、schema 1 快照和恢复；
- Session/Bitmap 所有权、关闭取消和迟到结果保护；
- 防抖和最后 generation 获胜；
- 缺失文件、非法快照和导出失败状态。

门禁：多 Scope 隔离、快照、取消、关闭、迟到结果和资源释放测试通过。

### G6：View、Standalone 与输出闭环

目标：完成真实可操作界面和开发预览。

交付：

- 编译绑定的 Spectrum Inspector View；
- 原图块框、频谱频带圈、遮罩、重建和侧栏；
- 径向曲线轻量 Control 与频点提示；
- Module 第三个 Persistable Document 注册；
- Standalone 第三个真实预览页；
- 原子 PNG 导出与正式回读验证。

门禁：Headless View、坐标边界、组合根、Standalone DI 和 PNG 回读测试通过。

### G7：本地集成与开发封板

目标：完成当前非发布阶段能够诚实证明的全部质量工作。

交付：

- Debug/Release restore、build、test 全部通过；
- 更新根 README、开发文档索引、公共图像领域边界和未来能力状态；
- 新增频域分析器用户指南和测试门禁文档；
- G0–G7 实施记录填写实际证据；
- Standalone 手工检查 512/1024/2048、六通道、悬停、块检查、频带和导出；
- 记录 Windows CI、ZIP、真实 Host 和发布封板延期。

门禁：所有文档只陈述真实执行过的结果，不以 Standalone 替代 Host 验收。

## 14. 预计代码与文档落点

### 14.1 生产代码

```text
src/ImageLabPlugin.Plugin/
├─ Application/
│  ├─ Ports/
│  │  └─ ImageLabPorts.cs                 拆分图片与 Payload 文件端口
│  └─ SpectrumAnalysis/
│     ├─ SpectrumAnalysisContracts.cs     请求、结果与用例接口
│     └─ SpectrumAnalysisUseCases.cs      分析、块检查和重建工作流
├─ Constants/
│  └─ PluginIds.cs                        新增稳定 DocumentTypeId
├─ Domain/
│  ├─ Imaging/
│  │  ├─ ImageChannel.cs                  六通道枚举与平面语义
│  │  ├─ ImageChannelConverter.cs         抽取与选定通道重建
│  │  └─ ImageAnalysisProxyProjector.cs   抗混叠分析代理
│  └─ Frequency/
│     ├─ Fft1DTransform.cs
│     ├─ Fft2DTransform.cs
│     ├─ FrequencyCoordinates.cs
│     ├─ FrequencySpectrum.cs
│     ├─ SpectrumProjector.cs
│     ├─ RadialEnergyAnalyzer.cs
│     ├─ FrequencyBandMaskFactory.cs
│     └─ DctBlockAnalyzer.cs
├─ Features/
│  └─ SpectrumInspector/
│     ├─ SpectrumInspectorDocument.cs
│     ├─ SpectrumInspectorView.axaml
│     ├─ SpectrumInspectorView.axaml.cs
│     └─ 必要的轻量绘制 Control
└─ Plugin/
   ├─ ImageLabPluginModule.cs
   └─ ImageLabPluginServices.cs
```

这是职责落点，不要求为了文件数量机械拆分。实现时可以把紧密且短小的值对象合并，但不得把 Domain、用例、
Document 和 View 重新塞进同一个类。

### 14.2 测试

建议在现有测试项目中按职责新增：

- `ChannelAndAnalysisProxyTests`；
- `FftTransformTests`；
- `SpectrumAnalysisTests`；
- `FrequencyBandReconstructionTests`；
- `SpectrumInspectorDocumentTests`；
- 对现有组合、生命周期和 Headless View 测试做增量扩展。

测试文件名可以根据现有规模合并，但失败输出必须能够区分数值、用例、Document 和 UI 层。

### 14.3 专用文档

```text
docs/
├─ design/
│  └─ spectrum-inspector-v1-implementation-plan.md
├─ spectrum-inspector-user-guide.md
├─ spectrum-inspector-testing.md
└─ plan-history/
   └─ spectrum-inspector/
      ├─ README.md
      └─ g0-... 至 g7-...
```

## 15. 自动测试与质量门禁

### 15.1 Domain 与数值

- 非法尺寸、非 2 的幂、空缓冲和不匹配缓冲必须安全失败；
- 六通道抽取、重建、Alpha 和源对象不可变；
- 512/1024/2048 代理尺寸、纵横比和不放大小图；
- FFT 常量、冲激、正弦、棋盘格 Golden Vector；
- 1D/2D 往返、Parseval、共轭对称和虚部残差；
- 中心化与有符号频率坐标的中心、四角和 Nyquist 边界；
- 三种幅度显示的零输入、极端动态范围、NaN/Infinity 防御；
- 径向 256 bin、默认边界、自定义环带和能量总和；
- DCT 定值块、已知系数、IDCT 往返、频带分类和非完整边缘块；
- 当前 DCT 投影不得再次执行额外的 `-128`。

### 15.2 频带与重建

- 全通返回逐字节一致的代理；
- DC-only 输出选定通道的常量平均值；
- 单频输入在保留对应频带时存在、阻断时消失；
- 每个遮罩点与其共轭点权重相同；
- 自共轭 DC/Nyquist 点处理正确；
- R/G/B 未选通道与 Alpha 不变；
- Y/Cb/Cr 裁切统计与 RGB 结果可重复；
- 取消不会提交半成品；
- 连续重建只有最后 generation 可以覆盖结果。

### 15.3 Document、UI 与组合

- Module 贡献顺序固定为三个 Persistable Document、零个 Tool；
- 不同 Scope 的源路径、Session、频带和取消互不影响；
- 快照只保存轻量参数，schema 1 可往返；
- 非法或未知快照安全回退；
- 恢复不自动启动 FFT；
- 选择新图片、通道或档位使旧结果失效；
- 只切换显示模式不重新执行 FFT；
- 关闭取消分析/重建并拒绝迟到结果；
- Headless 环境加载 View、编译绑定和关键交互 Control；
- 坐标映射覆盖黑边、面板边界、最后像素和非完整 DCT 块；
- 导出按钮只在有效重建后可用；
- PNG 使用正式编解码器回读并验证尺寸、像素和 Alpha。

### 15.4 回归与资源

- 现有 44 个测试必须全部继续通过；
- 水印三种 Profile、PNG/JPEG 回读、DCT-QIM 和协议 Golden Vector 不得变化；
- 最大复数缓冲、工作副本数和补零尺寸有结构化测试；
- 取消检查位于行、列、块和像素长循环；
- 不使用机器相关的严格毫秒断言作为单元测试门禁；
- 2048 档的实际耗时与峰值资源在 G7 本地记录中据实填写，但不写市场承诺。

### 15.5 本地回归命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

测试总数只能在实现完成后更新到测试文档。不得为了达到预期数量拆分无意义测试，也不得通过放宽数值断言掩盖
真实回归。

## 16. 人工验收场景

### 16.1 基本分析

1. 打开两个频域分析器实例并选择不同图片，确认状态完全隔离；
2. 分别切换 R/G/B/Y/Cb/Cr，确认频谱、DCT 和重建联动；
3. 对大图切换 512/1024/2048，确认实际代理尺寸和资源提示正确；
4. 切换线性、对数、百分位、幅度、相位和 DCT，确认不会重复执行 FFT；
5. 在频谱中心、轴线、边缘和角点悬停，核对坐标与频带；
6. 查看径向曲线和 DC/低/中/高能量占比。

### 16.2 DCT 块

1. 点击原图中心，确认 8×8 块框与矩阵坐标一致；
2. 点击右侧/底部非完整边缘，确认明确提示且不伪造填充块；
3. 在六通道间切换，确认块像素和 DCT 系数同步变化；
4. 检查 IDCT 重建误差和 DC/低/中/高频标注。

### 16.3 频带重建与导出

1. 依次选择全部、低频、中频、高频，确认典型模糊、结构和细节变化；
2. 连续拖动自定义环带，确认界面保持可响应且最终结果对应最后参数；
3. 对 R/G/B 和 Y/Cb/Cr 分别重建，确认 Alpha 保持；
4. 导出 PNG 并重新打开，确认尺寸是分析代理而不是原图；
5. 保存失败后重新选择路径，确认内存结果仍可用；
6. 分析期间取消或关闭 Document，确认没有迟到结果和异常窗口。

### 16.4 Standalone 边界

Standalone 可以证明：

- Module、DI、View、绑定、命令和插件内部对象图可工作；
- 多 Document Scope 隔离；
- 算法取消、导出和 Bitmap 生命周期；
- 主要交互在本地 Avalonia 窗口可用。

Standalone 不能证明：

- 真实 Host Catalog、Dock、布局恢复和插件卸载；
- AssemblyLoadContext 与发布依赖闭包；
- 正式 ZIP、Windows CI 或目标用户设备性能。

## 17. 兼容、迁移与回滚

### 17.1 兼容规则

- 两个既有水印 Document ID、快照 schema 和水印 V1 线格式不变；
- 新增频域分析器 ID 发布后不得更改；
- 新 Document 的快照 schema 只保存自身参数，不引用水印快照；
- `Dct8x8Transform` 的输入仍是原始 `[0,255]` 空间值，变换内部统一减 128；
- 修正 DCT 预览双重中心化后，应接受预览图变化，但水印输出和提取结果必须保持；
- 不新增 NuGet，因此中央版本和插件私有依赖清单原则上不变。

### 17.2 功能回滚顺序

若某阶段无法达到门禁，按以下顺序回滚，不保留半成品入口：

1. 隐藏尚未稳定的 UI 入口并移除 Module 贡献；
2. 移除 Document、View 和应用用例；
3. 仅当公共 FFT/通道代码已经有独立测试且不影响水印时才可保留；
4. 文件端口拆分必须整体回滚或整体完成，不能让旧 Document 使用两套事实源；
5. 不回退两个水印 Document、协议或已有测试门禁；
6. 文档如实记录未完成阶段和回滚原因。

### 17.3 无数据迁移

V1 只新增 Document 类型，尚无旧 Spectrum Inspector 快照需要迁移。开发期若修改 schema，可以清除开发快照；
首次发布后任何 schema 变化必须增加版本并保留旧版本恢复路径。

## 18. 注释与实施纪律

- 所有新增注释使用中文；
- 复杂类的 XML `<remarks>` 说明设计目的、坐标系、所有权、线程和取消边界；
- FFT、中心化、能量归一化、共轭索引和 YCbCr 裁切必须有公式或设计说明；
- 不给显而易见的属性访问器和简单赋值堆砌无价值注释；
- 不通过继承层次、反射、服务定位器或多层 Strategy/Factory 炫技；
- 接口只建在真实的替换边界和应用用例边界，不为每个纯值对象创建接口；
- 不在 ViewModel 中写像素循环，不在 Domain 中创建 Avalonia Bitmap；
- 不使用静态可变缓存，不让一个 Document 访问另一个 Document；
- 不吞掉 `OperationCanceledException` 以外的异常，不把异常堆栈直接展示给用户；
- 不修改用户已有的未提交文档或代码，发生重叠时合并而不是覆盖；
- 每个 G 包先补测试，再提交能力，最后填写实际实施记录；
- 当前阶段不增加 Windows CI 和发布门禁。

## 19. V1 开发封板检查清单

### 产品与交互

- [ ] 第三个贡献是 Persistable Document，不是 singleton Tool；
- [ ] 六通道、三档分辨率、三种幅度模式、相位和 DCT 全部可用；
- [ ] 原图、频谱、遮罩、重建和检查侧栏正确联动；
- [ ] 悬停和块选择的原图/代理/频率坐标可解释；
- [ ] 径向曲线和四类能量占比正确；
- [ ] 全部/低/中/高/自定义环带可重建；
- [ ] PNG 明确按分析代理尺寸导出。

### 架构与生命周期

- [ ] Domain、Application、Feature、Infrastructure 依赖方向正确；
- [ ] Document 不直接执行 FFT/DCT 或文件编码；
- [ ] 文件端口满足接口隔离；
- [ ] 多 Scope 状态完全隔离；
- [ ] Session、工作缓冲和 Bitmap 所有权明确；
- [ ] 取消、关闭、防抖和迟到结果保护均有测试；
- [ ] 快照不包含大型派生数据，恢复不自动执行 FFT。

### 数值与资源

- [ ] FFT/IFFT、Parseval、共轭和虚部残差门禁通过；
- [ ] 六通道和 Alpha 门禁通过；
- [ ] DCT 双重中心化问题有回归测试；
- [ ] 2048 档不突破规定补零和缓冲上限；
- [ ] 全通逐字节一致；
- [ ] 频带边界、能量总和和零能量输入正确。

### 测试与文档

- [ ] 现有 44 个测试全部保持通过；
- [ ] 新增 Domain、重建、Document、UI、组合和 PNG 回读测试；
- [ ] Debug/Release 本地门禁通过；
- [ ] 用户指南、测试门禁、文档索引和公共领域边界已同步；
- [ ] G0–G7 记录包含真实数据、偏差、风险和回滚；
- [ ] 文档没有宣称已执行真实 Host、ZIP、Windows CI 或发布封板。

