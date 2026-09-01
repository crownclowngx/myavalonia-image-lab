# Spectral Art Report schema 1

JSON/CSV 报告使用协议 `spectral-art-report-v1`，最大 1 MiB。内容包括源图短指纹与尺寸、补零尺寸、Pattern 来源/尺寸/指纹、Region、强度、写入点数、能量、相位/共轭/可见性、raw Y 与裁切、最大虚部、PSNR/SSIM/MAE/RMSE 和阶段耗时。

报告明确不包含绝对路径、源文件名、Pattern 图片名、原文字、RGBA、Pattern 权重、复数频谱或 Bitmap。CSV 固定 `key,value`，所有字段加双引号并转义；JSON 只序列化独立报告 DTO。强度 0 的无损 PSNR 使用明确字符串 `"Infinity"`，不伪造有限上限。报告是一次实验事实，不是识别率、扫码成功率、隐写安全或发布认证。
