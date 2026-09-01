# G1：领域模型与相似变换

完成不可变归一化点、完整点对、相似变换、裁切与 recipe 值对象。`SimilarityTransformSolver` 使用中心化 dot/cross 闭式最小二乘，拒绝短基线、退化、镜像最优与越界缩放；解析逆变换有 round-trip Golden。

领域层不依赖 Avalonia、IO、JSON、DI、Infrastructure 或 Features；固定相似变换未创建 Strategy/Factory。
