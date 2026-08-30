# G3 pHash 与 DCT 复用

状态：完成。新增 `OrthogonalDctBasis` 和只计算 32×32 左上 8×8 的 `LowFrequencyDctTransform`；既有 `Dct8x8Transform` 入口和数值语义未修改。pHash 排除 DC 求 63 个 AC 的中位数，但输出保留 DC 位。常量 DCT 与独立参考循环交叉验证通过，既有水印/频域测试未放宽。
