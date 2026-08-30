# G0 产品与指标基线

- 冻结主路径为原始载体 + Payload → 多 Profile 受控基线 → 有序扰动链 → 单轴扫描 → 分步诊断。
- 成功固定为正式提取成功、Payload 逐字节相等且完整性有效；N/A、取消、未测量和失败不混用。
- BER 分为投票前 `PhysicalRawBer` 与 RS 前 `VotedPreEccBer`，Header/Data 分层；质量分 Attack-only 与 End-to-end。
- 上限为 12 步、101 点、20 trial、3 Profile、300 案例、1,200 观察、16M 像素；敏感值不进快照/报告。
- 实施前 Debug 基线为 97/97、零跳过、零警告。未修改发布配置，不使用 AIFLOW。

回滚只需先移除第五个 Document 贡献；既有四个 Document 和水印协议不依赖本产品。
