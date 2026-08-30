# 频谱遮罩编辑器测试与本地门禁

## 实际结果

基线 G0 为 Debug/Release 362/362 通过。实现后本地门禁为：

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

Debug/Release 均为 408/408 通过、0 失败、0 跳过；两配置构建 0 警告、0 错误。相对 G0 新增 46 个 runner 用例。

## 已证明

- 增益范围、有限值、不可变所有权、规范指纹和严格 schema；
- 历史上限、撤销/重做、redo 清理、配方点数与 JSON 预算；
- 普通频点、DC 自共轭、画笔插值、重复 Pointer 去重、橡皮、矩形、圆环、频带锁定、反转和重置；
- `s=0/0.5/1` 强度公式、共轭误差、取消和确定性重放；
- Frequency Filter 共享核心回归、缓存频谱不变、IFFT `1E-8` 门禁和全通逐字节等价；
- 六通道回写与 Alpha、质量诊断、探针、完整尺寸和 stale 导出；
- 一次解码、Session dispose、原子端口、轻量快照和两个 Scope 隔离；
- 第十二个稳定 Document、零 Tool、Headless View/Canvas、letterbox 映射；
- 手势完成会在释放 Pointer capture 前冻结独立路径，即使释放同步触发 capture-lost 取消回调，已绘制路径仍会完整提交；
- Domain/Document 依赖扫描、中文设计注释、产品 NuGet 白名单、无 AIFLOW/Windows CI/发布配置。

## 没有证明

未设置跨机器耗时或工作集阈值；未执行真实 Host Catalog、Dock、布局恢复、ZIP、安装升级、Windows CI、GPU 或发布验收。
Standalone/Headless 只能证明插件内部对象图、View 构造与编译绑定，不能冒充真实 Host 或发布证据。
