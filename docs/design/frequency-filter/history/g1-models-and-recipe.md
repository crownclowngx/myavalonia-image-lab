# G1：模型与配方

- 新增稳定枚举、不可变 `FrequencyFilterRecipe`、遮罩/平面/投影结果及 SHA-256 截断指纹。
- 不适用外截止、阶数和 Direct 增益在构造时规范化；非法状态在算法前拒绝。
- 回滚只需移除 `Domain/FrequencyFiltering` 新消费者，不影响共享 FFT。
