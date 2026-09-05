# G0013 ImageLab 测试

2026-09-05 本地实施完成：全量 801 项通过、0 失败、0 跳过（本阶段新增 29 项）。
Debug 构建零警告零错误、全量格式只读检查及 Standalone 启动烟雾通过。
专项回归同时验证 Standalone 注册接口和 Scoped Handler 身份。

新增专项覆盖旧 Schema 冻结、新动作风险/输出、目录输出真实 PNG、无提交后通知、
非法文件名、未知/重复/缺失字段、参数范围、三个阶段取消、
损坏摘要/长度/版本/操作身份/marker、实际读取上限、超尺寸 PNG、输出冲突和真实 junction。
既有 Shared Domain Seed 确定性、输入/Alpha 不变及其他 Document 测试继续运行。

跨插件集成使用真实 ImageLab Module/Handler、Fractal PNG 与 Studio Runner，
不以 Fake 像素处理替代实际文件交接。Headless 仅为编解码提供平台环境。

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test ImageLabPlugin.slnx -c Debug --no-build --no-restore
dotnet format ImageLabPlugin.slnx --verify-no-changes --no-restore
```

最终计数和三个仓库的可重复门禁见 [G0013 结果](../../../../myavalonia-fractal-art/docs/refactoring/G0013/result.md)。
旧算法文件仅为通过全量格式检查统一空白，不涉及行为重构。
真实 Host、ZIP、部署、升级和发布尚未验收。
