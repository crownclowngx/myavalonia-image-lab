# G2 槽位布局与位置

- 实际修改：新增唯一槽位布局、顺序 Strategy、SplitMix64-v1、拒绝采样和稀疏 partial Fisher-Yates。
- 自动证据：混合 Alpha、RGB 精确顺序、SplitMix64 公开向量、0/1/全部请求、复现、不同 seed、无重复与无越界。
- 设计取舍：只为真实的两个替换点使用 Strategy；不使用 `System.Random`、水印随机源或完整槽位排列。
- 遗留：布局保存不透明像素的紧凑 `int[]`，最大图片仍需目标设备资源观察。
- 回滚：移除两种 Strategy，不影响 Robustness 的独立稳定随机性。
