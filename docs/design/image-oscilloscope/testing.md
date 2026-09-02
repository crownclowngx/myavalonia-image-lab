# Image Oscilloscope 测试与本地门禁

> 当前证据状态：V1 自动门禁已实现并于 2026-09-02 执行。Debug/Release 各 760 项通过、0 skip；两次构建均为 0 warning、0 error。真实素材人工观察、Windows CI、真实 Host 与发布门禁未执行。

## 1. 门禁原则

- 先用纯数值单元测试冻结颜色、bin、坐标与守恒，再接 Application 和 UI；
- Golden 既包含人工可算的小矩阵，也包含独立 oracle，不能用被测实现生成期望值；
- 截图和肉眼观察不能替代计数、坐标、生命周期与架构自动测试；
- 不用严格毫秒断言作为单元门禁；资源以数组尺寸、checked 预算和所有权验证为主；
- 失败输出必须能区分颜色、累计、裁切、探针、Session、Document、View 和组合层；
- 既有全部测试必须继续通过，不允许放宽旧数值容差换取绿灯。

## 2. Domain 数值门禁

### 2.1 Alpha 与颜色转换

- 黑、白、50% 灰、R/G/B/C/M/Y 纯色的 Y/Cb/Cr Golden；
- A=0、1、127、128、254、255 的白底合成与 ToEven 边界；
- 全透明不同隐藏 RGB 均得到相同白色 Scope 事实；
- HSV 黑色、灰阶、六主色、Hue 0/60/120/180/240/300 和环绕；
- Cb/Cr 理论边界、浮点 clamp、NaN/Infinity 防御；
- 输入 `PixelImage` 在分析后逐字节不变。

### 2.2 Luma Waveform

- 1×1 黑/白像素落在左下/左上正确格；
- 水平 0..255 梯度、垂直梯度、棋盘格和常量图的人工 Golden；
- 源宽 1、1024、1025 和最大合法宽度的 x 映射；
- 最后源像素必落最后 Scope 列，所有索引合法；
- 全部 density 之和精确等于像素数；
- 宽度压缩只合并列，不丢样本；
- 预取消和行中取消不返回半成品结果。

### 2.3 RGB Parade

- 纯 R/G/B/C/M/Y 的三个通道位置；
- 灰阶时三通道轨迹完全重合；
- 每通道 density 总和都等于像素数；
- 三通道共享 P99.5 量程，不能各自归一化；
- 通道顺序固定 R/G/B，UI 段与 Domain 数组一致。

### 2.4 Vectorscope

- 中性黑/灰/白全部落中心；
- 六主色坐标与独立公式一致；
- Cb 左右、Cr 上下方向不反转；
- 512 边界、中心量化、ToEven 和 clamp；
- 全部 density 总和等于像素数；
- 平均 Cb/Cr 从未量化值计算，不受 512 栅格量化影响；
- 对互补/对称颜色集合，平均向量回到预期中心。

### 2.5 直方图与颜色分布

- R/G/B/Y 四组 256-bin 各自守恒；
- S=0、1 和半饱和的 bin 边界；
- 灰阶 Hue 不进入 0°，HueDefinedCount 为 0；
- Hue 359.999° 与 0° 分属正确 bin，权重为 S；
- 色度半径 0、理论最大值和中间值固定量纲；
- 文化设置变化不改变任何结果或摘要文本中的数值序列化事实。

## 3. 裁切与显示投影门禁

### 3.1 阈值

- 默认 5/250、合法最小 0/1、合法最大 254/255；
- shadow 等于 highlight、越界和非法枚举被值对象拒绝；
- `<=shadow` 与 `>=highlight` 的包含边界测试；
- Luma 与 RGB any 语义分离，单通道高光/阴影计数准确；
- 一个像素可以同时命中不同通道的 shadow/highlight，汇总不重复误算像素数；
- 阈值改变只更新 clipping generation，不改变主分析 fingerprint。

### 3.2 覆盖层

- 代理等于源尺寸时逐像素准确；
- 缩小代理时“任一源像素命中即标记”，孤立 1 像素裁切不消失；
- letterbox/显示缩放不进入 Domain mask；
- Off/Luma/RGBAny 模式只改变投影，不修改源图或计数；
- 替换覆盖层 Bitmap 时旧对象释放，失败保留最后有效覆盖层。

### 3.3 密度投影

- 空数组、全零、单非零、均匀计数和极端离群格；
- nearest-rank P99.5 固定边界与小样本行为；
- 对数和线性公式由独立 oracle 验证；
- count=0 显示为 0，P99.5 以上 clamp 为 1；
- 投影不修改原 `uint[]`；
- 主题颜色变化只改变像素着色，不改变 tone、量程或分析 generation。

## 4. 探针与坐标门禁

- 图片显示区无缩放、等比 contain、左右/上下 letterbox；
- Pointer 在边界外返回无探针，不 clamp 到最近像素伪造读数；
- 左上、中心、右下和最后像素的像素中心规则；
- 1×N、N×1、奇偶尺寸和高 DPI 尺寸；
- 源像素到 Waveform、三个 Parade、Vectorscope、四直方图和三分布坐标；
- Hue 无定义时探针返回结构化 N/A；
- hover 不改变 Dirty/Revision，不创建后台全图任务；
- pin/清除 pin 改变 Revision，快照往返后按归一化坐标恢复；
- 更换输入后旧 pin 失效或按已冻结规则清除，不指向新图错误像素。

## 5. Application 与 Session 门禁

- 每次选择候选只解码一次、主分析只扫描一次；
- Session 独占源图、分析、代理和覆盖层，不暴露可写数组；
- 选择新图、快速连续选择、分析中取消、关闭和解码失败；
- 只有最后 analysis generation 可以提交；
- clipping generation 与 analysis generation 独立，迟到覆盖层不能覆盖新图片；
- 显示模式切换不重新解码或扫描；阈值变化不重建无关 Scope；
- 失败/取消保留最后有效 Session，并把候选错误结构化展示；
- 两个 Scope 的路径提示、阈值、探针、任务和 Bitmap 完全隔离；
- 关闭后拒绝迟到结果并释放所有大数组/Bitmap；
- 取消检查覆盖每行扫描与长投影循环。

## 6. Document、快照与 UI 门禁

### 6.1 Document 与持久化

- schema 1 仅含视图开关、密度模式、阈值、裁切模式、pin 和缩放；
- 快照不含绝对路径、源像素、计数数组、Bitmap、错误或进度；
- 合法快照往返；缺字段使用冻结默认值；非法/未知 schema 安全回退；
- 恢复不自动打开文件或启动分析；
- 参数提交推进 Revision，hover/进度/错误不推进；
- 未分析、分析中、有效、stale、失败、取消和关闭状态转换明确。

### 6.2 View 与交互

- Headless 环境加载 View 和所有自定义 Scope Control；
- 所有绑定使用 `x:DataType` 并通过编译绑定；
- 窄窗口 Tab/折叠布局和普通窗口布局都可构造；
- resize、主题切换和 Tab 切换不触发 Domain 重算；
- PointerMoved 只做坐标与探针更新，不分配全图数组；
- 键盘可选择、分析、取消、切换 Scope、pin/清除和调整阈值；
- R/G/B、阴影/高光具有文字/形状替代，不只靠颜色；
- 错误显示中文可理解摘要，异常堆栈不直接进入 UI。

### 6.3 组合与身份

- `PluginIds` 追加唯一稳定 Image Oscilloscope Document ID；
- Module 在旧二十个贡献之后追加第 21 个 Persistable Document；
- 旧二十个 ID 和顺序逐项不变；
- Tool、Workflow Action、Workbench Command 仍为零；
- Document/View/scoped Session 与 singleton 无状态算法登记符合预期；
- Standalone 创建真实独立 Scope，不复制生产 ViewModel 或服务。

## 7. SOLID、注释与架构门禁

- Domain 不引用 Avalonia、文件系统、JSON、DI、Feature 或 Plugin；
- Application 不引用具体 View/Control；
- Document/View 不出现全图像素循环、Y/Cb/Cr/HSV 公式、P99.5 或密度累计；
- 公共 Imaging/Frequency 不依赖产品专用 ImageOscilloscope 类型；
- 仅文件/解码/应用用例等真实替换边界有接口；固定 Analyzer/Projector/Mapper 为朴素 sealed 服务；
- 不出现服务定位器、反射路由、万能 Scope Engine、无消费者 Factory 或每视图一个 Strategy 注册表；
- 复杂生产类型的中文 XML 注释覆盖颜色协议、坐标、守恒、缓冲所有权、线程、取消和 generation；
- 简单 DTO 属性不以无价值注释充数；
- 可用架构扫描阻止 Domain 引用 Avalonia/IO 和 Feature 出现数值核心关键词。

## 8. 资源与回归门禁

- Waveform 不超过 `1024×256×uint`；Parade 不超过三倍；Vectorscope 固定 `512²×uint`；
- checked 预算先于数组分配，最大合法图片不会使计数溢出；
- 不为每个源像素缓存 Y/Cb/Cr/HSV，不创建 Scope-bin 倒排索引；
- 一次主分析只保留一个候选结果；提交后旧候选可回收；
- 最大尺寸通过结构门禁和取消测试；实际峰值在本地实施记录中测量；
- `dotnet test` 全部既有测试继续通过；
- 不以 1024/最大图严格耗时作为跨机器单元测试门禁。

## 9. 本地门禁命令

实现完成后在仓库根目录执行：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

结果、测试数量、skip、warning 和耗时只能在实际执行后填写。当前阶段不增加或执行 Windows CI，不执行真实 Host、ZIP、签名、安装和发布门禁。

## 10. 有限人工验收计划

- 黑白渐变、单色块、彩条、强背光、低调、过曝、欠曝和透明素材；
- 检查 Waveform 横向位置、Parade 通道分离、Vectorscope 六目标和直方图峰；
- 在源图四角、边界和典型颜色上 hover/pin，核对所有标记；
- 调整 0/1、5/250、254/255 阈值，观察计数与覆盖层；
- 快速换图、取消、双实例、关闭、窄窗口、主题切换和快照恢复；
- 记录显示可读性和交互问题，不把主观“看起来正确”替代数值测试。

Standalone 人工验收只能证明插件内部 View、绑定和对象图；真实 Host Dock、布局恢复、卸载与发布资产仍留到发布阶段。

## 11. 2026-09-02 实际执行证据

在仓库根目录按第 9 节顺序实际执行：

| 门禁 | 结果 |
| --- | --- |
| `dotnet restore ImageLabPlugin.slnx --locked-mode` | 成功，三个项目按 lock file 还原 |
| Debug `-warnaserror` build | 成功，0 warning，0 error |
| Debug 全量 test | 760 passed，0 failed，0 skipped |
| Release `-warnaserror` build | 成功，0 warning，0 error |
| Release 全量 test | 760 passed，0 failed，0 skipped |
| `git diff --check` | 成功，无空白错误 |

本次新增的图像示波器测试覆盖颜色与 Alpha Golden、五类计数守恒、灰阶 Hue、宽度压缩、取消、阈值边界、保守覆盖层、P99.5、线性/对数、共享 Parade 量程、letterbox、全 Scope 探针、六个参考目标、解码一次、clipping generation、Session 隔离、脱敏快照、Headless View、Module 顺序、SOLID 依赖和中文注释。全量数量包含仓库全部既有回归测试，不代表 Windows CI 或发布验收。
