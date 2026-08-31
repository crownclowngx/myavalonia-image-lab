# SVD Decomposition 测试与本地门禁

## 2026-08-31 本地封板证据

实施前基线为 442/442。实现后自动测试总数为 479。locked restore 成功；Debug/Release 构建均为
0 警告、0 错误；两配置测试均为 479/479、0 失败、0 跳过；`git diff --check` 通过。
本文只记录本地开发门禁，不代表 Windows CI、真实 Host、ZIP、安装或发布完成。

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

## 新增门禁覆盖

- `DenseMatrix` 行列、转置、有限值、样本上限、防御复制和 `SvdResourceEstimate` 的单/三通道边界。
- 1×1、1×N、N×1、2×2 对角/单位/零/非正交、rank-1、高/宽互转的手算或构造 Golden。
- 128×128 与 256×256 方阵在冻结样本上限内完成有限分解；不设置脆弱的绝对毫秒断言。
- 奇异值降序、经济型 U/V 尺寸、完整重建、正交诊断、确定性符号、取消和结构化 NotConverged。
- k=0、中间值、full-rank、越界；能量/误差单调、Parseval/Frobenius、全零 NotApplicable。
- 单分量 raw 正负、对称色标、能量占比和分量求和；不缓存全部分量图片。
- RGB 全秩、Alpha 保持、YCbCr neutral 只减/加一次、AwayFromZero 和裁切诊断。
- Session 解码/代理、尺寸+RGBA 指纹、缓存键不含 k、释放后阻断和三策略固定串行次序。
- 过期指纹、非 PNG、覆盖源路径阻断；严格 JSON 无 Infinity/NaN；CSV 记录顺序和原子写入边界。
- 第十四个稳定 Document、两个 Scope 隔离、轻量快照、Standalone 与 Headless View/曲线控件。
- 源码级依赖方向、中文设计注释、NuGet 白名单、禁止误导压缩字段及非发布阶段边界。

本机观察时间：Debug 全量测试约 6 秒，Release 全量测试约 2 秒；时间不构成性能 SLA。

## 已证明与未证明

已证明的是 V1 有界矩阵协议、数值重建、缓存/生命周期、颜色与导出结构。没有证明：

- 主观观感、语义保真或某个 k 的普遍最优性；
- RGB 或 YCbCr 在所有图片上普遍更优；
- 参数数量与真实 PNG/JPEG 文件体积之间的“压缩率”；
- 512 以上大图、随机 SVD、GPU/BLAS、跨平台性能或绝对毫秒 SLA；
- Windows CI、真实 Host、ZIP、安装/升级/卸载和发布验收。
