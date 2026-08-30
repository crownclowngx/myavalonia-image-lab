# 卷积核实验台测试与本地门禁

## 自动测试范围

- 核尺寸、系数有限性、输入/输出不可变所有权。
- 空格、Tab、逗号、分号矩阵解析和准确行列错误。
- Gaussian、Motion、Unsharp、High Boost、三类梯度和 Laplacian 代数事实。
- 非对称 impulse 真卷积方向；四种边界的正负多周期与 n=1。
- 四种归一化、近零阻断、AwayFromZero、偏置、raw 范围和两端裁切。
- RGB/单通道和 Alpha 保持；双核 Magnitude 只应用一次偏置。
- Identity/低通/导数频响 DC、双核组合、bias 不变性和 divisor 缩放。
- 绝对/有符号差异、MAE/RMSE、单核/双核像素贡献求和。
- Session 解码一次、代理/完整结果分离、过期指纹导出阻断、轻量快照和 Scope 隔离。
- 第九个贡献、Standalone 真实 Module、Headless View 和既有 241 项回归。

## 本地开发门禁

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

最终封板的实际 runner 数和输出记录在 [G9 历史](history/g9-local-sealing.md)。测试使用确定性数值或结构断言，不以机器相关毫秒数作脆弱门禁。

## 2026-08-30 实际结果

- locked restore：所有项目锁文件有效，依赖已是最新。
- Debug warn-as-error build：零警告、零错误；test 303/303 通过、零失败、零跳过。
- Release warn-as-error build：零警告、零错误；test 303/303 通过、零失败、零跳过。
- 相对起始 241 项基线净增 62 个 runner 用例；旧测试未删减、未跳过、未降低阈值。

## 已证明与未证明

本地门禁证明 .NET 10 Debug/Release 编译、领域数值、用例、快照、DI 与 Headless AXAML 回归。它不证明真实 Host、安装/升级/卸载、ZIP、不同 Windows/DPI/GPU、16 MP×31² 长时间压力或发布兼容性。本轮没有 Windows CI，也没有执行发布门禁。
