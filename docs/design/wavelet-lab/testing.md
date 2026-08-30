# Wavelet Lab 测试与本地门禁

## 2026-08-31 封板证据

起始基线为 304/304。实现后总计 333/333 自动测试通过，0 失败、0 跳过；Debug/Release 均 0 警告、0 错误。

```powershell
dotnet restore ImageLabPlugin.slnx --locked-mode
dotnet build ImageLabPlugin.slnx -c Debug --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Debug --no-build --no-restore
dotnet build ImageLabPlugin.slnx -c Release --no-restore -warnaserror
dotnet test tests/ImageLabPlugin.Tests/ImageLabPlugin.Tests.csproj -c Release --no-build --no-restore
```

最近一次证据：Debug build 0/0，Debug test 333/333（约 6 秒）；Release build 0/0，Release test 333/333
（约 2 秒）。时间只用于观察本机回归，不构成性能 SLA。

## 新增门禁覆盖

- 2×2 Haar 手算 Golden：packed `[5,-1;-2,0]`；四象限坐标冻结。
- Haar/CDF 5/3 对 4×4、5×3、17×9、1×N、N×1 和 6 层小图的 double 正逆重建。
- 从最深层逐级逆变换到指定层，阶段尺寸与第 1 层完整裁剪结果。
- 水平/垂直条纹分别进入 LH/HL；错误策略拒绝逆变换另一策略金字塔。
- Haar 未扩展平面的 Parseval；CDF 5/3 不套用该断言。
- Hard/Soft 的 T=0、阈值等号、正负值、目标子带隔离和 LL 不变。
- MAD、Universal、小样本不可用、扫描 21 点/60 案例上限、固定顺序和取消后的部分结果。
- 参考代理按同一缩放规则生成并在扫描中返回 PSNR/SSIM；无参考分支保持不可排序。
- 代理/完整尺寸指纹、释放后会话、stale/代理导出阻断、PNG 固定格式、JSON/CSV。
- Wavelet 图片回写固定 `AwayFromZero` 中点舍入并逐字节保留 Alpha。
- DWT 容量、max+1、固定种子回读、错误种子、源图不变、尺寸与 Alpha 保留。
- DCT/DWT Adapter 在各自协议下无扰动回读共同 Payload。
- 两载体案例 ID/次序完全一致；共同容量任一不足时在任何扰动前阻断；报告保存纠错前 raw BER。
- 第十个贡献、两个 Scope 隔离、轻量快照、未知 schema、Standalone 与 Headless View/专用控件。
- 源码级依赖方向门禁：Domain 不反向依赖 Application/Infrastructure/Avalonia/JSON，Application 不依赖 Infrastructure/Avalonia，Feature 不含 DWT 核心循环。
- 生产源码禁止 AIFLOW、Workflow Action、Workbench Command 和通用 DAG 入口。

## 没有证明的结论

- 没有证明主观观感、语义细节恢复或无参考去噪质量。
- 没有证明 DCT 或 DWT 在所有图片和攻击下普遍更好。
- 没有完成跨平台/Windows CI、真实 Host、ZIP、安装升级、GPU 或大图性能 SLA。
- 代理结果不能作为完整尺寸数值或导出证据。
