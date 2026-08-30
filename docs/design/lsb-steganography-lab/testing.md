# LSB 隐写与统计实验测试门禁

## 当前证据

起始基线为 191/191。2026-08-30 G9 最终 locked 门禁中 Debug 与 Release 均为 **241/241**、零失败、零跳过，两个配置构建均零警告、零错误；新增 50 个实际 runner 用例。191 项旧测试只作为回归基线，不冒充新能力证据。

覆盖范围：IEEE CRC 标准向量、Header 字段/端序/MSB-first、严格 UTF-8、容量边界、Alpha 跳过、RGB 顺序、SplitMix64 公开向量、两种槽位契约、四通道×bit0/1×两位置端到端、输入不变、字节变化界限、参数错配、Gamma Q 高精度值、位熵和 2×2 邻接 Golden、双重回读、PNG 导出、报告隐私、脆弱性 BER、Session 清理、快照、迟到结果、多 Scope 隔离、八个贡献和 Headless View。

## 本地命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

不新增 Windows CI，也不执行 ZIP、真实 Host、安装/卸载或发布门禁。有限人工清单和目标设备 16 MP 长时间资源观察仍属于延期事项。
