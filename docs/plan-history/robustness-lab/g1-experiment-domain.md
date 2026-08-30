# G1 实验领域

新增强类型 `PerturbationParameters`、稳定 Kind ID、步骤、decimal 列表/范围扫描、配方验证、稳定案例键、资源乘积和配方哈希。计划顺序固定为 Profile、扫描点、trial，执行完成时序不会改变报告顺序。

设计取舍：持久 DTO 可以是文本，但进入算法前必须转成强类型记录；没有反射 schema、通用 DAG、Factory/Builder/Visitor 堆叠。测试覆盖端点、去重、重复 StepId、未知参数、超限和哈希稳定性。
