# G6：Session、用例与报告

新增 scoped `ColorTransferSession`，以及准备、分析、冻结、迁移、重映射、PNG/报告导出窄用例。
PNG 编码后真实回读尺寸/Alpha，再原子发布；JSON/CSV serializer 拒绝非有限值且报告 DTO 不含路径或图片字节。
无状态数学服务均为 singleton，不缓存 Document 数据。
