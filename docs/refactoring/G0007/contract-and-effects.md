# File Artifact v1 与效果语义

## 输入描述对象

```json
{
  "contract": "myavalonia.workflow.file-artifact",
  "version": 1,
  "producerPluginId": "myavalonia.plugin.fractal.art",
  "producerOperationId": "00000000-0000-0000-0000-000000000000",
  "lifetime": "transient",
  "path": "C:\\...\\source.png",
  "mediaType": "image/png",
  "byteLength": 123456,
  "sha256": "64位大写十六进制摘要"
}
```

ImageLab 接受 `transient` 或 `run` 输入，输出 `producerPluginId=myavalonia.plugin.image.lab`、
`lifetime=persistent`。两个插件各自维护私有 DTO，没有 SDK Artifact 类型或共享二进制库。

## 固定效果顺序

`PNG Decode → Gaussian Blur → Bloom → Grain → PNG Encode`

- Blur：确定性可分离高斯；sigma 为 `0..10`。
- Bloom：Rec.709 亮度 `0.2126R + 0.7152G + 0.0722B` 选择高光，高斯扩散后按 strength 加法合成；
  threshold 为 `0..1`、sigma 为 `0.1..10`、strength 为 `0..4`。
- Grain：SplitMix64 产生均匀序列，Box–Muller 产生高斯噪声；amount 为 `0..100`，seed 为 Int64。
- 全流程使用 RGBA8888、row-major、straight alpha，Alpha 原样保留。

相同输入、参数和 seed 必须得到逐字节一致的像素及 PNG。
