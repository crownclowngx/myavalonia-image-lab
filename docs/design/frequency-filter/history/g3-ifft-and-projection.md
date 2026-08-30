# G3：IFFT 与投影

- `FrequencyFilterEngine` 只写频谱工作副本，缓存频谱保持不可变；虚部超过 `1e-8` 即失败。
- `FrequencySignalProjector` 固定 Direct、Centered、Additive 的一次性偏置/叠加语义并保留 Alpha。
- 常量、DC、缓存不变和三投影测试通过。
