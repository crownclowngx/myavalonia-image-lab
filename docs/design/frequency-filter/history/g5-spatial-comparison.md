# G5：空间有限核比较

- 从遮罩 IFFT 派生周期冲激响应，循环搬移后截取 7/15/31 核，并只在中心修正 DC 和。
- 从既有 `SpatialConvolver` 最小提取 `ConvolveRaw`，原入口行为不变。
- 比较固定 padded/Wrap/raw double，预热后测量三次取中位数；超过 350,000,000 乘加前阻断。
