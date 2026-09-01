# G5：Session、用例与资源

## 实际所有权

一次载体准备创建独立 SpectralArtSession，独占源 PixelImage、Y 平面、只读 FrequencySpectrum、原频谱预览和指纹。Document 只持有当前 Session 引用并在替换/关闭时 Dispose；无状态 FFT、映射、写入、诊断和 serializer 为 singleton。

一次渲染同时长期可见的是 Session 只读频谱与一个 `CreateWorkingCopy()` 工作数组。映射和预览不是 Complex 数组；工作数组完成全部频域诊断后直接交给 IFFT 原地消费，不存在第三份完整 `Complex[]`。2048² 预算在频谱构建前检查；Pattern、recipe、快照和报告分别有 512²、4 MiB、32 KiB 和 1 MiB 门禁。

## 用例和取消

应用层只有载体准备、Pattern 创建、Render 三个核心窄用例，文件导入导出另设窄边界。FFT 行、映射行、径向扫描、写入、IFFT、crop、质量与差异循环均检查 CancellationToken。Document 用 generation 和串行闸门拒绝迟到结果；候选 Session/Bitmap 在未提交时释放；失败/取消不修改最后有效 Session、Pattern 和 Result。

## 证据

应用测试覆盖单次解码、文字 fake 端口、完整 Render、Alpha 保持、强度 0、预取消、Dispose、2048² 阻断、stale、源文件覆盖与 PNG 回读后原子发布。最终总门禁 666/666；资源结论来自代码所有权与自动测试，不宣称已做外部内存 profiler 峰值测量。
