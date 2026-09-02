# Image Oscilloscope 数学原理

## 1. 信号与显示边界

V1 输入是 8-bit sRGB RGBA 静态图片。示波器描述图片经固定白底合成后的离散编码值，不描述场景线性光、HDR 亮度、显示器实测亮度或广播电平。所有 Scope 使用同一颜色事实，避免 Waveform、Vectorscope 和探针各自采用不同公式。

对每个通道字节 `C∈[0,255]` 和 Alpha `A∈[0,255]`：

```text
Cvisible = roundToEven((A × C + (255 - A) × 255) / 255)
```

全透明像素因此表现为白色，半透明像素表现为它在白底上的可见颜色。原 Alpha 只进入探针详情，不进入 Scope 统计。

## 2. Luma、色度与 HSV

### 2.1 编码亮度

使用与 ImageLab 既有亮度分析一致的 BT.601 权重：

```text
Y = 0.299R + 0.587G + 0.114B
yBin = clamp(roundToEven(Y), 0, 255)
```

这里是 gamma-coded luma，不是线性光 luminance。白色得到 255，黑色得到 0，纯绿色的 Y 高于纯红和纯蓝。

### 2.2 归一化色度

```text
Cb = (B - Y) / (1.772 × 255)
Cr = (R - Y) / (1.402 × 255)
```

中性灰满足 `Cb=Cr=0`。理论和浮点误差处理后，V1 把显示坐标约束到 `[-0.5,+0.5]`。它没有数字视频的 128 偏移，也没有 16/235 legal range。

色度半径和方向：

```text
chroma = sqrt(Cb² + Cr²)
angle  = atan2(Cr, Cb)
```

平均向量必须从每个像素的未量化 `Cb/Cr` 在线累计：

```text
meanCb = ΣCb / N
meanCr = ΣCr / N
```

平均向量可能由画面主体颜色造成，不能直接解释为白平衡错误。

### 2.3 HSV 饱和度和 Hue

先把 R/G/B 归一化到 `[0,1]`，令 `max=max(r,g,b)`、`min=min(r,g,b)`、`delta=max-min`：

```text
S = 0                  当 max = 0
S = delta / max        其他情况
```

Hue 采用标准分段公式并归一化到 `[0,360)`。当 `delta<=1e-12` 或 `S<=1e-12` 时 Hue 无定义，不把该像素塞入 0°。Hue 分布使用 `S` 作为权重，使近灰像素不会用数值噪声主导色相图。

## 3. Luma Waveform

Waveform 保留图片的横向位置，压缩或展开到最多 1024 个显示列：

```text
waveformWidth = min(sourceWidth, 1024)
scopeX = floor(sourceX × waveformWidth / sourceWidth)
scopeRow = 255 - yBin
W[scopeX,scopeRow] += 1
```

`scopeRow=0` 表示亮度 255，`scopeRow=255` 表示亮度 0。映射使用半开区间，因此 `sourceX=sourceWidth-1` 一定落在最后一个合法 Scope 列。

守恒关系：

```text
Σx Σrow W[x,row] = sourceWidth × sourceHeight
```

当多个源列汇入一个 Scope 列，密度变高但没有丢弃样本。Waveform 不能用于精确恢复源像素位置，它是横向位置与亮度的联合分布。

## 4. RGB Parade

Parade 对合成后的三个通道分别使用与 Waveform 相同的横轴映射：

```text
PR[scopeX,255-R] += 1
PG[scopeX,255-G] += 1
PB[scopeX,255-B] += 1
```

每个通道独立守恒：

```text
ΣPR = ΣPG = ΣPB = N
```

三个通道共享密度显示上限。若分别自动拉伸，一个稀疏通道可能看起来与密集通道同样强，会破坏横向比较。

## 5. Vectorscope

固定栅格边长 `V=512`：

```text
column = roundToEven((clamp(Cb,-0.5,0.5) + 0.5) × (V-1))
row    = roundToEven((0.5 - clamp(Cr,-0.5,0.5)) × (V-1))
S[column,row] += 1
```

右侧为正 Cb（偏蓝），上方为正 Cr（偏红），中心为中性色。六个参考色目标由同一公式把纯 R、Magenta、B、Cyan、G、Yellow 投影得到；它们不是 broadcast 75% target，也不表示合法范围判定。

守恒关系：

```text
Σcolumn Σrow S[column,row] = N
```

点云接近中心表示色度较低，远离中心通常表示色度较高。由于 YCbCr 半径与 HSV S 定义不同，两者相关但不相等。

## 6. 直方图与分布

R/G/B/Y 直方图分别把每个离散字节计入唯一 bin：

```text
HR[R] += 1
HG[G] += 1
HB[B] += 1
HY[yBin] += 1
```

每组总计均为 N。UI 可以显示计数或百分比，但 Domain 保留整数计数；百分比为 `count/N`，空图片在进入 Domain 前已被拒绝。

饱和度映射：

```text
saturationBin = clamp(roundToEven(S × 255),0,255)
```

Hue 使用 360 个一度 bin：

```text
hueBin = floor(Hue) mod 360
HHue[hueBin] += S
```

色度半径采用固定最大值 `sqrt(0.5²+0.5²)`：

```text
chromaBin = clamp(roundToEven(chroma / sqrt(0.5) × 255),0,255)
```

固定量纲使不同图片可比较；不得按每张图自己的最大色度归一化。

## 7. 高光与阴影裁切

给定整数阈值 `0<=shadow<highlight<=255`：

```text
lumaShadow    = yBin <= shadow
lumaHighlight = yBin >= highlight
rgbShadow     = min(R,G,B) <= shadow
rgbHighlight  = max(R,G,B) >= highlight
```

同时记录每个通道的 `C<=shadow` 与 `C>=highlight`。这里的“裁切警告”是阈值命中，不证明文件编码前确实发生传感器或调色链裁切。阈值包含边界，默认 shadow=5、highlight=250。

覆盖层代理的一个像素可能对应多个源像素。为避免漏掉孤立裁切点，聚合规则是：只要其覆盖的任一源像素命中，代理 mask 即命中。覆盖层是诊断投影，不参与计数，也不写回源图。

## 8. 密度到显示亮度

Scope 计数动态范围可能很大。默认对数显示对每个非零格集合取确定性 P99.5 nearest-rank 上限 `L`：

```text
tone(count) = clamp(log(1+count) / log(1+max(1,L)),0,1)
```

线性模式：

```text
toneLinear(count) = clamp(count / max(1,L),0,1)
```

P99.5 以上的格显示为最亮，但原始计数不被截断。Parade 把三通道非零格合并后计算一个 L；Waveform 与 Vectorscope 各用自己的 L。

## 9. 探针映射

源像素 `(x,y)` 先按本章公式得到 `R/G/B/A/Y/Cb/Cr/H/S`，再映射到：

- Waveform：`(scopeX,255-yBin)`；
- Parade：三个 `(scopeX,255-C)`；
- Vectorscope：`(column,row)`；
- 直方图：R/G/B/Y 四个 bin；
- 分布：S、Hue（若定义）与 chroma bin。

原图 Pointer 到源像素的换算必须先去除 View 的 letterbox，并以像素中心规则处理边界。该坐标换算与颜色公式分离，便于独立测试。Hover 不改变任何分析计数；pin 只保存坐标，不复制像素。

## 10. 解释限制

- 高光/阴影阈值命中不是相机 RAW 裁切证明；
- 平均 Cb/Cr 偏离中心不是白平衡错误证明；
- 饱和度高不是超出目标色域证明；
- sRGB/BT.601 数值不等于 Rec.709 视频示波器的完整工程语义；
- 白底 Alpha 合成代表当前固定观察协议，不代表所有 UI 背景下的外观；
- V1 的 Scope 适合静态 8-bit 图片分析与教学，不能冒充认证级视频测量设备。
