# G4 诊断、指标与频谱联动

状态：完成。

新增供体幅度/相位误差、未定义相位与借用能量、共轭/虚部/Parseval、raw/裁切、NCC、固定梯度相关、PSNR-Y 与全局 SSIM-Y。指标使用 Available/NotApplicable/Undefined，不输出 NaN/Infinity；科学投影的 PSNR/SSIM 固定 N/A。A/B/Result 幅度使用共享对数量程，相位无数据用棋盘纹理；指针探针按同一显示频点即时组合结果，不缓存第三份频谱。
