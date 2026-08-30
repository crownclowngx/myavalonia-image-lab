# G5 Document 生命周期记录

状态：完成（2026-08-30）。

新增 scoped `SpectrumInspectorDocument` 与分析、块检查、重建、显示投影四个窄应用用例。Document 保存轻量
配方，派生 Session/Bitmap 不入快照；恢复不自动读图。分析、重建和能量刷新分别可取消，重建约 150 ms 防抖，
generation 与 Session 引用共同阻止迟到提交。关闭会断开大型数组和 Bitmap 引用。

证据：schema 1 往返、非法参数回退、Dirty/Revision、Scope 隔离、新图片拒绝忽略取消的迟到结果测试通过。
