# 梯度域融合完整指南

## 坐标、遮罩和放置

遮罩只存在于源坐标。矩形采用闭开区间 `[left,right)×[top,bottom)`；归一化画笔点按
`round(n×(length-1), ToEven)` 转换为像素，圆盘/线段在图边缘 clamp，后写笔划覆盖先写笔划。源到目标映射固定为
`(sx+dx, sy+dy)`。区域及四邻域 halo 必须同时落在源图和目标图内并且 Alpha 为 255。

## 参数

| 参数 | 默认 | 允许范围/值 |
| --- | ---: | --- |
| RMS 绝对容差 | `1e-6` | `[1e-8,1e-3]` |
| MaxAbs 容差 | `1e-5` | `[1e-7,1e-2]` |
| 最大迭代 | `800` | `[1,2000]` |
| 预览间隔 | `10` | `1/5/10/25/50` |

预览间隔只影响 UI 提交频率，不影响每轮残差、停止条件或最终 double/byte。改变模式、容差或最大迭代会使旧运行状态过期；
改变偏移必须重新预检。Build 与 Step/Run 分离，方便证明“建立问题不执行迭代”。

## 状态与停止原因

典型状态为 `Empty → ImagesReady → MaskReady → PlacementReady → ProblemReady → Running/Paused → Converged`。
停止原因包括 `Converged`、`IterationLimit`、`Canceled`、`BudgetExceeded`、`NonFinite`、`Stale` 和 `Faulted`。
达到迭代上限的图可观察，但默认不能冒充正式收敛结果导出。

## 资源门禁

- 未知量最多 500,000；遮罩包围盒最多 1,000,000 像素。
- 最大迭代 2,000；标量更新量最多 180,000,000。
- 估算峰值托管内存最多 512 MiB；所有乘法使用 checked long。
- 残差最多 2,001 条；当前实现只保留当前解和显示代理，不保存全部完整尺寸帧。

超预算会报告实际值、上限和主要原因，不会静默缩小图片、减少迭代或放宽容差。

## 生命周期与持久化

每个 Document Scope 独占一个 Session、两图、遮罩、问题、解和 generation；无状态数学服务为 singleton。
Document 使用串行闸门和取消源，完整 sweep 后才提交 UI。快照只保存文件显示名、矩形/有界笔划、偏移、模式和参数；恢复后
必须重新选择图片，不自动 IO、Build 或 Run。
