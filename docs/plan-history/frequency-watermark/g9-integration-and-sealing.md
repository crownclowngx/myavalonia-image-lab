# G9 集成与封板记录

状态：本地开发集成完成；发布封板延期（2026-08-30）

## 已完成

- Standalone 通过真实 Module 与服务扩展解析两个 Document，各自拥有 Scope 和 Lifetime。
- Debug locked restore、`-warnaserror` 构建与 44 个测试通过。
- README、领域边界、协议、用户说明、测试说明和 G0–G9 记录已建立。
- 代码内无 AIFLOW；未新增 Windows CI、发布 Workflow 或发布脚本调用。

## 按用户要求未执行

- Release 发布门禁；
- `BuildManagedPluginPackage` 与 ZIP 确定性/内容审计；
- 部署到真实 Host、Dock、多实例与保存恢复人工验收；
- Windows CI 与正式发布。

因此当前不能标记“V1 已发布/已封板”，也没有建立对外 Protocol/Profile 兼容承诺。发布时从本记录继续执行，不应重写 G0–G8 已有事实。

回滚：隐藏两个贡献或卸载整个未发布插件；不删除用户原图、输出图或已导出的 Payload。
