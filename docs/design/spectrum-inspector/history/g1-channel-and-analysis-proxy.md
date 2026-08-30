# G1 通道与分析代理记录

状态：完成（2026-08-30）。

新增 `ImageChannelPlane`、`ImageChannelConverter` 和 `ImageAnalysisProxyProjector`。RGB 只替换选中字节；
Y/Cb/Cr 保留另两分量并统计裁切；Alpha 不参与运算。代理使用面积覆盖权重，小图逐字节克隆。
文件对话框拆为 `IImageFileDialog` 与 `IPayloadFileDialog`，现有适配器同时实现二者。

证据：六通道、透明 RGB、裁切、Alpha、源对象不变、代理尺寸与面积平均测试通过。回滚需整体恢复文件端口，
不能留下两套对话框事实源。
