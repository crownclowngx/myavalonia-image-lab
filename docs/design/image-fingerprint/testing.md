# 感知指纹自动测试与本地门禁

## 自动覆盖

- 1×1 放大、2×2 面积缩小、BT.601、白底 Alpha、透明隐藏 RGB、源图不变与取消。
- 64 位位序、X16 解析、aHash `>=`、dHash `>`、水平/垂直 Golden、距离 0/1/32/64 和异算法拒绝。
- 32×32 常量 DCT、独立二维参考循环 pHash、中位数、确定性和既有 DCT/水印回归。
- 参考策略的缩放、亮度和反相离线校准清单。
- 顺序解码、异尺寸比较、Session 释放、报告路径隐私、稳定性范围/21 点上限、JPEG Alpha 阻断。
- Document 快照、恢复不读图、Revision、关闭取消、迟到 Session 拒绝与释放。
- 六个 Persistable Document、两个 Scope 隔离、Standalone/组合根和第六个 Headless View/两个控件加载。
- Robustness 同图观测距离 0、默认未启用兼容和报告可选字段。

2026-08-30 最终本地证据：locked restore 成功；Debug/Release warn-as-error build 均为零警告、零错误；Debug 与 Release test 均为 **149/149 通过、零跳过**。完整命令见 [G8 记录](history/g8-local-sealing.md)。Release 在这里只是第二编译配置回归。

未执行 Windows CI、ZIP、真实 Host、安装/卸载、目标设备性能或发布封板，因此不能声称已发布。
