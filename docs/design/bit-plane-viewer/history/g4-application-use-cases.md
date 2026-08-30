# G4 应用用例与 Session

状态：完成（2026-08-30）。

实际建立准备、通道分析、投影/探针和导出四个窄接口。`BitPlaneSession` 拥有一张源图；
`BitPlaneChannelAnalysis` 拥有当前 `BytePlane` 与八行统计。Dispose 会切断大数组引用，释放后所有入口拒绝访问。

设计思路：用例按用户意图拆分而非按技术工具合并；编解码与原子写入通过既有端口倒置。没有万能 service、
Service Locator、事件总线或额外 NuGet。

证据：准备只解码一次、切换通道复用 Session、一次生成八份统计、投影复用分析、Dispose 拒绝访问、导出固定 PNG。
长循环按行或 65,536 样本检查取消。风险是 `Task.Run` 仍依赖进程线程池；V1 通过单 Document 串行/generation 限制并发。
