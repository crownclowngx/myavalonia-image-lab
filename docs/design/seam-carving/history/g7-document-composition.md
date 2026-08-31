# G7：Document、组合与 Standalone

登记第十六个 Persistable Document `myavalonia.plugin.image.lab.document.seam-carving`。
Document 通过构造注入只依赖窄用例，管理 generation、取消、Bitmap 和轻量快照；无状态算法 singleton，Session/Document scoped。
Standalone 从真实 Module/DI 解析真实 Document/View，没有复制演示业务。
