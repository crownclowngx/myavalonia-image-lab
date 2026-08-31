# 梯度域融合测试与本地门禁

实施前基线：locked restore 成功，Debug warn-as-error build 0 警告/0 错误，587/587 测试通过、0 跳过。

实施后覆盖：闭开矩形、ToEven、画笔覆盖、连通分量/孔洞、正负偏移、halo、Alpha 254/255、sRGB Golden、
三种 guidance 与混合平局、通道数、紧凑邻接、常量解、手算一/二未知量、单步/连续确定性、双阈值、取消回滚、
线性 Alpha、clamp、预算、JSON/CSV、N/A/非有限数、快照隐私、17 个 Document、scoped 隔离、singleton 复用、
五个专用控件、中文可访问名称和架构依赖扫描。

本地开发门禁：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

性能门禁断言数组长度、checked 工作量、分配前预算与取消，不使用机器相关毫秒阈值。自然图片观感、真实 Host、16 MP
长时间压力、ZIP、安装/升级/卸载、签名、Windows CI 和发布验收未由本地自动测试证明。

## 2026-08-31 最终结果

- locked restore 成功；锁文件无新增产品依赖。
- Debug warn-as-error build：0 警告、0 错误；629/629 通过、0 失败、0 跳过。
- Release warn-as-error build：0 警告、0 错误；629/629 通过、0 失败、0 跳过。
- 相对 587 起始基线净增 42 项自动测试。
