# G1：Pattern 与输入规范化

新增不可变 Pattern、来源/采样/适配/背景参数及指纹。图片沿用 PixelImage、IImageCodec 和预算；灰度经 Alpha 合成后使用目标尺寸面积缩放，二值使用专用最近邻。文字通过 Avalonia 适配器返回 RGBA，不把字体对象暴露给 Domain，未增加 NuGet。
