# G9 本地集成与开发封板

本地门禁使用 locked restore、Debug/Release warn-as-error build 和 Debug/Release test。最终实际测试数与命令结果记录在 `docs/robustness-lab-testing.md`；既有水印、频域、比较和四个旧 Document 全量回归未放宽。

Module 固定贡献五个 Persistable Document、零普通 Document、零 Tool；Standalone 复用同一 Module/DI 并增加第五页。没有新增 NuGet、AIFLOW、Workflow Action、Workbench Command、Windows CI 或发布脚本。

未执行：Standalone 完整人工场景、真实 Host、ZIP、安装/卸载、授权语料、目标设备性能、Windows CI 和正式发布。本记录只封板开发状态，不宣称发布完成。
