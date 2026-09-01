# G6：协议、报告与导出

完成 `hybrid-image-v1` strict recipe、`hybrid-image-report-v1` JSON/CSV、32 KiB 脱敏快照和有界导入用例。读取拒绝重复/未知字段、未知固定事实、越界数值与指纹篡改。

PNG 仅接受当前完整尺寸结果，禁止覆盖 A/B；编码后内存回读、原子发布后真实目标回读，并核对尺寸、RGBA、不透明灰度。Recipe/report 不保存绝对路径或像素；快照恢复不执行 IO、对齐或滤波。
