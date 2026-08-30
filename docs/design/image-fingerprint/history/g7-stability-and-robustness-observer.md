# G7 稳定性与 Robustness 观测

状态：完成。指纹稳定性以窄信道复用正式 JPEG 编解码及既有缩放、亮度、裁剪算子，限制为四种单轴、最多 21 点、串行执行，只保留当前预览。JPEG Alpha 明确阻断。

Robustness Lab 增加默认关闭的 aHash/dHash/pHash 勾选项和窄 `IFingerprintObservationProbe`；案例报告追加可选观测和 CSV 距离列。观测不参与水印成功、BER、质量或失败分类，同图三算法距离 0 有自动门禁。
