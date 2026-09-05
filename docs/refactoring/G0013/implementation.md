# G0013 ImageLab 实施与协议

## 新增接口

稳定 Action ID：`myavalonia.plugin.image.lab.workflow.apply-art-effects-file-to-directory`。
source、blur、bloom、grain 沿用旧输入；outputDirectory 为已有绝对目录，fileStem 为 1–64 个小写字母、数字或连字符。
应用层追加 .png，返回原有 artifact + image，lifetime 固定 persistent。
风险 ReadsLocalFiles | WritesLocalFiles | LongRunning，确认 OncePerRun。

旧 `apply-art-effects-file` ID、输入输出 Schema 和风险保持兼容，与 G0007 固定 JSON 夹具对照。
新目录 Handler 是薄适配，复用旧 Handler 的处理/提交顺序，不通过 Gateway 调用其他 Action。

## 单一职责和依赖倒置

Prepare 用例依赖输入读取、编解码和纯效果流水线，返回尚未提交的结果与 PNG；
Handler 负责严格解析、进度与响应序列化；Committer 只负责排他提交。
Workspace 文件校验集中到 Infrastructure，Shared Domain 不引入 UI、Workflow、JSON、DI 或文件系统。

Standalone 的 PreviewPluginRegistration 补齐 IWorkflowActionRegistration，只注册 Scoped Handler。
该适配器仍是纯 Provider，不取得 Consumer Gateway；修复新增 Workflow 注册后预览启动失败的问题。

## 文件与取消边界

实际读取上限为 PNG 256 MiB、marker 4096 字节；拒绝重复或未知字段，兼容 Fractal 新 marker 的 invocationId/itemId。
按生产者与操作 GUID 重建 source.png 路径，检查所有现存祖先的重解析点、长度及大写 SHA-256。
PNG 签名和 IHDR 必须存在，在像素解码前拒绝超过 4096 的单边尺寸，解码后复核。
输出拒绝临时根、重解析点、原输入与既有目标；父目录必须已存在。

校验、编码、结果序列化和 committing 通知均在最终提交前完成。临时文件以 CreateNew 写入，
最后检查取消并再次检查目标，然后无覆盖原子改名，直接返回预构造结果。
提交成功后的 finally 不再触碰临时路径，防止外部重建同名文件后产生错误删除或错误失败。
未提交 partial 会清理；清理失败不能被解释为成功。

Host 的最终终态与本地文件提交不是同一个事务；Host 拒绝结果时可能已存在 persistent 输出。
Studio 将该项标为需核对并用新名称重试，ImageLab 不自动删除用户目录中的已提交 PNG。
