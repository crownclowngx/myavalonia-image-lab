# G1 规范画布与双输入领域

状态：完成。

新增 `FrequencyPairCanvasProjector` 与不可变 `FrequencyPairCanvas`。实现白底 Alpha 合成、BT.601、居中 FitContain、缩小面积聚合、放大像素中心双线性、内容矩形与 24 位内容指纹；尺寸只接受 256/512/1024。专用测试覆盖透明、留白、面积聚合、指纹稳定与资源前置验证。领域目录无 Avalonia、IO、JSON 或 DI 依赖。
