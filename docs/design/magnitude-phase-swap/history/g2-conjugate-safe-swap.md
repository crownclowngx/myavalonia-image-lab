# G2 交换与共轭安全

状态：完成。

新增 `SpectrumComponentMixer` 与 `MagnitudePhaseReconstructor`。混合器按未中心化行主序代表处理共轭对，另一点精确写共轭，自共轭点固定实数；结果经独立共轭扫描后才进入公共 IFFT。测试覆盖两种交换、供体误差、自共轭符号、IFFT 归一化和非法虚部拒绝。公共 FFT 没有新增产品枚举或路由。
