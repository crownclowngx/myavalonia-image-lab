# G5 水印诊断

从 `FrequencyWatermarkCarrier` 抽出只读 `PhysicalChannelRead`，正式 `ReadHeader/ReadData` 改为复用同一读取路径；没有复制 DCT-QIM 或 Frame 协议。诊断同时比较每副本判决和投票字节，Header/Data RS 修复分别读取，最终成功仍由正式提取用例裁决。

实现过程中曾发现 Header 槽位原本按“副本优先”布局，而通用诊断期望“bit 优先”；已在适配边界显式重排并用既有 97 项回归锁定。人工 bit Golden Vector 和真实未扰动 Carrier 证明 Physical/Voted BER、RS 均为 0 或精确错误数。受控 Frame/Mapping Key 由可释放基线拥有并清零。
