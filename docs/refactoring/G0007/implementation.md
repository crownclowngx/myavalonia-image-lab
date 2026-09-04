# 实现与 SOLID 边界

## 生产结构

```text
Workflow Handler（JSON/SDK Adapter）
  → ApplyArtEffectsFileUseCase
    → WorkflowArtifactReader（只读验证）
    → AvaloniaImageCodec（PNG 解码/编码边界）
    → ImageArtEffectPipeline
       → GaussianBlurArtEffectProcessor
       → BloomArtEffectProcessor
       → GrainArtEffectProcessor
    → ExclusivePngCommitter（唯一 partial + 原子提交）
```

Shared Domain 不引用 Avalonia、Workflow SDK、JSON、文件系统或 DI；处理器实现单效果 Strategy，Pipeline 只固定
顺序，不扩展为通用 DAG。输入 `PixelImage` 不可变，三个效果创建新结果并保持 Alpha。

Action 风险为 `ReadsLocalFiles | WritesLocalFiles | LongRunning`，确认策略为 `OncePerRun`。模块仍登记原有
21 个 Persistable Document，新增 Action 不改变 Document 数量。

## 文件事务

Reader 在解码前验证契约、生产者、OperationId、规范化绝对路径、约定根目录、`.owner.json`、重解析点、
长度、SHA-256、PNG 签名和 256 MiB 上限。解码后验证单边不超过 4096、总像素不超过 16,777,216。

输出必须是父目录已存在的绝对 `.png`，不得位于 Workflow Artifact 根、不得与输入相同、不得覆盖现有文件。
编码数据先写唯一 `.partial`，再以不覆盖方式原子移动；异常和取消清理 partial。
