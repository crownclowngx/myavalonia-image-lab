# Magnitude/Phase Swap 测试与本地门禁

> 当前证据状态：2026-09-02 已完成生产接入和本地自动门禁。locked restore 成功；Debug/Release 均 0 warning；两种配置均为 739/739 通过、0 skip。未执行 Windows CI、真实 Host、ZIP、签名、安装或发布验收。

## 已执行证据

| 门禁 | 结果 |
| --- | --- |
| `dotnet restore ImageLabPlugin.slnx --locked-mode` | 通过，三个项目锁定还原 |
| Debug `build -warnaserror` | 通过，0 warning / 0 error |
| Debug tests | 739 通过，0 失败，0 skip，控制台持续时间约 6 s |
| Release `build -warnaserror` | 通过，0 warning / 0 error |
| Release tests | 739 通过，0 失败，0 skip，控制台持续时间约 3 s |
| 新增专用覆盖 | 规范画布、交换、共轭、自共轭、圆周插值、IFFT、投影、诊断、Session、严格协议、导出、快照、View、DI 与架构扫描 |

真实素材的人脸/建筑/文字主观观察与 1024 进程级峰值采样没有伪装成自动测试结论，保留到发布前人工复核；256/512/1024 的 checked 资源估算和 256 端到端数值链已自动覆盖。

## 1. 单元测试门禁

### 1.1 规范画布

- 256/512/1024 精确尺寸、居中 FitContain、内容矩形和白色 letterbox；
- 横图、竖图、1×N、N×1、同尺寸、透明/半透明和单像素边界；
- 缩小面积聚合、放大双线性、像素中心、ToEven 和文化无关指纹；
- 源 `PixelImage` 不变，越界尺寸、checked 乘法、预取消和预算前置失败；
- A/B 相同输入产生逐元素相同规范画布。

### 1.2 FFT 分量

- 常量、冲激、平移冲激、整数周期正弦、棋盘格和固定小矩阵 Golden；
- 幅度非负、相位范围、零阈值、DC/Nyquist 和中心化显示坐标；
- 相同图交换恒等；A 幅度+B 相位精确匹配两个供体；反向同理；
- 每个非自共轭点精确成对，自共轭点纯实；
- IFFT 虚部残差 `<=1e-10`，不得仅丢弃虚部后通过；
- NaN/Infinity、长度不匹配、不同画布和非法枚举结构化拒绝。

### 1.3 单分量与插值

- 零相位幅度-only 与独立公式 oracle 一致，且不偷偷 fftshift 空间结果；
- 相位-only 的单位/零幅度策略、P99.5 零中心投影和零输入；
- 幅度插值 t=0/1 精确端点、t=0.5 独立手算且始终非负；
- 相位插值 t=0/1 精确端点、跨 `-π/+π` 走最短圆弧；
- π 歧义 tie-break、共轭代表顺序、自共轭符号切换与过零计数确定；
- 不允许双插值自由组合进入 V1 Recipe；非法模式/投影组合拒绝。

### 1.4 诊断与质量

- 幅度/相位供体误差使用未投影频谱，恒等时为零；
- 相位误差忽略无定义频点并按约定幅度加权；
- NCC、梯度相关、PSNR-Y、SSIM-Y 与独立小数组 oracle；
- 常量/零方差返回明确 N/A，不产生 NaN；
- 相位-only PSNR/SSIM 固定 N/A，不误用科学投影；
- raw min/max/mean、上下裁切数、比例和显示投影一致。

## 2. 应用、Document 与 UI 门禁

- 每张输入每个 generation 只解码一次；两张 FFT 各构建一次，切换显示不重算；
- Session 独占规范画布、两张只读频谱和至多一个当前重建；临时工作频谱及时释放；
- 取消位于规范化、FFT 行/列、频点组合、IFFT、投影和质量扫描长循环；
- 快速切换模式/滑块只有最后 generation 可提交，失败/取消保留最后有效结果；
- 更换 A/B/画布使频谱、指标和导出资格 stale；只切换显示页不推进 Recipe；
- 多 Document Scope 的输入、Session、取消和 Bitmap 完全隔离；关闭拒绝迟到结果并释放大缓冲；
- schema 1 快照不含路径、像素或频谱，恢复不自动读取文件或执行 FFT；
- Headless View、编译绑定、频谱悬停、letterbox 坐标、键盘和可访问名称通过；
- Module 仅新增第二十个 Persistable Document，继续零 Tool/Workflow Action/Workbench Command；
- PNG 内存回读、原子发布、真实目标回读、禁止覆盖 A/B；Recipe/Report 严格读取和脱敏。

## 3. SOLID、注释与架构门禁

- Domain 不依赖 Avalonia、IO、JSON、DI 或 Feature；
- Document/View 不出现 FFT、复数循环、像素扫描、质量公式或 JSON 业务；
- 公共 FFT 不依赖 MagnitudePhaseSwap 产品类型；
- 只在真实替换边界建立接口，固定 mixer/interpolator/projector 使用 sealed 服务；
- 不出现服务定位器、反射路由、万能 Engine、为每个枚举建 Strategy/Factory 或无消费者的抽象层；
- 中文 XML `<remarks>` 详细说明画布、坐标、共轭、自共轭、阈值、缓冲、线程、取消和 generation；
- 简单 DTO 属性不以无价值注释充数；复杂公式注释和本文引用一致；
- 既有十九个 Document ID、schema、顺序和数值 Golden 不变。

## 4. 资源与确定性

- 1024 档峰值工作集在 G5 用实际测量记录；门禁前不得写市场承诺；
- 两张长期 `Complex[]` 是允许上限，当前组合只创建一张短生命周期工作频谱；
- 不同时缓存所有实验模式的完整 raw/Bitmap；显示投影按需创建并释放旧对象；
- 相同输入/Recipe 在 Debug/Release、当前文化差异下内容指纹和数值容差内结果一致；
- 不用严格毫秒数作为单元门禁；性能超预算时结构化拒绝，不静默降到更小画布冒充当前结果。

## 5. 本地门禁命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

以上数量与耗时来自本轮真实执行。当前阶段不加入或执行 Windows CI；也不执行真实 Host、ZIP、签名、安装和发布门禁。

## 6. 有限人工验收

- 人脸/建筑/文字至少各一组，观察两种交换、单分量和两类插值；
- 同一图、平移图、强亮度差和大量留白四类可解释边界；
- 检查 A/B/结果三频谱共享量程、相位无数据纹理和频点联动；
- 快速拖动、取消、关闭、双实例、失败导出和快照恢复；
- 记录“常见情况下相位更保留结构”的观察，不提升为普遍定理。

人工验收不能替代数值和生命周期自动门禁，Standalone 也不能替代真实 Host 发布验收。
