# G2 空间卷积核心

- 日期：2026-08-30；状态：完成。
- `BorderIndexMapper` 统一 Constant/Replicate/Reflect-101/Wrap；`SpatialConvolver` 固定行优先 `f(x-kx,y-ky)`。
- raw double、除数、偏置、AwayFromZero、字节裁切和范围统计一次生成；输入只读，取消不提交半成品。
- `ConvolutionImageProcessor` 复用公共 `ImageChannelConverter` 完成 RGB 与 R/G/B/Y/Cb/Cr；Alpha 原样复制。
- 测试含非对称 impulse、正负多周期、核大于图、近零阻断、舍入/裁切、通道与输入不变。
- 偏差：V1 全部走通用二维正确路径，未启用可分离优化；这是允许的安全回退，不影响数学结果。
