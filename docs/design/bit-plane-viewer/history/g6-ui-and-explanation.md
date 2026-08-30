# G6 UI 与可解释性

状态：自动证据完成；有限人工交互延期（2026-08-30）。

实际新增四预览布局、bit 7→0 列表、权重/统计、四类预设、掩码文本、状态/错误、探针、说明开关和 Alpha 棋盘背景。
自绘 `BitPlanePreviewControl` 只负责棋盘、Uniform 绘制和坐标映射。Standalone 通过真实 Module/DI 加载第七个标签页。

设计思路：View 只有绑定；行模型只把单个勾选转换成统一掩码，不复制算法。文本、二进制和统计保证信息不只依赖颜色。
采用原生 Button/CheckBox/NumericUpDown 保留键盘 Tab 与激活语义，没有自造复杂控件体系。

证据：AXAML warn-as-error 编译、View/自绘控件 Headless 构造、Uniform 黑边和边界点、真实 PNG + Document + 四 Bitmap
闭环通过。键盘、高对比、快速换图和 16 MP 观察仍需按实施计划第 14 节人工执行，未写成已完成。
