# Wavelet Lab 开发与高级使用指南

## 身份与对象生命周期

- Document ID：`myavalonia.plugin.image.lab.document.wavelet-lab`。
- 每个打开实例拥有独立 `WaveletSession`、图片、配方、金字塔、Bitmap、扫描/benchmark 结果和取消源。
- 无状态数值服务为 singleton；Document 为 scoped；View 为 transient。
- 快照 schema 为 1，只保存路径、策略、通道、层数、阈值、子带、投影和代理档位。

## SOLID 分层

```text
Features/WaveletLab
        ↓
Application/Wavelets ─→ Application/Ports
        ↓                      ↑
Domain/Wavelets       Infrastructure/Wavelets + Persistence
```

`IWaveletTransform` 只有 Haar/CDF 5/3 两个 Strategy。`WaveletTransformCatalog` 做固定 ID 映射；
DCT/DWT benchmark 通过两个 `IWatermarkBenchmarkCarrier` Adapter 统一。阈值、噪声估计、投影、重建、Session、
序列化和 Document 分属不同类型，禁止合并成万能 `WaveletService`。

## 状态和失效规则

- 路径或代理档位变化：释放 Session，并使全部结果失效。
- 小波、通道、层数或阈值变化：推进 generation，取消旧任务，清除分析、完整结果、扫描和报告。
- 层/子带/投影模式只影响下一次有界投影，不改变真实系数。
- 异步提交同时核对 generation、Session 引用和配方指纹；旧任务不能覆盖新状态。
- 导出 PNG 必须满足 `IsFullSize` 且结果指纹等于当前配方；报告保存当前已完成案例。
- 扫描曲线只显示保留系数比例趋势；每个扫描和 benchmark 案例都同时保留文本行，取消后只显示完成项。

## 资源边界

- 编码输入 64 MiB、图片 16,000,000 像素，DWT 扩展后仍不得超过 16,000,000 样本。
- 代理档位固定 512/1024/2048；层数 1–6。
- 阈值点最多 21、总案例最多 60；串行执行，每案例检查取消。
- 一个金字塔拥有一个连续 `double[]`；阈值返回新副本；View 只持有 Bitmap 和统计。

## 增加第三种小波时

只有产品范围确认出现真实变化点后，新增 `IWaveletTransform` 实现和组合根固定登记；不得修改阈值、投影、Session、
Document 或报告核心流程，也不得引入反射发现。新策略必须遵守相同尺寸、有限值、取消、正逆和所有权契约。

## 错误边界

用户可见错误包括路径不存在、参考尺寸不符、层数/阈值非法、扩展预算超限、容量不足、结果过期、完整性失败、
导出失败和取消。错误不能吞掉后继续导出旧结果；未知快照回退安全默认值而不阻断 Host 恢复。
