# G7：画布与 View

- Canvas 负责 Uniform/letterbox 映射、Pointer capture、轻量 gesture 路径和双探针绘制。
- 手势状态将“冻结快照—清理预览—释放 capture”封装为固定顺序，避免 capture-lost 同步回调丢失绘制路径。
- Domain 只接收 `[0,1]²` 坐标；共轭显示坐标由 Application 探针返回。
- View 使用编译绑定并在真实 Module/Standalone 对象图中构造；Headless 门禁通过。
