# G3：能量、Rank 与分量

- 完成补偿求和累计能量、数值秩、理论尾能量和直接 Frobenius 残差交叉验证。
- `LowRankReconstructor` 支持 k=0..r 且不改写因子；分量按需计算，不常驻 r 张图片。
- 分量采用对称蓝/灰/橙色标，displayScale 不进入真实重建；全零能量返回 NotApplicable。
