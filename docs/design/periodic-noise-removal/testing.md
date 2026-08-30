# 周期噪声与陷波器测试与本地门禁

## 实际结果

起始基线为 Debug/Release 408/408。完成实现后执行：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Release --no-build --no-restore
git diff --check
```

Debug/Release 均为 442/442 通过、0 失败、0 跳过；两配置构建 0 警告、0 错误；locked restore 与
`git diff --check` 通过。相对起始基线新增 34 个 runner 用例。

## 已证明

- 频率范围、canonical/conjugate、环面距离、固定舍入、配方防御性复制、32 对上限和双指纹；
- 三类 Notch 在中心/半径/边界的 Golden，0 强度全通，Butterworth 阶数和非法数值拒绝；
- 多中心最小值组合、输入顺序无关、DC 保持、增益 `[0,1]` 与 1E-12 共轭门禁；
- 常量无候选、取消无部分结果、水平/垂直/斜向合成正弦 top candidate 命中及重复运行确定性；
- 精确全强度陷波移除目标正弦，IFFT 最大虚部不超过 1E-8；
- 一次解码、只读 Session 频谱、六通道 Alpha 保持、能量/raw/差异/PSNR/SSIM 诊断和原尺寸标记；
- 草案结果硬拒绝导出、Session/Recipe stale 拒绝、结果/遮罩 PNG 原子端口；
- 严格 JSON round-trip、未知/重复字段、错误指纹、1 MiB 读取上限及候选摘要与配方分离；
- 第十三个稳定 Persistable Document、零 Tool、两个 Scope 隔离、轻量快照和真实 Document 草案状态机；
- Headless View/频谱控件/编译绑定、Standalone 真实 Module/DI；
- Domain/Document 依赖扫描、中文设计注释、NuGet 白名单、无 AIFLOW/Windows CI/发布配置。

## 没有证明

未设置跨机器耗时或工作集阈值；未执行真实 Host Catalog、Dock、布局恢复、ZIP、安装升级、Windows CI、GPU 或发布验收。
Standalone/Headless 只证明插件内部对象图、View 和绑定，不代表真实 Host 或发布完成。真实纹理风险测试只能证明风险可见，
不能证明单张图片能可靠区分噪声来源。
