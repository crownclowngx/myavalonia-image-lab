# G2：配方与历史

- 实现不可变归一化点、频带锁定、六种稳定操作和 Recipe 指纹。
- 所有集合防御性复制；非有限、越界、零面积和预算在进入 Rasterizer 前拒绝。
- `MaskEditHistory` 保存小型操作和游标，支持 undo/redo、新编辑清 redo，不保存遮罩快照。
