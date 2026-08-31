# G5：普通缩放与算法间差异

唯一 Strategy 变化点包含预乘 Alpha 双线性和 Catmull–Rom 双三次，均使用像素中心逆向映射与 clamp 边界。
复用 `FullReferenceQualityAnalyzer` 和差异投影，同尺寸后报告 `seamVsReference`；UI/报告明确不作质量排名。
