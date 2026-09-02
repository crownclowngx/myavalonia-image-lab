# G6 Recipe、Report、快照与导出

状态：完成。

实现 `magnitude-phase-swap-v1` 严格 Recipe、脱敏 JSON/CSV Report 和 schema 1 轻量快照。未知字段、重复属性、非法枚举/组合、固定事实不一致与指纹篡改均拒绝；快照只保存文件名提示与意图，恢复不访问磁盘。PNG 只允许当前已提交结果，拒绝覆盖 A/B，并执行内存回读、原子发布和真实目标回读。
