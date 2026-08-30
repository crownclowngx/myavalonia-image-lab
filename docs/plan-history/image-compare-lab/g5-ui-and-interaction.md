# G5 UI 与交互记录

状态：完成（2026-08-30）。

实际修改：新增编译绑定 View、共享 `ComparisonViewportControl` 和轻量 `ComparisonHistogramControl`。支持并排、分割、
叠加、默认暂停闪烁、RGB 差异与热力图；适应/100%/滚轮缩放、右键平移、归一化中心、准线、指针和键盘数值检查；
直方图支持六通道与 log 显示。View/code-behind 只转发 Pointer 和视口意图，不执行业务算法。

证据：第四个 View 与两个控件 Headless 加载、Uniform 黑边、首末像素和并排双面板同坐标映射通过。
风险：尚未完成真实窗口下所有缩放/高 DPI/窄窗口组合的人工走查，保留到非发布人工验收。
