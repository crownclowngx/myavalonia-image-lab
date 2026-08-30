# 图像比较实验室自动测试与本地门禁

## 当前证据

基线日期：2026-08-30。当前测试总数为 **97**；Debug 与 Release 均为 97/97 通过、零跳过；
`--locked-mode` restore 和两种配置的 `-warnaserror` build 是本地开发门禁。Release 仅表示本地编译配置回归，
没有执行 ZIP、Windows CI、真实 Host、安装/卸载或发布封板。

## SOLID 与结构门禁

- Domain 不依赖 Avalonia、JSON、文件系统或 DI；纯领域算法按验证、质量、直方图、基础差异、投影和像素检查分责。
- Application 只有准备、投影、像素检查和摘要导出四个窄用例；图片、报告选择、剪贴板和原子写入按意图隔离。
- Document 不执行完整像素扫描、编解码或 JSON 写入；每实例拥有独立 Session、generation、取消源和 Bitmap。
- 无状态领域组件与用例登记 singleton，Document scoped，View transient；两个 Scope 的比较路径与 Session 互不影响。
- Module 固定贡献四个 Persistable Document、零普通 Document、零 Tool；没有 AIFLOW、Workflow Action 或 Workbench Command。
- 未新增第三方 NuGet、Windows CI 或发布流程。

## 数值与领域门禁

- 完全一致、单像素最大变化、透明 RGB、Alpha-only、尺寸不符、取消和 Candidate-Reference 符号。
- PSNR-Y 使用 `N`、PSNR-RGB 使用 `3N`；零误差返回正无穷；全局 SSIM-Y 完全一致为 1。
- 既有 `ImageQualityCalculator` 映射新流式分析器，68 项水印/频域回归没有放宽阈值。
- 质量扫描只保留在线均值、二阶矩、协方差和误差累加器，不创建两张全尺寸亮度 `double[]`。
- 六通道各 256 bin 的参考/待比较计数总和均等于完整像素数，Y/Cb/Cr 舍入公式有固定断言。
- 缩小差异场的双色反向变化门禁证明“先差异、后聚合”，源 `PixelImage` 保持不变。
- RGB 差异 1/2/4/8/16/32 六档、裁切和 Alpha=255；非法倍率拒绝。
- 固定热力色表长度 256、端点差异、输入不自动归一化；MaxRGB/Y 来源独立。

## 用例、JSON 与生命周期门禁

- 参考图后候选图的顺序解码；尺寸不匹配保留双预览和结构化原因，但不建立 Session 或伪指标。
- Session Dispose 后像素检查和投影均抛出 `ObjectDisposedException`。
- schema 1 固定字段、合法非有限 PSNR、UTF-8、文件名隐私、无像素/堆栈；摘要导出委托原子写入端口。
- 快照只含路径和轻量交互参数；非法枚举、倍率、间隔、缩放和中心回退；恢复不自动比较。
- 路径变化立即失效旧结果；忽略取消的迟到结果不能提交；纯像素悬停不推进 Revision。
- Uniform 黑边、首末像素和并排双面板坐标映射；第四个 View 与两个轻量控件可在 Headless 环境加载。
- Module 贡献顺序、DI Scope 隔离、正式 PNG/JPEG 编解码和三个既有 Document 全部回归。

## 本地命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

## 资源结构证据

最大 16,000,000 像素输入长期仅保留两张完整 RGBA 图片。两张显示代理、基础差异场和当前 RGBA 投影最大边均为
1024；基础差异场每代理像素保存 R/G/B/MaxRGB/Y 五个 byte 标量。直方图为 `12 × 256 × sizeof(long)`。
长循环按行或固定步长检查取消。门禁不使用机器相关严格毫秒断言，也不以单一覆盖率百分比替代关键分支。

## 尚未声称的证据

没有执行 Standalone 全量人工交互、真实 Host Catalog/Dock/布局恢复/卸载、ZIP 内容审计、Windows CI 或目标用户设备
性能测试。因此结论是“开发实现与本地自动门禁完成”，不是“已发布”。发布时必须另行启用发布文档中的门禁。
