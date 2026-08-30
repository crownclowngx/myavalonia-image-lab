# 卷积核实验台数学原理

## 1. 真二维离散卷积

K 为奇数，`r=(K-1)/2`。矩阵 `(row,column)` 对应 `ky=row-r`、`kx=column-r`：

```text
acc(x,y) = Σky Σkx h(ky,kx) f(x-kx,y-ky)
raw(x,y) = acc(x,y) / d
v(x,y) = raw(x,y) + bias
```

最后 `Math.Round(v, AwayFromZero)` 并裁切到 `[0,255]`。只有最后一步量化，乘加过程保持 double 和固定行优先顺序。

相关运算读取 `f(x+kx,y+ky)`。对称低通核下两者相同，非对称 impulse Golden 会把响应放到相反侧，因此可作为方向门禁。

## 2. 边界扩展

长度 n 的越界索引 i：

- Constant：直接返回显式常量，不映射坐标。
- Replicate：`clamp(i,0,n-1)`。
- Reflect-101：周期 `2n-2`；n=3 时为 `...2,1|0,1,2|1,0...`，不重复边缘。
- Wrap：`((i%n)+n)%n`。

二维分别映射 X/Y。核大于图片时同一公式自然处理多次反射或环绕。

## 3. 归一化和 DC

有效除数 d 为 1、`Σh`、`Σ|h|` 或显式值。接近 0 的除数会使算子含义不稳定，产品选择阻断而不是回退。线性核频率响应的 DC 为 `H(0,0)=Σh/d`：归一化低通为 1；一阶导数和 Laplacian 为 0；High Boost 为 A-1。

## 4. 平滑、锐化和高提升

Gaussian 使用 `G(x,y)=exp(-(x²+y²)/(2σ²))` 后归一化。Unsharp 为 `(1+a)δ-aG`，等价于 `f+a(f-Gf)`，核和为 1。High Boost 为 `Aδ-G`，等价于 `Af-Gf`，DC 为 A-1。它们增强数值变化，不证明恢复真实细节。

## 5. 梯度与 Laplacian

Sobel、Prewitt、Scharr 近似一阶方向导数。X/Y 各自线性，Magnitude：

```text
m = sqrt(Gx² + Gy²)
```

不是线性卷积，因为平方和开方不满足叠加。Laplacian 是二阶差分，四/八邻域核均零和；常量图内部响应应为 0。

## 6. 核频率响应

将归一化核 `h/d` 的中心锚点搬到 256 周期网格原点：负 k 坐标以模 256 落到数组尾部。二维 FFT 得到：

```text
H(u,v) = Σy Σx h(x,y)/d * exp(-i2π(ux+vy)/256)
```

显示前 fftshift，把未移位数组 `(0,0)` 的 DC 搬到中心。幅值采用 `log(1+|H|)` 显示，相位映射 `[-π,π]`。偏置是仿射常数、边界使有限图不再严格平移不变、裁切是非线性，所以三者都不属于核响应。

## 7. 通道和 Alpha

RGB 模式独立处理三个字节。单 Y/Cb/Cr 模式先抽取公共全范围分量，替换一个分量后转回 RGB；颜色立方体有限，回写可能裁切。Alpha 不参与，因为内部像素是未预乘 RGBA；处理 Alpha 会改变透明度并使颜色边缘含义不清。

## 8. 代理与完整尺寸

面积平均代理抑制缩小时的混叠，但 `Convolve(Downsample(f))` 通常不等于 `Downsample(Convolve(f))`，尤其在边界、裁切和非线性 Magnitude 下。因此代理只用于交互观察；完整尺寸必须显式执行并独立绑定配方指纹。
