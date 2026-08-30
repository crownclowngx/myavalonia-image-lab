# Workbench Command 边界

ImageLab V1 不登记 Workbench Command，也不占用全局快捷键。水印写入、检测、提取、取消、保存和导出都属于当前 Document 的局部意图，使用 `AsyncRelayCommand` 调用构造注入的应用用例即可。

这项决定避免把当前图片实例、密码状态或执行进度提升为全局工作台状态。两个同类型 Document 可以独立运行和取消，Host 不需要为局部按钮路由活动 Document。

后续只有同时满足以下条件才考虑新增 Workbench Command：语义跨 View 仍稳定；Host 从活动 Document 路由确实有价值；目标 Document 实现公开 Target 契约；命令返回真实可等待任务并观察取消；ID、菜单与快捷键冲突政策已经冻结。不得把现有局部命令机械包装成全局命令。

Standalone 不模拟 Host Catalog、活动 Document 路由或菜单投影。若未来引入 Workbench Command，必须新增组合根、实例隔离、未知 ID、状态变化与真实 Host 测试。
