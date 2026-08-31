# 调色板与颜色迁移精确指南

## 产品与协议

- Document ID：`myavalonia.plugin.image.lab.document.palette-color-transfer`；
- 颜色：非预乘 RGBA8888、IEC sRGB、XYZ D65、CIELAB；
- 颜色协议：`srgb-d65-cielab-v1`；
- Alpha：`straight-alpha-weight-a-over-255-v1`；
- 聚类：`rgb5-weighted-lab-kmeans-v1`；
- 色域映射：`lab-preserve-l-hue-chroma-bisection-v1`；
- 报告：`image-lab.palette-color-transfer-report/1`。

构造边界拒绝非有限值和非法范围。最终 sRGB 字节使用 ToEven 舍入。Domain 不引用 Avalonia、JSON、文件系统或 DI。

## 分层与 SOLID

`Domain/Imaging` 只放稳定颜色数学；`Domain/ColorTransfer` 分开统计、聚合、聚类、迁移、重映射、色差与探针；
`Application/ColorTransfer` 提供准备、分析、冻结、迁移、重映射和导出窄用例；`Infrastructure/ColorTransfer`
只做严格报告序列化；Feature 只管理命令、generation、取消、Bitmap 和文案。

无状态服务为 singleton。`ColorTransferSession` 为 scoped，独占目标、参考、分析、冻结调色板和当前结果。
这里使用不可变值、Session、既有 Adapter 和构造注入；没有 Mediator、事件总线、算法注册中心或抽象工厂。

## 参数与失效规则

| 参数/操作 | 范围 | 对已有结果的影响 |
| --- | --- | --- |
| K | 2–12，默认 6 | 清除分析、冻结调色板与结果 |
| 调色板来源 | 目标/参考 | 只选择待冻结来源 |
| 显示排序 | 占比/L*/Hue | 纯显示，不改变 cluster identity |
| 迁移模式 | 完整 Lab/保留 L* | 结果过期 |
| 强度 | 0–1 | 结果过期 |
| 换目标 | 显式载入 | 清除目标分析、冻结调色板与结果 |
| 换参考 | 显式载入 | 清除参考分析；参考 palette 与结果失效 |

`ResultRevision == Revision` 才允许导出。每个载入与运算通道都使用取消源和 generation；取消、迟到成功、
迟到异常与关闭后的返回都不得覆盖新状态。

## 资源和所有权

- 解码输入受既有 64 MiB 编码与 16,000,000 像素上限保护；
- 目标、参考和一个当前结果为长期完整图；预览最大边 512；
- 统计使用固定数组：RGB 768、HSV 380、Lab 612、H-S 18,000、a*-b* 16,384；
- 聚合固定 32,768 cell；聚类最多处理非空 cell，不保存逐像素 Lab 数组；
- 像素循环行优先，每行检查取消；没有无界并行；
- Bitmap 和取消源归 Document；PixelImage 与分析事实归 scoped Session；换图/关闭时清空所有引用。

## UI 与可访问性

三张图明确标注目标/参考/结果。调色板同时显示序号、Hex、Lab、占比和误差；直方图、密度和 ΔE 图都有
文字定义与数值表。所有主要操作均为标准可聚焦按钮/输入控件，高对比下不只靠颜色区分身份。

## 导出

PNG 只接受当前完整尺寸结果：编码后从内存真实解码，验证尺寸与每个 Alpha，再经原子写入端口发布。
JSON/CSV 先拒绝非有限值，再通过原子端口写入。报告 DTO 不含路径字段，serializer 没有机会泄漏绝对路径。

## 扩展边界

只有至少两个真实产品消费者证明稳定后，才把颜色原语继续提升为共享框架。新增颜色操作应先写一个普通 sealed
领域服务和一个窄用例；只有多个实现确实共享输入不变、Alpha 保持、取消无半结果和诊断契约时才引入 Strategy。
ICC、LUT、局部迁移、抖动与自动参数不应塞入当前 Document 的 switch。
