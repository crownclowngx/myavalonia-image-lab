# G5：Session、用例与导出

- `SvdSession` 独占源图、代理和有限字典；缓存键含尺寸+RGBA 指纹、策略、通道和协议，不含 k。
- 完成 Prepare、Decompose、Rank、Component、Compare、PNG 和 Report 七类窄用例。
- PNG 强制扩展名、当前指纹和非源路径；JSON 严格表达精确 PSNR，CSV 使用固定记录与 invariant culture。
