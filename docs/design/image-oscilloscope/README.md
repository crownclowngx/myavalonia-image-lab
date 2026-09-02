# Image Oscilloscope／图像示波器

Image Oscilloscope 把一张静态图片解释为离散图像信号，以 Luma Waveform、RGB Parade、Vectorscope、RGB/Y 直方图和颜色分布观察曝光、裁切、饱和度与平均色度偏移。它只分析并显示事实，不修改源图片，也不自动给出“应该怎样调色”的结论。

> 当前状态：V1 已完成生产接入与本地自动门禁，登记为第 21 个 Persistable Document，仍为零 Tool。2026-09-02 的 Debug/Release 全量测试各 760 项通过、0 skip、0 warning；真实素材人工观察、真实 Host、ZIP、签名、安装和发布门禁未执行。

## 文档入口

- [实施与验收](implementation.md)：产品范围、Host 形态、SOLID 边界、阶段顺序、资源预算和完成清单；
- [数学原理](mathematical-principles.md)：亮度、色度、Waveform、Parade、Vectorscope、直方图与裁切定义；
- [测试与本地门禁](testing.md)：单元、应用、Document、UI、架构、资源门禁和真实执行结果；
- [使用指南](guide.md)：视图含义、参数、联动、限制和错误解释；
- [新手说明书](user-manual.md)：不要求调色背景的最短观察路径；
- [实施历史](history/README.md)：Gate 实际完成后的代码落点、自动证据、偏差和延期项。

## V1 固定范围

- 单张静态图片的 Luma Waveform；
- 使用共享量程的 RGB Parade；
- 基于固定 sRGB/BT.601 语义的 YCbCr Vectorscope；
- R、G、B、Y 四组 256-bin 直方图；
- 阴影、高光的亮度与 RGB 任一通道裁切计数及诊断覆盖层；
- 饱和度分布、饱和度加权 Hue 分布、色度半径分布与平均 Cb/Cr 向量；
- 原图鼠标位置、固定像素探针与所有示波器采样点联动；
- 全图精确累计、固定大小密度栅格、可取消分析、轻量快照与多实例隔离。

V1 不处理视频、摄像头、HDR、ICC、广播 legal range、IRE、肤色自动判断、白平衡修复、LUT、调色参数或图片写回。示波器上的“色偏”只表示当前像素集合的平均色度向量，不等同于白平衡错误诊断。

## Host 形态结论

V1 规划为第 **21** 个多实例 `Persistable Document`，不登记 Tool。当前 Host 的 Tool Model 是插件级 singleton，而本能力需要：

- 每张图片拥有独立的路径提示、分析结果、鼠标探针和阈值；
- 允许两个实例并排比较不同图片；
- 关闭某个实例时准确取消任务并释放密度栅格、覆盖层和 Bitmap；
- 通过轻量快照恢复视图参数，但不自动重读本地文件。

这些职责与 scoped Document 生命周期一致。未来若 Host 提供“绑定活动 Document、可卸载缓存、可测试停靠恢复”的 Tool 生命周期，可另行设计只读 Companion Tool；V1 不同时维护 Document 与 Tool 两套状态源。

## 首要设计规定

SOLID 是首要规定：纯 Domain 负责颜色数值、累计和不可变分析事实；Application 负责解码、取消、Session 与结果提交；Document 负责实例状态和命令；View/Control 只负责布局、绘制和坐标转发。固定公式使用直白的 `sealed` 服务和强类型值对象，不建立算法插件目录、反射路由、万能 Scope Engine 或多层 Strategy/Factory。

所有新增生产代码注释使用中文。复杂颜色公式、坐标映射、密度累计、阈值边界、缓冲所有权、取消位置和 generation 提交条件必须给出详细设计思路；简单属性和显而易见赋值不堆砌无价值注释。

本能力不使用 AIFLOW，不增加 Windows CI，也不执行真实 Host、ZIP、签名、安装或发布门禁。上述事项只在正式发布阶段按公共发布文档执行。
