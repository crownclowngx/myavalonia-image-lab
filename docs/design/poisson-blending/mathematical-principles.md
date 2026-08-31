# 梯度域融合数学原理

## 线性 sRGB

输入为非预乘 RGBA8888。RGB byte 先除以 255，再按 IEC sRGB 分段函数解码：编码值不大于 `0.04045` 时除以
`12.92`，否则计算 `((Cs+0.055)/1.055)^2.4`。方程全程使用 double；输出显式 clamp 到 `[0,1]`，编码回
sRGB 并按 `ToEven` 量化。裁剪通道和像素分别计数，因为裁剪会破坏未约束方程的精确关系。

单色模式使用线性 BT.709：`Y=0.2126R+0.7152G+0.0722B`。求得 `Ysolve` 后，将
`delta=Ysolve-Ytarget` 同量加到目标 RGB；未裁剪时亮度恰好改变为 `Ysolve`。

## Guidance

- 普通克隆：`v_pq=S_p-S_q`。
- 混合梯度：比较 `|S_p-S_q|²` 与 `|T_p-T_q|²`，按整条 RGB 向量选择，平局选源。
- 单色融合：`v_pq=Ysource_p-Ysource_q`。

反向边满足 `v_qp=-v_pq`。混合模式不能逐通道择强，否则会拼出输入中不存在的颜色方向。

## 离散方程

对每个目标求解像素 `p` 和固定左、右、上、下四邻域：

```text
4 f_p - Σ(q∈N4(p)∩Ω) f_q
  = Σ(q∈N4(p)) v_pq + Σ(q∈N4(p)\Ω) target_q
```

域外邻居是目标图 Dirichlet 边界。实现只保存 `unknown×4` 邻接索引、RHS 和解，不构造 `N×N` 矩阵。

## 红黑 Gauss–Seidel 与残差

目标坐标按 `(tx+ty)&1` 着色。一次 sweep 依次按目标 y/x 更新红点、黑点，再计算全域残差：

```text
f_p = (rhs_p + Σinternal f_q) / 4
r_p = rhs_p - (4f_p - Σinternal f_q)
rms = sqrt(Σr²/(unknown×channel))
relative = rms/max(initialRms,1e-15)
```

只有 `rms<=1e-6` 且 `maxAbs<=1e-5` 才收敛。取消时恢复 sweep 前缓冲，所以不会提交半轮；单步 N 次与连续 N 次
调用同一数值核心并逐 double 一致。曲线显示对零残差使用 `1e-15` 下限，显示归一化不反馈给方程。
