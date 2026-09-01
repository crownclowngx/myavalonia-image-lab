# Spectral Art 测试与本地门禁

## 自动覆盖

- 共享回归：FFT/IFFT、Frequency Filter、Frequency Mask、Periodic Noise、面积缩放和 letterbox 既有测试不变。
- Pattern：防御性复制、指纹、非法/全零值、Alpha、阈值、反相、面积缩放、二值硬边。
- 映射/写入：ToEven、最小尺寸、20%、半平面、DC/轴/Nyquist、自共轭、Contain/Stretch、强度 0、相位与精确共轭。
- 重建/诊断：`1E-8` 虚部、crop、Y 回写、Alpha 保持、质量、2×/4×/8×差异和固定频谱量程。
- 应用/文件：单次解码、2048² 前置阻断、Session Dispose、预取消、stale、禁止覆盖源图、PNG 的 RGBA/映射/补零/共轭频谱事实内存回读后原子发布、严格 recipe。
- 组合/架构：18 个 Persistable Document、0 Tool、多 Scope、singleton 数值服务、脱敏快照、依赖方向和 NuGet 白名单。

## 实跑结果（2026-09-01）

- 起始基线：Debug/Release 629/629，0 失败、0 跳过。
- 最终 locked restore：成功。
- Debug warn-as-error build/test：0 警告、0 错误；666/666，0 失败、0 跳过。
- Release warn-as-error build/test：0 警告、0 错误；666/666，0 失败、0 跳过。
- 净增 37 个测试案例。

`git diff --check` 在最终工作区封板执行。Standalone 已通过 Release 编译和真实 Module/DI 对象图构建路径，但拖动手柄、字体外观和主观频谱可读性尚未进行人工窗口验收，延期到真实交互检查。Windows CI、真实 Host、ZIP、安装、签名和发布门禁按要求未执行。
