# 鲁棒性实验室本地测试门禁

## 当前结论

鲁棒性实验室已完成本地开发自动门禁：既有 97 项基线未放宽，新增配方、随机性、算子、链、BER、诊断、报告、隐私、Document、组合根和 Headless View 测试。2026-08-30 最后一次门禁为 Debug 121/121、Release 121/121，均零跳过；两次构建均零警告、零错误。

本轮按要求没有使用 AIFLOW，没有新增 Windows CI，也没有执行 ZIP、真实 Host、安装/卸载或正式发布门禁。本地 Release 仅表示另一编译配置回归。

## 自动覆盖

- 配方：decimal 范围端点、显式列表去重、稳定哈希/排序、重复 StepId、未知扫描参数、300 案例与 1,200 观察上限。
- 随机：同种子逐字节一致、案例顺序独立、实验随机源与密码学随机源类型隔离。
- 算子：恒等值、源图不变、Alpha 规则、椒盐精确计数、缩放舍入、裁剪坐标、取消；既有真实 PNG/JPEG 回读继续回归。
- 链：显式 Strategy 唯一性与种类完整性、严格顺序、前缀观察、首次失败与失败后恢复。
- 诊断：人工 bit 翻转 Golden Vector、未扰动真实 Carrier 的两类 BER=0、Header/Data RS=0、正式提取成功。
- 聚合和报告：未完成 trial 不进分母、N/A 不混入 0、稳定 CSV、JSON/CSV 路径与敏感字段泄漏门禁。
- 生命周期：第五个稳定 Document、两个 DI Scope 隔离、轻量 schema 1 快照、恢复不自动运行、Payload/密码不持久化、步骤变化推进 Revision。
- 响应性：使用“调用后同步占用 CPU”的阻塞型基线替身，证明运行命令会立即交还界面线程；执行标识、配方锁定、可交互取消和取消后的状态恢复均有断言。
- UI：第五个真实 View、曲线和矩阵控件可在 Avalonia Headless 环境加载；图形下方存在等价数值表。

## 本地命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

## 未执行项

Standalone 完整人工交互、真实 Host Catalog/Dock/布局恢复、长时间大图性能、授权图片语料、ZIP 内容、目标设备、Windows CI 和发布验收均延期到发布阶段。Headless 自动测试不能冒充这些证据。
