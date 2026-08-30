# G1 位领域基础

状态：完成（2026-08-30）。

实际新增 `BitPlaneChannel`、`BitMask8`、不可变 `BytePlane`、通道抽取器和一次扫描统计器；同时提取
`YCbCrColorSpace`，让既有 `ColorSpaceConverter`、`ImageChannelConverter` 与位平面共同使用一组系数和舍入。

设计思路：值对象负责范围与公式，抽取器只负责样本，统计器只负责计数与熵，符合 SRP；组件都是无状态小类，未使用
继承层次或复杂策略模式。中文注释重点说明位序、所有权、Y 量化和 Alpha 边界。

证据：输入复制、尺寸保护、R/Alpha/Y Golden、八位统计、熵边界、取消及既有颜色测试均通过。扫描只分配一个 byte/像素
当前通道和固定八个计数器。风险是 Y 公式变更会影响多能力；共享原语与全回归是保护边界。
