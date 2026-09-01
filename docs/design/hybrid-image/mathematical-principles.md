# Hybrid Image 数学原理

## 亮度

RGBA 先在白色 sRGB 背景合成，再按 `Y=0.299R+0.587G+0.114B` 转为 `[0,1]` double。Alpha 只参与白底合成，输出固定 `R=G=B、A=255`。

## B→A 相似变换

`A = s R(θ) B + t`，其中 `s>0`，不允许镜像、剪切或透视。实现先中心化两组点，用协方差 dot/cross 闭式求 `θ`，以投影长度求 `s`，再从质心恢复 `t`。缩放必须位于 `[0.1,10]`。若镜像解优于无镜像解、协方差退化或任一图最短控制点基线小于对角线 2%，结构化拒绝。

归一化点 `u` 投影到像素中心为 `u×(size−1)+0.5`。warp 对每个 A 像素中心应用解析逆变换，减去 0.5 得 B 数组坐标，再按左上、右上、左下、右下固定顺序做双线性累加。四邻点不完整即无效，不使用 Clamp/Wrap/Reflect 伪造内容。

## 有效矩形

二值有效掩码逐行累计直方图高度，以单调栈求最大面积矩形。面积相同时依次选择更靠上、更靠左、更矮、更窄。裁切为左闭右开整数矩形；recipe 保存相对 A 尺寸的归一化边界。

## Gaussian 与组合

一维核：

```text
g(x) = exp(-x²/(2σ²))
radius = ceil(3σ)
kernel = g / sum(g)
```

二维滤波先水平后垂直，边界为 Reflect101。连续理论 50% 幅度截止为 `sqrt(ln2)/(sqrt(2)πσ)` cycles/pixel；3σ 离散截断核的真实响应会略有差异。

```text
LowA  = Gaussian(A, lowSigma)
HighB = BAligned - Gaussian(BAligned, highSigma)
Raw   = lowGain×LowA + highGain×HighB
```

`HighB` 的负值一直保留。最终才计算 `roundToEven(raw×255)` 并裁切到 `[0,255]`，同时统计 raw min/max/mean、下溢和上溢。

## 多尺度与频谱

1/2、1/4、1/8 对 raw double 做确定性面积平均，再统一量化。频谱对超过 1024 的平面先建立 double 面积代理，再使用共享 FFT 基础；同屏 A、B、低频、高频与 raw 先确定一个共同幅度量程，避免各自拉伸制造虚假差异。
