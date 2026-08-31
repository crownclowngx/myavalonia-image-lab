# G0：产品、协议与基线

- 2026-08-31 冻结非预乘 RGBA8888、sRGB D65、CIELAB、HSV、Alpha=A/255、Hue N/A；
- 冻结 K=2–12、32³ 聚合、确定性 tie-break、统计迁移零方差和 chroma 二分；
- 明确 SOLID 第一、朴素模式、不用 AIFLOW、不加 Windows CI/发布门禁；
- 实跑 locked restore、Debug warn-as-error build 与 479/479 测试，0 跳过。
