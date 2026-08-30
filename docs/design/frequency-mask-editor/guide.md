# 频谱遮罩编辑器使用与开发指南

## 产品闭环

1. 选择 PNG/JPEG、R/G/B/Y/Cb/Cr 通道和 512/1024/2048 代理档位。
2. 单击“载入并缓存 FFT”。一次 Session 只解码一次并缓存只读频谱。
3. 在中心化频谱画布拖动。画布只提交归一化坐标，领域层负责离散化和共轭配对。
4. 等待约 150 ms 防抖重建，或单击“立即重建代理”。旧 generation 和取消结果不会覆盖新状态。
5. 检查遮罩范围、编辑 bin、共轭误差、能量保留、虚部残差、raw 越界、PSNR 和 SSIM。
6. 导出重建 PNG、遮罩显示 PNG 或版本化 JSON 配方。

## 工具语义

- 衰减画笔：把一次 gesture 命中的 bin 向目标增益混合。
- 恢复橡皮：复用相同光栅规则，但目标固定为 1。
- 矩形：拖动两角，包含边界。
- 圆环：起点为中心，拖动距离为外半径，参数“内半径比例”决定内孔。
- 频带锁定：固化进每条操作，只允许径向半径位于闭区间内的 bin 被修改。
- 反转全部：执行 `M'=1-M`，不受频带锁定影响。
- 重置为全通：作为可撤销操作写入历史，不清空旧工作。

一次 gesture 对同一 bin 最多混合一次；路径采用固定间距插值，因此结果不依赖 Pointer 事件密度。

## 状态和资源

- 路径、通道或代理档位变化会释放 Session，并要求显式重新载入。
- 配方或强度变化会让旧代理/完整结果 stale，并禁用导出。
- 强度不进入操作历史；工具参数在下一次提交时固化。
- 历史最多 128 条、单 stroke 4096 点、全配方 32768 点、JSON 1 MiB。
- 快照只保存路径、参数和有界配方；恢复不自动读图或 FFT。
- 原图补零后超出共享 2048² 预算时只允许代理结果，不做分块或缩放回填冒充原尺寸。

## SOLID 落点

- Domain：不可变 Recipe/Operation、History、Rasterizer、共轭 writer 和诊断。
- Application：Prepare、Render、Full、Probe、Recipe import/export、Image export 窄用例。
- Infrastructure：严格 DTO、有限文本读取、Avalonia 对话框和原子写入。
- Feature：Document 管状态与生命周期；Canvas 管 letterbox、Pointer capture 和轻量手势预览。

V1 使用不可变值对象、普通 sealed 服务、构造注入和有限 switch；没有 Strategy 目录、Mediator、Event Bus、
反射发现或万能文件服务。
