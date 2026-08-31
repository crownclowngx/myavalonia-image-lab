# 梯度域融合报告协议

JSON schema ID 为 `image-lab-poisson-blending-report/v1`。JSON 使用固定 camelCase 和有限数；CSV 使用 UTF-8 BOM、
固定列顺序、InvariantCulture 与 RFC 4180 转义。

## JSON 结构

- `schema/product/numericProtocol/budgetProtocol/createdAtUtc`：协议与时间事实。
- `mode/source/target/offset`：模式、两图尺寸和 SHA-256 内容 fingerprint；不含绝对路径或像素。
- `mask`：unknown、闭开包围盒、连通分量、孔洞和边界数；不含栅格或点列。
- `options/resource`：双容差、迭代、预览间隔和资源估算。
- `convergence`：停止原因、迭代数、初始/最终/最佳 RMS 和样本数。
- `diagnostics`：边界 guidance RMSE、内部梯度 RMSE、残差、混合源边比例与裁剪统计。
- `warnings/interpretation`：裁剪等结构化警告和“不等于视觉排名”的解释。

`mixedSourceEdgeRatio` 只在混合梯度模式有意义；其他模式为 JSON `null`，CSV 非适用值写 `N/A`，不得伪造为 0。
NaN/Infinity、未知枚举、空残差或超过 2,001 条残差会被拒绝。

CSV 列固定为：`iteration,rms,maxAbs,relativeRms,stopReason`。只有最后一行写实际停止原因，其余行为 `N/A`。

## 隐私和体积

报告与 Document 快照均禁止绝对路径、RGBA、缩略图、完整遮罩、RHS、当前解、残差热图和迭代帧。报告 fingerprint
用于证明输入身份，不用于恢复文件或泄露文件位置。
