# G8：Document、持久化和组合根

- 登记 `myavalonia.plugin.image.lab.document.wavelet-lab`，Module 贡献数从九增至十，不新增 Tool。
- `WaveletLabDocument` 为 scoped；每实例独立 Session、取消、generation、结果、Bitmap 和 revision。
- 快照 schema 1 只保存路径与轻量参数，不含 RGBA、系数、Bitmap、Payload、密码或报告。
- 参数变化推进 generation 并使完整结果/报告 stale；迟到提交核对 generation、Session 和指纹。
