# G1 核领域与目录

- 日期：2026-08-30；状态：完成。
- 新增不可变 `ConvolutionKernel`、结构化 Parser、Normalizer、Recipe 指纹和显式 `ConvolutionPresetFactory`。
- 工厂覆盖 Identity、Mean、Gaussian、Motion、Sharpen、Unsharp、High Boost、Sobel/Prewitt/Scharr、Laplacian 与 Emboss；无反射扫描。
- 测试固定尺寸/有限系数、数组所有权、四种分隔符、解析行列、Gaussian 对称、Motion 确定性、Unsharp/High Boost/DC 事实。
- 偏差：自定义核编辑使用可滚动矩阵文本框而非 961 个单元格控件，避免大核控件爆炸；行列错误仍可精确定位。
