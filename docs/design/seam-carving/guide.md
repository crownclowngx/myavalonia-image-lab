# 内容感知缩放精确指南

## 固定协议

| 项目 | V1 值 |
| --- | --- |
| 能量 | `seam-energy-bt601-white-matte-sobel-v1` |
| 插值 | `seam-premultiplied-srgb-even-rounding-v1` |
| 预算 | `seam-resource-budget-v1` |
| 计划 | `seam-resize-plan-v1` |
| 快照 | `image-lab-seam-carving-document-v1` |

算法以非预乘 RGBA8888 `PixelImage` 为唯一图片容器。Domain 不依赖 Avalonia、文件、JSON、Document 或 DI；
无状态服务为 singleton，图片、蒙版、计划、批次、Bitmap 和取消源由每个 Document Scope 独占。

## 计划和顺序

`Auto` 比较 `abs(Δwidth)/inputWidth` 与 `abs(Δheight)/inputHeight`，比例大的轴先执行，相等时宽先。
`WidthFirst` 和 `HeightFirst` 完成一个轴再处理另一个轴。V1 不计算二维运输图，不声称轴顺序全局最优。

## 冻结预算

| 边界 | 上限 |
| --- | ---: |
| 任一步最大工作像素 | 2,000,000 |
| 一次总缝数 | 256 |
| 单轴相对输入变化 | 25% |
| 估算单元访问 | 160,000,000 |
| 未消费插入坐标 | 8,000,000 个 `int` |
| 笔划 | 512 |
| UTF-8 快照 | 128 KiB |

访问量使用等差数列在 O(1) 时间估算；插入步骤额外计算影子规划访问。峰值字节包括三份 RGBA、蒙版、三个
double 平面、前驱、插入映射、路径、预览和 25% 安全余量。门禁是拒绝边界，不是运行时间承诺。

## 状态、取消和所有权

`Empty → Ready → Paused ↔ Playing → Completed`，取消和异常分别进入 `Canceled`、`Faulted`；参数变化进入
`Stale`。Session 由一个 Document 串行使用；Document 用同一个异步闸门保护载入和算法操作。亮度、Sobel、DP、
搬移、重采样和投影至少每行或每主轴检查取消。关闭时取消任务、递增 generation、释放 Bitmap，最后释放 Session。

## 持久化

快照保存源路径字符串、目标、选项、播放速度、显示开关和归一化笔划，不保存图片、蒙版栅格、能量、累计代价、
路径、结果或运行状态。快照超过 128 KiB 会拒绝保存。恢复不会自动文件 IO 或算法执行。

## SOLID 与模式取舍

- 能量、DP、删除、插入规划、插入、蒙版、预算、报告和 UI 各有单一变化原因；
- 只有普通缩放存在两个真实实现，因此仅它使用 Strategy；
- 单实现数值服务保持 `sealed`，不建立工厂、Mediator、事件总线、Repository 或通用 Pipeline；
- Document 依赖窄用例，领域模型以不可变值对象和受控数组所有权为主。
