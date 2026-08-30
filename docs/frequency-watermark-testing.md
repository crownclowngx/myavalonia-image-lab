# 频域水印测试与质量门禁

## 本地回归命令

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

当前开发基线为 44 个自动测试。测试分成五层：纯数值与 RS、Frame 与安全、频域载体、正式 PNG/JPEG 字节回读、Document/组合根/原子持久化。

## 已冻结的开发门禁

- DCT/IDCT 浮点往返误差不超过 `1e-9`；QIM 0/1 均可读回。
- RS(255,223) 覆盖完整块和缩短块，单块 16 个符号错误可修复，17 个错误必须失败。
- 三种 Profile 均通过 RGBA 量化后的内存闭环。
- PNG 实际输出字节由正式提取器完整恢复。
- `Robust + JPEG 100` 实际输出自检通过；Robust 输出再经 JPEG 95 重编码仍可恢复测试 Payload。
- Robust 可恢复确定性的 RGB 每通道 ±1 轻噪声。
- Alpha、透明块和非 8 倍数边缘逐字节保持；容量不足时源对象零修改。
- 随机普通纹理不误判；未知 Flag、超长输入、错误密码和超 ECC 损伤安全失败。
- 两个 Document 的密码、内联 Payload 和恢复内容不进入快照；迟到操作不覆盖新结果；关闭取消正在执行的工作。

上述 JPEG 与噪声结论是开发回归边界，不是对任意照片、编码器或平台的市场承诺。正式发布前仍需在授权照片/插画/透明图语料上记录逐样本原始结果、编码器版本、设备、耗时和峰值内存。当前用户明确要求不执行发布门禁，因此这里不伪造真实 Host、ZIP、Windows CI 或长时间泄漏结论。

## 失败定位

- Domain/RS 失败：数值、索引或纠错实现回归。
- ProtocolSecurity 失败：线格式、安全边界或密码学处理回归，禁止通过更新断言绕过。
- WatermarkPipeline 失败：载体位置、QIM 强度、Alpha/边缘或容量回归。
- ImageCodecAndUseCase 失败：正式编解码字节、自检或重编码鲁棒性回归。
- DocumentLifecycle/Composition 失败：Scope、取消、敏感快照、DI 或文件发布回归。
