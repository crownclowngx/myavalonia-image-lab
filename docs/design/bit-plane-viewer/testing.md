# 位平面观察器自动测试与本地门禁

## 当前证据

2026-08-30 从 149 项既有基线开始，新增/扩展后仓库总数为 **191**，即增加 42 项测试用例（xUnit Theory 的每组
InlineData 按实际 runner 用例计数）。Debug 与 Release 均 191/191 通过、零失败、零跳过；两种配置 build 均
零警告、零错误。已执行 locked restore。Release 只表示第二编译配置回归，不是发布验收。

## 测试分类

- 值对象与 Golden：单位/高位/低位掩码、非法索引、`00/01/7F/80/AA/55/FE/FF` 位序。
- 通道与统计：R/G/B/Alpha 直取、BT.601 Y 量化、不可变 BytePlane、八位计数、比例、熵 0/1 边界。
- 投影与坐标：不透明单位平面、不拉伸组合灰度、五类掩码、最大边 1024、小图不放大、Uniform 黑边与首末点。
- 重建与探针：RGB 未选通道不变、Alpha 隐藏 RGB、五通道全掩码恒等、二进制/掩码/保留值。
- 用例与资源：只解码一次、切换通道复用 Session、Dispose 拒绝访问、PNG 格式固定、原子端口接收完整结果。
- Document 与集成：schema 1 轻量快照、未知 schema 回退、两个 Scope 隔离、忽略取消的旧图片迟到结果、Module 恰好七个 Document、零 Tool。
- Avalonia：第七个 View 和自绘控件 Headless 构造；正式 PNG 编解码、Document、统计和四 Bitmap 端到端闭环。
- 回归：原有水印、频谱、比较、鲁棒性和感知指纹测试均保留，没有跳过或放宽精度。

## 门禁命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

## 已证明与未证明

自动证据证明位语义、主要数值分支、分层组合、快照、Scope、Headless 构造和真实编解码闭环。资源预算通过数据结构和
最大边断言约束，没有使用机器相关严格毫秒阈值。尚未执行实施计划第 14 节有限人工清单、16 MP 目标设备性能/泄漏观察、
真实 Host、ZIP、Windows CI、安装/卸载和发布门禁；因此状态不是“已发布”。
