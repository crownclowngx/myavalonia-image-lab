# G3：Gaussian 与组合

完成 3σ 奇数对称归一核、Reflect101、水平/垂直可分离卷积、`B-Gaussian(B)` 有符号高频、raw 组合、ToEven 灰度量化和裁切统计。

测试覆盖核和、常量保持、边界映射、高频正负值、gain=0 短路、双 gain=0 黑图、白底 Alpha 顺序。工作量在大缓冲分配前检查；没有改用 box blur、FFT 边界或代理放大。
