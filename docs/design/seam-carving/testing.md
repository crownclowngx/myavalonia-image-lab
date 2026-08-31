# 内容感知缩放测试与本地门禁

## 实施基线与当前证据

实施前实跑 locked restore、Debug warn-as-error build 和 Debug test：0 警告、0 错误，520/520 通过、0 失败、0 跳过。
实现后测试总数为 587，净增 67；最终 Debug/Release 命令和结果记录在 [G9](history/g9-local-sealing.md)。

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

## 已证明

- 白/黑/原色/半透明/全透明隐藏 RGB 的白底 BT.601 亮度 Golden；
- 常量、窄边、阶跃、clamp 边界、`±1000` 偏置、显示映射与逐 double 确定性；
- 垂直/水平 DP、tie-break、非法路径、保护绕行、优先删除和 2×2 至 5×5 穷举全局最优；
- 垂直/水平删除逐字节 Golden、过期尺寸拒绝、蒙版同步；
- 影子删除插入批次、非重复源坐标、偏移修正、边界、透明隐藏 RGB 和蒙版传播；
- Auto/显式轴顺序、冻结预算、O(1) 访问量公式、阻断信息和 1 像素插入邻居拒绝；
- 双线性/双三次常量保持、中心 Golden、核权重和、目标尺寸、Alpha 和取消契约；
- 预览不修改、单步只一缝、播放/单步逐字节相同、暂停、取消、插入完成和多实例隔离；
- `seamVsReference` 同尺寸比较、JSON 隐私、PSNR 非有限表达、CSV BOM/列序；
- 第十六个唯一 Persistable Document、scoped Document/Session、singleton 算法、Standalone 与 Headless View；
- Domain 依赖方向、Document 无数值循环、中文核心注释、NuGet 不变、无 AIFLOW/Windows workflow。

## 性能与取消门禁

测试断言数组长度、步骤数、预算公式和分配前拒绝，不采用依赖机器速度的毫秒阈值。中等尺寸播放用例覆盖逐步骤
取消和不保留全部帧；未执行 16 MP 或低内存机器长测，因为产品专用预算已在 200 万像素先行阻断。

## 未证明

真实 Host、ZIP、安装/升级/卸载、Windows CI、签名、不同 GPU/DPI/系统区域、低内存矩阵、自然图片主观质量和
发布安全审查未执行。Standalone/Headless 只证明开发期对象图、绑定和控件可加载，不等于发布验收。
