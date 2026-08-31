# Poisson Blending／梯度域融合

状态：V1 生产实现与本地自动门禁已完成；真实 Host、ZIP、安装、签名、Windows CI 和发布验收未执行。

本能力是 ImageLab 第十六项产品能力、第十七个多实例 Persistable Document。用户显式选择源图和目标图，使用闭开矩形、
添加画笔或擦除画笔建立二值源遮罩，以整数 `(dx,dy)` 放置到目标图，然后比较直接 Alpha 合成与线性 sRGB
梯度域融合。它不做 AI 抠图、语义分割、配准、缩放、旋转或内容生成，也不使用 AIFLOW。

## 阅读顺序

1. [新手说明书](user-manual.md)：从选择两图到比较与导出。
2. [完整指南](guide.md)：模式、参数、状态、预算、取消和限制。
3. [数学原理](mathematical-principles.md)：颜色、guidance、离散方程、红黑迭代与残差。
4. [报告协议](report-schema.md)：JSON/CSV 字段、N/A 和隐私边界。
5. [测试与门禁](testing.md)：Golden、架构测试和本地命令。
6. [设计与实施计划](implementation.md)：冻结协议、SOLID 取舍和实际落地差异。
7. [阶段历史](history/README.md)：G0–G9 的开发证据。

## 固定边界

- 遮罩及源/目标一像素 halo 必须完全不透明且位于图片内部；不符合时在创建 RHS/解数组前阻断。
- 源到目标映射固定为 `tx=sx+dx`、`ty=sy+dy`；只允许整数平移。
- 三种模式是普通克隆、混合梯度和单色融合；混合梯度按整 RGB 向量择强，平局固定选源。
- 求解使用单线程、确定性红黑 Gauss–Seidel；双残差阈值必须同时满足。
- 残差收敛只证明离散方程达到阈值，不证明主观视觉质量更好。
- 快照和报告不保存绝对路径、图片像素、完整遮罩栅格、RHS、解或迭代帧。
