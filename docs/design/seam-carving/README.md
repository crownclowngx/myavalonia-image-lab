# Seam Carving／内容感知缩放

## 当前状态

V1 已完成生产实现与本地自动测试，登记为 ImageLab 第十六个多实例 Persistable Document，稳定 ID 为
`myavalonia.plugin.image.lab.document.seam-carving`。功能使用固定白底 Alpha 合成、BT.601 亮度、3×3 Sobel、
确定性动态规划、有限区域偏置和预规划插入；它不是语义分割、对象识别或自动修图。

这里的“完成”只指代码、文档和本地 Debug/Release 门禁。真实 Host、ZIP、安装/升级/卸载、Windows CI 和发布验收
仍未执行，也没有使用 AIFLOW、Workflow Action 或 Workbench Command。

## 阅读顺序

1. [新手说明书](user-manual.md)：载入、绘制、计划、预览、播放、比较与导出；
2. [精确指南](guide.md)：参数、状态、预算、取消、持久化与错误语义；
3. [数学原理](mathematical-principles.md)：Alpha、Sobel、DP、删除/插入和参考重采样；
4. [报告 Schema](report-schema.md)：JSON/CSV 字段、非有限数和隐私；
5. [测试与门禁](testing.md)：自动证据、边界和未证明事项；
6. [冻结实施基线](implementation.md)与 [G0–G9 历史](history/README.md)。

## V1 能力

- 显式载入一张 PNG/JPEG，精确改变宽、高或两者，不覆盖源文件；
- 查看基础/偏置后 Sobel 能量图和下一条垂直/水平缝；
- 使用保护、优先删除、擦除三态画笔；洋红/黄绿纹理不只依赖颜色区分；
- 逐缝删除或通过影子删除批次预规划插入，RGBA 与蒙版同步变形；
- 预览、单步、播放、暂停、取消和重置，不常驻全部 RGBA 历史帧；
- 与预乘 Alpha 双线性或 Catmull–Rom 双三次结果并排比较；
- 导出完整 PNG，以及不含绝对路径、像素或蒙版栅格的 JSON/CSV 报告；
- 保存最多 512 条归一化笔划的 128 KiB 轻量快照，恢复不自动读图或计算。

## 解释边界

低 Sobel 能量只表示亮度梯度较小，不表示语义不重要。保护和优先删除是固定 `±1000` 的有限能量偏置，必要时
路径仍可能穿过保护区。插入像素来自邻域插值，不会生成新纹理；Seam 与普通缩放的 MAE、PSNR、SSIM 只表示
两种算法输出的差异，不是审美或质量排名。
