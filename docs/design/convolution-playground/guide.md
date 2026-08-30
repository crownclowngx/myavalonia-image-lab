# 卷积核实验台使用与开发指南

## 稳定身份与所有权

- Document ID：`myavalonia.plugin.image.lab.document.convolution-playground`。
- 每个 DI Scope 独占路径、参数、Session、代理/完整结果、取消源和 Bitmap。
- 快照 schema 为 1，只保存路径、稳定预设 ID、有限系数和轻量参数；不保存像素、raw、FFT、Bitmap 或 PNG。
- 恢复只还原路径与参数，不自动读文件、不自动卷积。

## 参数协议

| 参数 | 范围/值 | 说明 |
| --- | --- | --- |
| 核尺寸 | 3..31 奇数 | 中心锚点固定 |
| sigma | 0.1..5 | 所选半径至少覆盖一个 sigma |
| amount | 0..5 | Sharpen/Unsharp 强度 |
| High Boost A | 1..6 | DC 为 A-1 |
| Motion length | 1..K | 角度按 180° 周期规范化 |
| Emboss strength | 0..5 | 建议偏置 128 |
| 边界常量 | -1024..1024 | 按当前输入平面解释 |
| 显式除数 | `1e-12 <= abs(d) <= 1e12` | 必须有限 |
| 偏置 | -4096..4096 | 只在除法/组合之后应用 |
| 代理档位 | 512/1024/2048 | 小图不放大 |

## 通道

`Rgb` 分别处理 R/G/B，Alpha 原样复制。`Red/Green/Blue` 只替换该字节。`Luma/ChromaBlue/ChromaRed` 使用公共全范围 YCbCr 公式，保留另外两个分量后转回 RGB；合法分量组合仍可能超出 RGB 色域，所以另行报告回写裁切像素数。

## 状态与并发

- 图片或任何数学配方变化都会推进 generation，取消旧预览/完整尺寸/导出任务。
- 文本核使用约 200 ms 防抖；无效草稿不启动计算。
- 选点只复算一个像素，不重新卷积。
- 幅值/相位切换只重新编码已有 256² 响应，不让完整结果过期。
- 完整结果记录 recipe fingerprint；任何数学参数变化都会清空它，导出用例还会再次核对指纹。
- 取消或失败不提交半成品；关闭顺序为推进代次、取消、释放 Session，再释放 Bitmap。

## 错误分类

界面会分别暴露路径不存在/解码失败、矩阵行列或数字错误、预设参数越界、归一化除数非法、计算取消、编码/写入失败和过期结果。参数错误保留已有可观察结果，但旧结果不会被贴上新参数标签。

## SOLID 落点

- `ConvolutionKernel` 只维护不可变核事实；Parser 只处理文本；Factory 只生成预设。
- `BorderIndexMapper`、`SpatialConvolver`、`GradientCombiner`、频响、差异和探针各自单责。
- 六个应用接口分别准备 Session、预览、探针、响应、完整执行和导出，避免万能图片服务。
- Domain 不引用 Avalonia/文件/JSON/DI；Application 不返回 Bitmap；View 不写算法。
- 朴素模式仅使用 Value Object、Factory、Session 与 Use Case；没有反射发现、Mediator、事件总线、Repository 或通用 DAG。

## 资源限制

完整输入仍受公共 `ImageSize` 的 16,000,000 像素限制；代理最大 2048²；频响固定 256²。通用二维路径复杂度为 `O(W*H*K²*C)`，31×31 RGB 完整图可能很慢，界面在开始前显示乘加估计并提供取消。V1 不承诺 GPU/SIMD 或实时完整尺寸处理。
