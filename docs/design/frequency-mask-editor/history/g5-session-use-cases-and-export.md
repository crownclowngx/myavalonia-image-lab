# G5：Session、用例与导出

- Prepare Session 一次解码并独占源图、代理、通道、只读频谱和幅度预览。
- Render、Full、Probe、Recipe import/export、Image export 均为窄接口。
- schema 1 使用严格 DTO、1 MiB 前后预算和指纹校验；图片与配方通过原子端口写入。
