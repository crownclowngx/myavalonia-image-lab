# 鲁棒性实验室 G0–G9 实施记录

本目录记录 `robustness-lab-v1-implementation-plan.md` 各实施包的实际落点。实现坚持 SOLID：Domain 纯数值、Application 编排、Infrastructure 适配 JPEG/诊断/报告、Feature 只管理 Document 状态与展示；设计模式只在扰动替换点使用一个显式 Strategy。

1. [G0 产品与指标基线](g0-product-and-metric-baseline.md)
2. [G1 实验领域](g1-experiment-domain.md)
3. [G2 像素与颜色算子](g2-pixel-and-color-operators.md)
4. [G3 滤波与几何算子](g3-filter-and-geometry-operators.md)
5. [G4 JPEG 与链执行](g4-jpeg-and-chain-execution.md)
6. [G5 水印诊断](g5-watermark-diagnostics.md)
7. [G6 实验用例](g6-experiment-use-cases.md)
8. [G7 Document 生命周期](g7-document-lifecycle.md)
9. [G8 UI 与解释](g8-ui-and-explanation.md)
10. [G9 本地封板](g9-local-sealing.md)

当前结论是“开发实现与本地自动门禁完成”，不等于已发布。AIFLOW、Windows CI、ZIP、真实 Host 和发布门禁均未加入或执行。
