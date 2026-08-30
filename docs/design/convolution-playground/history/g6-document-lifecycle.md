# G6 Document 生命周期

- 日期：2026-08-30；状态：完成。
- 稳定 ID 登记为第九个 Persistable Document；服务为 singleton 无状态算法、Document 为 scoped 实例。
- generation + CancellationToken + Session 引用 + fingerprint 四重检查阻止迟到结果；数学参数变化立即清空完整结果。
- schema 1 只保存稳定 ID、有限系数和轻量参数；恢复不读文件、不运行。
- Bitmap 替换释放旧对象；Dispose 先推进代次/取消，再释放 Session 和四张 Bitmap。
- 测试覆盖贡献顺序、两个 Scope 隔离、轻量快照、未知 schema 和恢复不解码。
- 后续可读性整理把 `ImageLabPluginServices` 按十个领域/基础边界拆成私有登记函数；公共入口、服务类型和生命周期保持不变，303 项全量回归通过。
