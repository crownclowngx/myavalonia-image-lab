# G3：CDF 5/3

- 以第二个朴素 Strategy 实现 predict/update lifting 和端点复制边界。
- 逆变换严格撤销 update/predict 并交织偶奇样本；不增加 Haar 分支。
- 使用与 Haar 相同的尺寸、取消、所有权和正逆契约。
- 门禁覆盖奇偶尺寸、方向、6 层和策略隔离；明确不执行 Parseval 断言。
