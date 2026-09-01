# G3：径向背景与幅度

原 Periodic radial baseline 无语义迁入 Domain/Frequency，PeriodicPeakDetector 继续消费并保持回归。SpectralAmplitudeWriter 在调用方唯一工作副本原地按共同幅度、对数功率、稳健尺度和固定相位规则写入精确共轭对；源频谱不变，强度 0 不写任何点。
