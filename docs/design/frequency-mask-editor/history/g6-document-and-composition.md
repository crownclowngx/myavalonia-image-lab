# G6：Document 与组合

- 新增第十二个 Persistable Document 稳定 ID；仍为零 Tool。
- Document 管 generation、取消、stale、Bitmap、轻量快照和 bounded history，不含 FFT/光栅/JSON DTO 实现。
- 两个 DI Scope 的 Document、Session、历史和结果隔离；算法继续为无状态 singleton。
