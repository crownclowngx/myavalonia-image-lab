# G3 用例与统一摘要记录

状态：完成（2026-08-30）。

实际修改：新增准备、投影、像素检查、摘要导出四个窄用例；`ImageComparisonSession` 独占两张原图、两张代理和
一份基础差异场。`ImageComparisonSummary` 不含路径，应用层 `ImageComparisonReport` 只加入文件名和完成时间。
schema 1 使用固定英文属性顺序；正无穷 PSNR 表示为 `value:null + isInfinite:true`；输出委托原子写入器。

证据：顺序解码、尺寸不匹配无伪 Session、Dispose 后拒绝访问、合法 JSON、绝对路径/像素/堆栈隐私和 UTF-8 导出通过。
风险：schema 1 首次正式发布后必须以新版本演进；当前未实现旧 schema 读取，因为尚无发布数据。
