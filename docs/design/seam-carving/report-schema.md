# 内容感知缩放报告 Schema

## JSON

schema 固定为 `image-lab-seam-carving-report-v1`，camelCase 输出包含：协议 ID、UTC 时间、输入 fingerprint、
输入/目标尺寸、轴顺序、参考算法、最终状态、蒙版计数、资源估算、最多 256 条步骤、`seamVsReference` 和限制说明。

每个步骤含序号、方向、操作、前后尺寸、基础/有效累计能量、保护命中和优先删除命中。PSNR 正无穷不写非法
`Infinity`，而是 `{ "isExact": true, "valueDb": null }`。所有领域计算在序列化前拒绝 NaN/Infinity。

报告刻意不含：源绝对路径、RGBA、笔划坐标、蒙版栅格、能量/累计矩阵、路径坐标、逐帧图片或 Avalonia 状态。

## CSV

CSV 使用 UTF-8 BOM、CRLF、InvariantCulture 小数点和 RFC 4180 转义。固定列为：

```text
stepNumber,orientation,operation,beforeWidth,beforeHeight,afterWidth,afterHeight,
baseEnergy,effectiveEnergy,protectHits,preferRemovalHits
```

CSV 是有界步骤表，不复制 JSON 的嵌套资源和指标对象。方向/操作使用稳定英文枚举值，便于机器读取。

## 兼容策略

V1 schema 不在原字段上改变单位或语义。能量、tie-break、Alpha、插入或预算协议变化必须升级对应协议 ID；
报告 schema 只允许向后增加可选字段。消费者必须拒绝未知 schema，而不能猜测数值含义。
