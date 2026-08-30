# 频域滤波使用与开发指南

## 产品身份与闭环

- Document ID：`myavalonia.plugin.image.lab.document.frequency-filter`。
- 形态：多实例 Persistable Document；不是 Tool、Workflow Action 或 Workbench Command。
- 输入：显式选择 PNG/JPEG；通道为 R/G/B/Y/Cb/Cr；代理档位为 512/1024/2048。
- 组合：低通/高通/带通/带阻 × Ideal/Butterworth/Gaussian。
- 输出：Direct、Centered、Additive；导出名包含输出模式及 full/proxy 尺寸语义。

先“载入并缓存 FFT”，再调节参数。图片、通道或代理档位变化会释放 Session；数学参数变化会使代理、完整尺寸和空间比较结果过期；只改输出模式时 Application 复用同一 raw IFFT；只改核尺寸时仅空间比较过期。

## SOLID 落点

Domain 中配方、响应、遮罩、执行器、投影器、诊断器和空间比较器各只有一个变化原因；Application 提供准备、应用、空间比较、完整尺寸和导出五个窄用例；Feature 只管理状态与显示。没有万能 Service、运行时算法发现、事件总线或插件内插件。

算法服务是无状态 singleton。`FrequencyFilterSession` 属于 Document Scope，持有一次解码、代理、通道、只读频谱和最后一个数学配方 raw 缓存；它不持有 Bitmap、View、Document 或 ServiceProvider。

## 资源与错误

代理分别补到最小 2 的幂，任何一维或样本总数不得超过共享 2048² 上限。原图只有宽高都不超过 2048 时才允许显式完整尺寸执行；实现不缩小再放大，也不分块伪造全局 FFT。

空间比较在乘加数超过 350,000,000 前阻断。滤波、空间、完整尺寸和导出分别可取消；generation 保护迟到提交。导出同时验证 Session 指纹和完整配方指纹，再调用现有 PNG 编码与原子写入端口。

## 快照

schema 1 只保存路径、通道、代理档位、方向、家族、截止、阶数、输出模式/增益、核尺寸和归一化剖面位置。恢复不保存或重建图片、Complex、mask、raw、Bitmap、耗时或取消对象，也不会自动解码和 FFT。
