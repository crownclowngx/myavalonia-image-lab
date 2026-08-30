# LSB 隐写与统计实验

本目录是 ImageLab 第八个 Persistable Document 的专用入口。该能力把独立 `ILSB` V1 Frame 写入用户显式选择图片的 RGB 低位，并联动展示容量、位置、实际变化、bit 代理、位分布、PoV 卡方、邻接统计和受控扰动结果。

> 教学与实验用途；不保证不可检测；不是频域鲁棒水印。seed 不是密码，CRC 不是认证，p 值不是“图片含隐写的概率”。

## 阅读顺序

1. [新手说明](user-manual.md)：第一次使用时从这里开始。
2. [准确指南](guide.md)：参数、状态、导出、资源与限制。
3. [协议](protocol.md)：`ILSB` V1、位序和位置版本。
4. [数学原理](mathematical-principles.md)：replacement、容量、熵、卡方、邻接、BER、PSNR。
5. [报告 schema](report-schema.md)：JSON/CSV 与隐私边界。
6. [测试门禁](testing.md)和[实施历史](history/README.md)：自动证据与延期事项。
7. [实施计划](implementation.md)：G0–G9 的完整产品和工程约束。

## 当前状态

生产领域、应用用例、第八个 Document、真实 View、Standalone 接入、专用测试和 G9 Debug/Release locked 本地门禁已经完成。没有使用 AIFLOW，没有新增 Workflow Action、Workbench Command、Windows CI 或发布脚本；真实 Host、ZIP 和发布验收仍延期。
