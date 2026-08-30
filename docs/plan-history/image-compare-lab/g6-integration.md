# G6 集成记录

状态：完成（2026-08-30）。

实际修改：新增稳定 ID `myavalonia.plugin.image.lab.document.image-compare-lab`，Module 按第四位登记 Persistable
Document；领域算法和用例 singleton、Document scoped、View transient。Standalone 通过真实 Module/DI 增加第四个页签，
不复制实现。既有水印质量入口复用新分析器，其他功能可直接消费不含 UI 的 `ImageComparisonSummary`。

证据：四贡献固定顺序、零普通 Document/Tool、两 Scope 隔离、第四个 Headless View 与全部既有三个 Document 回归通过。
没有登记 Workflow Action、Workbench Command 或 AIFLOW。回滚先移除第四个贡献和 Standalone 页签，再移除 Feature/用例。
