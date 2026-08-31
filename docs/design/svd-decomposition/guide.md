# SVD Decomposition 开发与高级使用指南

## 身份与生命周期

- Document ID：`myavalonia.plugin.image.lab.document.svd-decomposition`。
- 每个打开实例独占 `SvdSession`、源图、分析代理、分解字典、当前结果、Bitmap、generation 和取消源。
- `JacobiSvdDecomposer` 等无状态数值服务为 singleton；Document 为 scoped；View 为 transient。
- 快照 schema 1 只保存路径、128/256 档位、策略、单通道、k、分量、曲线偏好和数值协议。
- 快照不保存图片、U/σ/V、报告或比较结果，恢复时不自动读文件或启动分解。

## SOLID 分层

```text
Features/SvdDecomposition
        ↓
Application/SvdDecomposition ─→ Application/Ports
        ↓                              ↑
Domain/SvdDecomposition      Infrastructure/Persistence + Ui
```

`DenseMatrix` 只拥有行优先 double 样本；`JacobiSvdDecomposer` 只分解一个矩阵；
`SingularValueEnergyAnalyzer`、`LowRankReconstructor`、`SvdComponentProjector` 各自只负责能量、Rank 和分量；
`SvdColorStrategyExecutor` 用一个完整 switch 处理固定三策略；`SvdSession` 只负责每实例有限缓存。

V1 没有第二种 SVD 算法和第三方策略扩展，因此没有 `ISvdDecomposer`、反射目录、抽象工厂、Mediator、事件总线或通用 DAG。
接口只用于编解码、文件、报告和 Document 可替换的应用边界。

## 状态与缓存

- 载图：解码一次，调用共享 `ImageAreaResampler` 建立代理，指纹包含宽、高和 RGBA 字节；不自动分解。
- 缓存键：`proxyFingerprint + strategy + singleChannel + numericProtocol`，不含 k 或分量索引。
- 改 k/分量：100 ms debounce，推进 projection generation，取消旧投影；只从不可变因子重建。
- 改策略/通道：当前结果失效，但 Session 保留已成功策略缓存；再次分解可以命中。
- 改源图/代理：取消全部任务、释放旧 Session 与 Bitmap，建立新缓存边界。
- 失败、取消、未收敛结果不写入成功缓存；Document 关闭后任何迟到结果都不能提交。

## 资源边界

- 最大样本数 65,536，最大秩 256，单次最多三个矩阵；比较固定三案例且串行。
- `SvdResourceEstimate` 在分配前用 checked 公式估算输入、工作区、U、V、Rank 输出、转置临时区和三张 RGBA 图。
- 列对循环不使用 LINQ、闭包或临时数组；数值核心不自行 `Task.Run` 或并行列对。
- 256 档位是 V1 上限。提高到 512 不能只改常量，必须重新评审复杂度、内存、取消和交互延迟。

## 扩展规则

- 新颜色策略只有在出现真实产品需求时才扩展固定枚举和完整 switch；必须定义矩阵中心、组合、Alpha 和共同 k 语义。
- 第二种 SVD 算法只有在需要独立替换和比较时才引入窄接口；不能为了假想变化点预建框架。
- 图片导出仍须检查当前 Session、代理指纹和配方指纹，只编码 PNG并走原子写入。
- 报告 schema 新字段应向后兼容；破坏字段意义时提升 schema，而不是静默改变 V1 数值协议。
