# Wavelet Lab 数学与数值协议

## 坐标和 packed 布局

二维正变换固定先逐行处理 X，再逐列处理 Y。子带字母采用 `(X 滤波, Y 滤波)`：

| 子带 | 含义 | packed 象限 |
| --- | --- | --- |
| LL | X 低通 / Y 低通 | 左上 |
| LH | X 低通 / Y 高通 | 左下 |
| HL | X 高通 / Y 低通 | 右上 |
| HH | X 高通 / Y 高通 | 右下 |

逆变换严格反序：先逐列撤销 Y，再逐行撤销 X。每一级只继续分解上一级 LL。测试用水平/垂直条纹冻结
LH/HL 方向，避免教材命名差异导致标签交换。

## 尺寸扩展

请求 `L` 层时，宽高向上取整到 `2^L` 的倍数。右侧和底部采用重复端点的对称扩展，例如
`[a,b,c] → [a,b,c,c,b,a,…]`。扩展只发生在 `double[]` 分析平面；逆变换后裁回原尺寸。
分配前按扩展尺寸检查 16,000,000 样本预算和整数溢出。

## Haar

令 `s = 1/sqrt(2)`：

```text
low  = (a + b) * s
high = (a - b) * s
a = (low + high) * s
b = (low - high) * s
```

所有级间计算使用 double，最终写回图片时才量化。Haar 是正交归一化变换，Parseval 能量断言只比较扩展平面和
完整 packed 系数，不把未扩展原图能量混入。

## CDF 5/3 lifting

偶样本形成 `s[i]`，奇样本形成 `d[i]`，边界缺失邻居复制端点：

```text
predict: d[i] = d[i] - (s[i] + s[i+1]) / 2
update:  s[i] = s[i] + (d[i-1] + d[i]) / 4
```

V1 不额外缩放低频/高频。逆变换先撤销 update，再撤销 predict，最后交织偶/奇样本。CDF 5/3 是双正交变换，
门禁只断言正逆重建、方向、边界和确定性，不错误套用 Haar 的 Parseval 结论。

## 阈值与噪声建议

Hard：`|c| < T` 时置零，否则保留。Soft：`sign(c) × max(|c|-T, 0)`。阈值只作用于显式选择的层和
LH/HL/HH；LL 在领域构造时被排除。最细层 HH 的建议为：

```text
sigma = median(|HH1|) / 0.67448975
T_universal = sigma × sqrt(2 × ln(N))
```

少于 4 个样本时建议明确不可用；全零 HH 返回零建议。Universal 是可见建议，不静默覆盖用户实际阈值。

## DWT 系数对差分 QIM

实验载体固定 Haar/Y 通道，从 LH/HL 生成确定性系数对。令 `d=c1-c2`，bit 0/1 分别映射到周期 `2Δ`、
偏移为 `0/Δ` 的格点；差值修正量按 `+δ/2`、`-δ/2` 分给两个系数。Frame 使用 `DWT1` Magic、
little-endian 长度和 IEEE CRC-32。CRC 只能检查意外损坏，不是认证。

DCT 适配器继续使用既有 DCT Frame、纠错和 QIM；DWT 不依赖 8×8 DCT 槽位内部实现。两者强度量纲不同，
报告必须分别解释，不能因数值相同就宣称实验强度相等。

## 图片重建

R/G/B 直接替换选中通道；Y/Cb/Cr 保留源像素的其他分量并用既有 YCbCr 公式回写。Alpha 逐字节保留。
最终样本执行有限值校验、`MidpointRounding.AwayFromZero` 和 `[0,255]` 裁切；double 最大误差与 RMS 在量化前计算。
