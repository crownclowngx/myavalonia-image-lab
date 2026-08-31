# 颜色实验报告 Schema

## 版本与格式

- schema：`image-lab.palette-color-transfer-report/1`；
- JSON：UTF-8、缩进、标准有限数；N/A 为 `null` 并伴随状态；
- CSV：UTF-8 BOM、固定列 `recordType,key,value,status`、RFC 风格双引号转义；
- 发布：序列化成功后交给原子写入端口。

## JSON 顶层

| 字段 | 含义 |
| --- | --- |
| schema/product | 稳定 schema 与产品名 |
| colorProtocol/alphaProtocol | 颜色和透明度协议 |
| clusteringProtocol/gamutMappingProtocol | 聚类与色域映射协议 |
| operation | `StatisticsTransfer` 或 `FixedPaletteRemap` |
| recipeFingerprint | 完成结果的配方身份 |
| targetSize/referenceSize | 输入尺寸；无参考时为 null |
| target/reference/result | 可见数、有效权重、Lab 均值/标准差与 Hue 状态 |
| difference | ΔE00 均值/P50/P95/最大值与改变像素数 |
| gamut | 未映射、色度压缩、L* 裁切和最大映射 ΔE76 |
| quality | PSNR-RGB、全局 SSIM-Y、MAE/RMSE；无穷 PSNR 使用 null+status |
| before/afterReferenceCloseness | 相对参考的 Lab 均值/标准差残差与分通道 JSD；不适用时为 null |
| palette | clusterIndex、Hex、占比和 Lab；没有冻结 palette 时为空数组 |

CSV 把协议、操作、指标和每个调色板项分成独立 record。复杂固定数组不写入 V1 CSV；其协议由报告 ID 和文档冻结。

## 非有限数和 N/A

serializer 在输出前检查所有关键 double。NaN/Infinity 直接失败，不写 0。灰阶 Hue 的
`circularMeanHueDegrees=null` 且 `hueStatus=not-applicable`。JSON 不使用命名浮点字面量。

## 隐私

报告模型没有路径或图片字节字段，不输出绝对路径、用户名、机器名、临时目录、异常堆栈或 Bitmap。
fingerprint 只用于本地内容/配方身份，不是安全哈希，也不用于反推图片。

## 兼容规则

字段语义、Alpha、颜色矩阵、聚类 tie-break 或色域映射改变时必须升级 schema/协议 ID。读取方不能用捕获所有异常并
返回空对象的方式“兼容”未知版本。
