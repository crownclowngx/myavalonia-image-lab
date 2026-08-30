# G9：UI、专用控件与 Standalone

- 三栏 View 提供输入/参数、子带/源图/结果、统计/解释和底部状态。
- `WaveletPyramidControl` 只绘制投影 Bitmap/四象限空态，不读取或修改真实系数。
- `WaveletScanChartControl` 只绘制案例顺序下的保留系数比例；完整扫描与 benchmark 案例同时提供逐行表格等价信息，颜色不是唯一载体。
- UI 明示代理/完整尺寸、Universal 只建议、LL 禁止阈值、方向和水印结论限制。
- Standalone 通过真实 Module/DI 增加第十个独立 Scope 与页签；Headless 可实例化完整 View。
