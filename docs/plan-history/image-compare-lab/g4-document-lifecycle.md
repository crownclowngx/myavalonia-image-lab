# G4 Document 生命周期记录

状态：完成（2026-08-30）。

实际修改：新增 scoped `ImageCompareLabDocument`，实现选择、交换并重比、比较/取消、复制、导出、像素检查、
轻量 schema 1 快照、Dirty/Revision、generation 与路径/Session 身份门禁。路径变化立即释放 Session、Bitmap 和摘要；
恢复只恢复配方，不自动读取大文件。闪烁计时器在离开模式、暂停、失效和 Dispose 时停止。

证据：快照/非法回退、恢复零调用、Revision、迟到结果拒绝和 Scope 隔离通过。偏差：连续投影使用取消和最新结果门禁，
没有建立无限参数缓存。风险是实际超大图交互仍需目标设备人工性能验收；结构预算已冻结。
