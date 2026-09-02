# 2026-09-02 Image Oscilloscope V1 本地实施记录

## 完成范围

G1–G4 已实现固定白底 sRGB/BT.601 颜色协议、全图一次扫描累计、裁切重扫、保守代理覆盖层、P99.5 线性/对数密度投影、源图 letterbox 坐标和全部 Scope/bin 探针。G5–G7 已实现独占 Session、窄应用用例、独立 analysis/clipping generation、轻量 schema 1、Document 命令与 Bitmap 生命周期、折叠式 View、专用 Scope/直方图控件、Module 第 21 项贡献和 Standalone Scope。G8 已完成文档同步和本地自动门禁。

## SOLID 与模式决策

- `OscilloscopeColorConverter` 只负责颜色事实，`ImageOscilloscopeAnalyzer` 只负责主累计，`ClippingAnalyzer` 只负责阈值事实与覆盖层；
- 显示密度、栅格着色、探针和 Pointer 坐标分别由独立 sealed 服务负责；
- 仅文件/解码和 Application/UI 层间边界使用接口，没有增加 Strategy 注册表、Factory、Mediator、事件总线或服务定位器；
- Session 是完整源图、分析结果和当前覆盖层的唯一长期所有者，Document 是 Bitmap、取消源、参数和修订的唯一所有者；
- 新增生产代码使用中文 XML/设计注释，复杂公式、坐标、守恒、代理聚合、所有权和 generation 提交条件均有说明。

## 自动证据

2026-09-02 在本地依次执行 locked restore、Debug/Release `-warnaserror` build、Debug/Release 全量 test 和 `git diff --check`：

- Debug build：0 warning，0 error；
- Debug test：760 passed，0 failed，0 skipped；
- Release build：0 warning，0 error；
- Release test：760 passed，0 failed，0 skipped；
- diff check：通过。

新增测试覆盖颜色/Alpha Golden、计数守恒、Hue 灰阶语义、坐标边界、裁切、覆盖层、密度量程、探针、参考目标、Session/generation、快照、Headless View、Module 顺序、DI Scope、架构依赖、中文注释与 NuGet 白名单。既有回归测试全部继续通过。

## 实施偏差与说明

详细设计允许窄窗口使用 Tab 或折叠布局；实际采用可滚动 `Expander` 分组，以避免把多个 Scope 压缩到不可读尺寸。Vectorscope 六个参考目标由同一颜色转换和坐标公式生成，UI 只绘制结果。覆盖层用不同颜色和条纹/网格透明度形状区分高光与阴影，并同时提供文字计数。

## 未执行与延期

- 未使用 AIFLOW；
- 未增加或执行 Windows CI；
- 未执行真实素材的有限人工观察；
- 未执行真实 Host Dock/恢复/卸载验证；
- 未执行 ZIP、签名、安装和发布门禁。

这些事项不属于当前非发布阶段的完成结论，发布时应按公共发布文档另行执行。
