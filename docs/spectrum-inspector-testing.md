# 频域分析器自动测试与本地门禁

## 当前证据

频域分析器封板时基线为 68 项；仓库在图像比较实验室完成后的当前测试总数为 **97**，Debug 与 Release 配置均为
97/97 通过，零跳过；
`--locked-mode` restore 和两种配置的 `-warnaserror` build 均通过。Release 在这里仅表示本地编译配置回归，
没有执行 ZIP、Windows CI、真实 Host 或任何发布门禁。

## 覆盖范围

- 六通道冻结公式、透明 RGB、RGB 单通道替换、Alpha 保持、裁切统计和源对象不变。
- 512/1024/2048 档位验证、面积平均缩小、小图不放大和 `2048²` 复数缓冲结构上限。
- 一维/二维 FFT 往返、Parseval、常量 DC、冲激、整数周期正弦共轭峰、棋盘格 Nyquist 和实值共轭。
- 中心化坐标、cycles/pixel、归一化半径、共轭索引、三种幅度模式和零能量相位。
- 256-bin 径向能量、四区占比守恒、默认/自定义频带和每点共轭遮罩一致性。
- DCT 常量块、IDCT 往返、频带分类、非完整边缘块以及调用层不得重复 `-128` 的回归。
- 全通逐字节短路、DC-only 重建、虚部残差和 Alpha 保持。
- schema 1 快照、非法参数回退、恢复不自动分析、Revision、Scope 隔离和迟到结果拒绝。
- 四个真实 View 的 Headless 加载，以及正式 Avalonia PNG 编解码器驱动的分析—预览—重建闭环。
- Module 只按固定顺序贡献四个 Persistable Document，零 Tool；算法 singleton、Document scoped。

## 本地命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

## 尚未声称的证据

当前没有执行真实 Host Catalog/Dock/布局恢复/卸载、ZIP 内容审计、Windows CI 或目标用户设备性能测试。
Standalone 只用于本地开发预览，不能替代这些发布阶段证据。2048 档通过资源结构门禁，但本文不写市场级
耗时或峰值内存承诺。
