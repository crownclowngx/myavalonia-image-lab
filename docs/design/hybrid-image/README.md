# Hybrid Image／混合图像

Hybrid Image 把图像 A 的 Gaussian 低频与对齐后图像 B 的有符号 Gaussian 高频组合为灰度图片。大尺寸观察时 B 的细节更明显；缩小后高频被面积平均，A 的轮廓更突出。

## 文档入口

- [使用指南](guide.md)：角色选择、控制点、裁切、参数、四尺度与导出闭环；
- [用户手册](user-manual.md)：界面字段、状态、错误和可访问操作；
- [数学原理](mathematical-principles.md)：相似变换、采样、Gaussian、量化、尺度和频谱；
- [实施基线](implementation.md)：V1 范围、SOLID 约束、Gate 和资源边界；
- [测试证据](testing.md)：自动门禁、限制与未执行项；
- [Recipe schema](recipe-schema.md) 与 [Report schema](report-schema.md)：持久协议；
- [G0–G9 历史](history/README.md)：按实际提交记录的阶段事实。

## V1 边界

- A 固定为低频主体与输出参考坐标系，B 固定为高频主体；
- 2–8 对归一化控制点求解 B→A 无镜像相似变换；
- 有效区域要求双线性四邻点完整，用户只能在默认有效矩形内收紧裁切；
- 输出固定为白底 Alpha 合成后的不透明灰度 PNG；
- 不提供自动配准、彩色、透视、非刚性变形、AI、工作流或发布能力；
- 本阶段没有新增 Windows CI，也未执行 ZIP、签名、安装或真实 Host 发布门禁。

## 设计原则

SOLID 是首要约束。Domain 只保存不可变数值事实；Application 协调取消、预算与外部端口；Infrastructure 负责严格 JSON 与原子文件；Document/View 只管理交互和 Bitmap 生命周期。固定 Gaussian 与固定相似变换没有策略、工厂或继承层。
