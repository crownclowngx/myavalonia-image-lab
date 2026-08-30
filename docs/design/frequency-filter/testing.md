# 频域滤波测试与本地门禁

## 实际结果

2026-08-31 在 .NET 10 / Avalonia 12 基线上执行：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

Debug/Release 均为 362/362 通过、0 失败、0 跳过；两配置构建 0 警告、0 错误。相对 333 基线新增 29 个 runner 用例。

## 已证明

- 三家族固定点、互补、有限值、过渡带和非法参数；
- 配方规范化、指纹稳定、遮罩数组所有权和共轭对称；
- 常量图 DC、缓存频谱不变、IFFT 虚部 `1e-8` 门禁；
- Direct/Centered/Additive 只应用一次、Alpha 保留、raw 缓存失效；
- 越界摘要上限、剖面、梯度能量、差异和全参考指标；
- 冲激响应、7/15/31 DC 修正、能量比例、Wrap/raw 空间近似与非负计时；
- 一次解码、Session 释放、完整尺寸、stale 导出和原子端口；
- 第十一个稳定 Document、两个 Scope 隔离、轻量快照与 Headless View。

## 没有证明

本轮没有设置跨机器性能阈值，没有执行真实 Host、Dock、ZIP、安装升级、Windows CI、GPU 或发布验收。Standalone/Headless 只能证明真实 View 可构造及绑定对象图可用，不能冒充 Host 证据。
