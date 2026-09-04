# G0007：ImageLab 共享核心与文件式 Workflow Action

G0007 为 ImageLab 增加一个 Provider Action：

```text
myavalonia.plugin.image.lab.workflow.apply-art-effects-file
```

大型 RGBA/PNG 数据只经文件系统传输，Workflow JSON 只携带 File Artifact v1、效果参数、输出路径和尺寸摘要。
ImageLab 保持纯 Provider，不引用 Fractal Art，也不删除输入生产者拥有的文件。

- [实现与 SOLID 边界](implementation.md)
- [数学与文件契约](contract-and-effects.md)
- [测试记录](testing.md)

本阶段不增加 AIFLOW、Windows CI、发布、签名、ZIP 或 NuGet 门禁。
