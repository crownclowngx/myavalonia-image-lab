# LSB 实验数学原理

## 容量与变化

```text
eligibleSlots = opaquePixels × channelCount
frameBytes = floor(eligibleSlots / 8)
payloadBytes = max(0, frameBytes - 20)
requiredBits = (20 + payloadLength) × 8
```

所有容量运算使用 checked `long`。bit 0 的字节差只可能为 -1/0/+1，bit 1 只可能为 -2/0/+2。MSE-RGB 对所有 RGB 样本计算，`PSNR=10 log10(255²/MSE)`；MSE=0 以结构化无变化表达，界面显示 ∞，JSON 使用空值而不是非法非有限数。

## 位分布与熵

设目标 bit 中 1 的比例为 `p`：

```text
H₂ = -p log₂ p - (1-p) log₂(1-p)
```

`p=0/1` 时相应项为 0。接近 0.5 或熵接近 1 不等于有隐写，也不等于不可检测。

## Pair of Values 卡方

只在目标 bit 为 0 的字节值上枚举一次 partner `v xor (1<<b)`。一对计数 `a,c` 的期望为 `e=(a+c)/2`：

```text
χ² = Σ((a-e)²/e + (c-e)²/e)
p = Q(df/2, χ²/2)
```

空 pair 跳过。正规化上不完全 Gamma Q 在 `x<a+1` 使用级数求 P 后取补，在其余区间使用 Lentz 连分式；实现有有限输入检查、`1e-14` 收敛容差、极小分母保护和 10,000 次上限。p 值是当前模型下的观测，不是后验隐写概率。

## 邻接、BER

同通道目标 bit 的水平右邻和垂直下邻分别累计 `00/01/10/11`：

```text
transition = (01 + 10) / pairs
equal = (00 + 11) / pairs
```

透明间断、Scope 外样本和图片边界不组成 pair；没有 pair 返回 N/A。BER 为对应 Frame bit 的汉明错误数除以实际比较 bit 数；攻击后不足的尾部不补 0。
