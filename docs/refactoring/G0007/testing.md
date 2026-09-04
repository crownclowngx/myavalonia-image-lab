# G0007 测试记录

新增自动测试覆盖：

- Pipeline 输入不可变、固定顺序、Alpha 保持和同 seed 确定性；
- Blur 恒等、Bloom 边界、Grain 同/异 seed；
- 真实 Artifact 的 marker、摘要、PNG 读取与输出提交；
- 输入文件不被 ImageLab 删除；
- Action 稳定 ID、风险、确认策略和注册数量；
- Shared Domain 继续禁止 UI、JSON、DI、Workflow 和文件系统依赖。

当前 Debug/Release 全量结果均为：`772 passed, 0 failed, 0 skipped`；
G0007 改动文件的 `dotnet format --verify-no-changes --no-restore --include ...` 通过；未为满足门禁改写
仓库内不属于本阶段的历史格式差异。真实三插件 Host 人工集成不属于发布门禁，结果另行记录。
