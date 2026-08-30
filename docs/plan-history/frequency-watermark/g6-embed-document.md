# G6 水印写入 Document 记录

状态：完成（2026-08-30）

界面形成选择源图、文本/JSON/文件 Payload、Profile、容量估算、密码保护、PNG/JPEG、写入自检、原图/输出/差异/频谱四窗预览、取消和原子保存闭环。

Document 只依赖 `IEstimateWatermarkCapacityUseCase` 与 `IEmbedWatermarkUseCase`，不直接依赖协议实现，符合依赖倒置。每次操作会取消上一操作；慢实现即使忽略取消，结果提交前仍再次检查 Token，旧结果不能覆盖新状态。关闭 Document 取消操作并释放 Bitmap、输出字节和密码。

快照 schema 1 保存源路径、外部 Payload 路径、Profile 与输出配方，不保存内联 Payload、密码或生成结果。密码变化会使旧输出立即失效。

门禁证据：Headless View、脏修订、敏感快照、恢复清空、快速双击迟到结果和关闭取消测试通过。

偏差：V1 通过 Host 文件选择器而非自建覆盖对话框；SDK 没有窄 Document-to-Document 交接端口，因此不持有另一个 Document。拖放未作为必要入口，键盘可通过标准控件 Tab/Space/Enter 完成闭环。
