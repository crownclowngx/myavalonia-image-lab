# G0：产品、数值与基线

- 冻结稳定 ID、非预乘 RGBA、白底 BT.601、Sobel、tie-break、`±1000`、预乘插值和三种轴顺序。
- 采用保守预算：200 万像素、256 缝、单轴 25%、1.6 亿访问、800 万坐标、512 笔划、128 KiB 快照。
- 实跑 locked restore、Debug warn-as-error build 和测试：0 警告/错误，520/520、0 跳过。
- 明确排除 AIFLOW、Windows CI、真实 Host、ZIP、安装和发布门禁。
