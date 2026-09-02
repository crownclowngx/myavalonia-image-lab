# G5 Session、资源与应用用例

状态：功能完成；进程级 1024 峰值采样延期。

`MagnitudePhaseSession` 由单个 Document Scope 独占两画布、两只读频谱、源预览和一个当前结果。准备用例各解码/FFT 一次；渲染候选携带 Session/Recipe 指纹与 generation，迟到候选被拒绝且最后有效结果保留。一次实验只创建一个工作 `Complex[]`，在频谱投影和能量记录后由 IFFT 原地消费。checked 估算给出 1024 保守工作集约 100 MiB、256 MiB 前置上限；实际进程峰值未采样，保留为发布前资源复核。
