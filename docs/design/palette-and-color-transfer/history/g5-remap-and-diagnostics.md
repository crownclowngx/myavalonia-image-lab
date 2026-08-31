# G5：重映射与误差

完成对 2–12 色冻结调色板的逐像素精确 ΔE76 最近色、稳定 cluster tie-break、A=0 四字节保持、每项计数/
权重、固定 100-bin ΔE00、真实均值/最大值，并复用既有 FullReferenceQualityAnalyzer 的 PSNR/SSIM。
像素探针分别使用目标/参考坐标，不构造隐藏像素配对。
