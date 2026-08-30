# G2：响应与遮罩

- `RadialFilterResponse` 用完整 switch 实现十二种组合，没有引入 Strategy/工厂目录。
- Butterworth 使用对数域防溢出；遮罩复用 `FrequencyCoordinates`，按行观察取消。
- Golden 覆盖截止、互补、有限值、过渡和共轭对称。
