# G7 Document 生命周期

新增稳定 ID `myavalonia.plugin.image.lab.document.robustness-lab`，Module 将其登记为第五个 scoped Persistable Document。Document 只协调用例、进度、generation、取消、Session、Revision 和导出；算法、BER、编解码和 JSON 不在 Document 中。

schema 1 快照只含路径、Profile、扫描、种子和非敏感步骤 DTO。内联 Payload、密码、像素、结果和密钥不持久化；恢复不自动读取或运行，未知 Kind 在预检时可见阻断。路径/配方变化取消工作并销毁旧 Session，迟到结果由 generation 拒绝。JSON/CSV 经窄对话框和原子写入发布。

响应性修正后，Document 将完整的基线用例和实验用例调度到后台线程。原因是这些用例虽返回 `Task`，但 DCT、像素和编解码阶段可能在第一个真正异步等待之前同步占用调用线程；只写 `await` 不能保证 Avalonia Dispatcher 得到控制权。Document 仍只负责调度、取消和提交，不承载算法循环；`Progress<T>` 在 UI 上下文创建并负责回送进度，既有 generation 门禁继续拒绝迟到结果。
