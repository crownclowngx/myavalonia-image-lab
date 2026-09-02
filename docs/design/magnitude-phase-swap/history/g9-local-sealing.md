# G9 本地自动门禁

状态：完成（非发布封板）。日期：2026-09-02，环境：Windows，.NET 10。

- locked restore：通过；
- Debug `build -warnaserror`：0 warning / 0 error；
- Debug tests：739/739 通过，0 fail，0 skip，控制台约 6 s；
- Release `build -warnaserror`：0 warning / 0 error；
- Release tests：739/739 通过，0 fail，0 skip，控制台约 3 s；
- `git diff --check`：通过；
- 未新增 NuGet、AIFLOW、Windows CI 或发布脚本；
- Windows CI、真实 Host、ZIP、签名、安装与发布门禁按用户范围未执行。

上述结论只代表本地开发自动门禁，不代表发布完成。
