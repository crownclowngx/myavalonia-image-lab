# G1：颜色空间、色域与 ΔE

新增不可变 sRGB/linear RGB/XYZ D65/Lab/HSV 值与四个单责服务。sRGB 分段、标准矩阵、D65 白点、Lab
`δ=6/29`、Hue N/A、ΔE76/CIEDE2000 和 24 次 chroma 二分均有中文设计注释与 Golden 测试。
Domain 未引用 Avalonia、JSON、文件系统或 DI。
