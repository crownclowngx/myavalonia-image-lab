# G1：领域模型与 packed 布局

- 增加稳定 transform/subband/projection/threshold ID、左闭右开 `WaveletRegion` 和连续层描述。
- `WaveletPyramid` 在构造时复制系数、只公开只读内存，并验证有限值、层号和四象限布局。
- `WaveletDenoiseRecipe` 归一化层/子带集合，构造期排除 LL 并生成稳定指纹。
- 所有模型位于 Domain，不引用 Avalonia、JSON、文件系统、DI 或 Host SDK。
