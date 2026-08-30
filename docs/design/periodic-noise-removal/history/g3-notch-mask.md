# G3：Notch 与遮罩

完成 Ideal、Butterworth、Gaussian 固定公式、强度和阶数边界、多陷波最小值组合。光栅化按自然共轭对一次写入两侧，
再构造共享 `FrequencyGainMask` 执行 1E-12 门禁；没有复制 IFFT 或建立 Strategy 层次。
