# G6：应用会话与导出

- `WaveletSession` 独占一次解码的完整源图、可选同尺寸参考图和分析代理。
- 分解、去噪、扫描、PNG、报告各有窄用例；Application 不创建 Bitmap、不实现数学循环。
- PNG 固定无损编码，只有当前指纹的完整尺寸结果可导出；代理和 stale 结果被阻断。
- JSON/CSV 序列化在 Infrastructure，输出经既有原子写入端口发布。
