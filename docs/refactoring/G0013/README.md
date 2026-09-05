# G0013：ImageLab Workflow 编排

ImageLab 保持纯 Provider，新增 `apply-art-effects-file-to-directory`，供 Studio ForEach 使用。
仍只有二十一个 Persistable Document；不增加 Consumer、Tool 或实时跨插件效果。

- [接口、提交与 SOLID](implementation.md)
- [测试及验收](testing.md)
- [跨插件完整设计](../../../../myavalonia-fractal-art/docs/refactoring/G0013/workflow-orchestration-design.md)

本阶段不使用 AIFLOW，不增加 Windows CI，不执行 Release、ZIP、安装、部署、签名和发布门禁。
