# 频谱遮罩编辑器数学原理

## 自然索引、显示索引和共轭

FFT 数组内部保持自然索引，UI 使用中心化显示坐标。所有转换复用 `FrequencyCoordinates`：

```text
internalX = (displayX + floor(W/2)) mod W
internalY = (displayY + floor(H/2)) mod H
conjugate(u,v) = ((W-u) mod W, (H-v) mod H)
```

实值输入满足 `F(u,v)=conj(F(-u,-v))`。只乘实数增益时，遮罩必须满足：

```text
0 ≤ M(u,v) ≤ 1
M(u,v) = M(conjugate(u,v))
```

`ConjugateMaskWriter` 比较线性索引；DC 和 Nyquist 自共轭点只混合一次，避免 opacity 被重复应用。

## 混合与全局强度

单次写入为：

```text
new = old + opacity × (targetGain - old)
```

一个 gesture 先形成离散命中集合，再对每个 bin 混合一次。画笔相邻采样点按半径的一半进行插值，避免快速拖动留下空洞。

全局强度不改写编辑遮罩：

```text
H(u,v) = 1 - s + sM(u,v),  0 ≤ s ≤ 1
```

所以 `s=0` 逐值全通，`s=1` 完全应用编辑遮罩，中间值是线性混合。

## 几何与频带

操作坐标位于中心化显示平面的 `[0,1]²`。矩形和圆环按离散 bin 中心判断，边界包含；画笔半径相对较短边。
频带锁定使用项目统一归一化半径 `ρ`，只有 `inner ≤ ρ ≤ outer` 时允许写入。锁定值保存在操作中，重放不读取当前 UI。

## IFFT 门禁

共享 `FrequencyMaskApplier` 复制 Session 频谱，执行 `F'=HF`，再做 IFFT。缓存频谱不原地修改。
所有复数分量必须有限，最大虚部残差不得超过 `1E-8`；超限代表共轭或数值不变量失效，结果不会提交。

## 诊断

频谱能量按 `Σ|F|²` 计算，有效能量为 `Σ|HF|²`。能量保留比只描述幅值变化。
重建后继续统计 raw 最小/最大、低于 0/高于 255 的样本、颜色回写裁切、MAE、PSNR 与全局 SSIM。
