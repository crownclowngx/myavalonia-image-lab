# G7 Document 生命周期

新增稳定 ID `myavalonia.plugin.image.lab.document.robustness-lab`，Module 将其登记为第五个 scoped Persistable Document。Document 只协调用例、进度、generation、取消、Session、Revision 和导出；算法、BER、编解码和 JSON 不在 Document 中。

schema 1 快照只含路径、Profile、扫描、种子和非敏感步骤 DTO。内联 Payload、密码、像素、结果和密钥不持久化；恢复不自动读取或运行，未知 Kind 在预检时可见阻断。路径/配方变化取消工作并销毁旧 Session，迟到结果由 generation 拒绝。JSON/CSV 经窄对话框和原子写入发布。
