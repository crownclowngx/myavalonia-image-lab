# 卷积核实验台实施历史

这些记录描述 G0–G9 实际落点与证据；当前使用方式以同级 `README.md`、`guide.md` 和 `testing.md` 为准。

| 阶段 | 记录 |
| --- | --- |
| G0 | [产品与数值基线](g0-product-and-numeric-baseline.md) |
| G1 | [核领域与目录](g1-kernel-domain-and-catalog.md) |
| G2 | [空间卷积核心](g2-spatial-convolution-core.md) |
| G3 | [预设与组合算子](g3-preset-and-composite-operators.md) |
| G4 | [响应与解释](g4-response-and-explanation.md) |
| G5 | [应用 Session 与导出](g5-application-session-and-export.md) |
| G6 | [Document 生命周期](g6-document-lifecycle.md) |
| G7 | [界面与交互](g7-ui-and-interaction.md) |
| G8 | [质量强化](g8-quality-hardening.md) |
| G9 | [本地封板](g9-local-sealing.md) |

共同回滚顺序：先从 Module 隐藏第九个贡献，再移除 Feature/Application，最后移除独立 Convolution Domain；不得回滚或改写其他八个 Document 的稳定协议。
