using ImageLabPlugin.Domain.Shared.Imaging;

namespace ImageLabPlugin.Domain.PoissonBlending;

/// <summary>
/// 把 guidance 来源/强度和 RHS 投影为目标尺寸热图。投影先完成全部 double 统计，再生成独立 byte 图片；
/// 归一化、颜色和纹理只服务解释，绝不会成为问题构造或下一轮迭代输入。
/// </summary>
internal sealed class PoissonFieldProjector(SrgbColorSpace colorSpace, PoissonGuidanceCatalog catalog)
{
    public PixelImage ProjectGuidance(PixelImage source, PixelImage target, PoissonBinaryMask mask,
        ImageOffset offset, PoissonBlendMode mode)
    {
        var strategy = catalog.Resolve(mode); var values = new double[checked((int)target.Size.PixelCount)];
        var selectedSource = new bool[values.Length]; double maximum = 0d;
        for (var sy = 0; sy < source.Size.Height; sy++) for (var sx = 0; sx < source.Size.Width; sx++)
        {
            if (!mask.Contains(sx, sy)) continue; var tx = sx + offset.Dx; var ty = sy + offset.Dy;
            var sp = Decode(source, sx, sy); var tp = Decode(target, tx, ty); double square = 0d; var sourceVotes = 0;
            foreach (var direction in new[] { (X: 1, Y: 0), (X: 0, Y: 1) })
            {
                var guidance = strategy.Evaluate(sp, Decode(source, sx + direction.X, sy + direction.Y), tp,
                    Decode(target, tx + direction.X, ty + direction.Y));
                for (var channel = 0; channel < strategy.ChannelCount; channel++) square += guidance.Get(channel) * guidance.Get(channel);
                if (guidance.SelectedSource) sourceVotes++;
            }
            var index = (ty * target.Size.Width) + tx; values[index] = Math.Sqrt(square); selectedSource[index] = sourceVotes >= 1;
            maximum = Math.Max(maximum, values[index]);
        }
        return Build(target.Size, index =>
        {
            var strength = maximum == 0d ? 0d : values[index] / maximum; var value = ToByte(strength);
            // 源 guidance 用蓝/实色倾向，目标 guidance 用橙/点纹倾向；旁边文字图例提供非颜色解释。
            return selectedSource[index] ? ((byte)32, (byte)(96 + (value / 2)), value) : (value, (byte)(80 + (value / 3)), (byte)24);
        });
    }

    public PixelImage ProjectRhs(PoissonProblem problem)
    {
        var scalar = new double[problem.UnknownCount]; double maximum = 0d;
        for (var i = 0; i < problem.UnknownCount; i++)
        {
            var sum = 0d; for (var channel = 0; channel < problem.ChannelCount; channel++) sum += problem.Rhs[(i * problem.ChannelCount) + channel];
            scalar[i] = sum / problem.ChannelCount; maximum = Math.Max(maximum, Math.Abs(scalar[i]));
        }
        var map = Enumerable.Repeat(-1, checked((int)problem.TargetSize.PixelCount)).ToArray();
        for (var i = 0; i < problem.UnknownCount; i++) map[(problem.TargetY[i] * problem.TargetSize.Width) + problem.TargetX[i]] = i;
        return Build(problem.TargetSize, index =>
        {
            var unknown = map[index]; if (unknown < 0) return ((byte)0, (byte)0, (byte)0);
            var normalized = maximum == 0d ? 0d : scalar[unknown] / maximum; var value = ToByte(Math.Abs(normalized));
            return normalized >= 0d ? (value, (byte)(32 + ((255 - value) / 4)), (byte)32) : ((byte)32, (byte)(32 + ((255 - value) / 4)), value);
        });
    }

    private PixelImage Build(ImageSize size, Func<int, (byte R, byte G, byte B)> color)
    {
        var bytes = new byte[checked((int)(size.PixelCount * 4))];
        for (var i = 0; i < size.PixelCount; i++) { var value = color(i); bytes[i * 4] = value.R; bytes[(i * 4) + 1] = value.G; bytes[(i * 4) + 2] = value.B; bytes[(i * 4) + 3] = 255; }
        return new PixelImage(size, bytes);
    }
    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0d, 1d) * 255d, MidpointRounding.ToEven);
    private LinearRgbColor Decode(PixelImage image, int x, int y) { var p = image.GetPixel(x, y); return colorSpace.Decode(SrgbColor.FromBytes(p.R, p.G, p.B)); }
}

/// <summary>
/// 把 double 解合成为目标尺寸 RGBA8888。域外从目标逐字节复制；域内只替换 RGB 且保留目标 Alpha。
/// 求解值允许暂时超出色域，此处显式 clamp 并统计通道/像素，不能把裁剪藏进颜色空间服务。
/// </summary>
internal sealed class PoissonBlendComposer(SrgbColorSpace colorSpace)
{
    public PoissonComposedImage Compose(PixelImage target, PoissonProblem problem, PoissonSolverState state)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(problem); ArgumentNullException.ThrowIfNull(state);
        if (target.Size != problem.TargetSize || state.Fingerprint != problem.Fingerprint)
            throw new InvalidOperationException("目标、问题或解 fingerprint 不一致。 ");
        var output = target.Clone(); long clippedChannels = 0; long clippedPixels = 0;
        for (var i = 0; i < problem.UnknownCount; i++)
        {
            LinearRgbColor linear;
            if (problem.ChannelCount == 3)
                linear = new(state.Values[i * 3], state.Values[(i * 3) + 1], state.Values[(i * 3) + 2]);
            else
            {
                var targetPixel = target.GetPixel(problem.TargetX[i], problem.TargetY[i]);
                var targetLinear = colorSpace.Decode(SrgbColor.FromBytes(targetPixel.R, targetPixel.G, targetPixel.B));
                var delta = state.Values[i] - MonochromeGuidanceStrategy.Luma(targetLinear);
                linear = new(targetLinear.Red + delta, targetLinear.Green + delta, targetLinear.Blue + delta);
            }
            if (!linear.IsFinite) throw new ArithmeticException("合成前解包含非有限数。 ");
            var clippedHere = 0;
            if (linear.Red is < 0d or > 1d) clippedHere++;
            if (linear.Green is < 0d or > 1d) clippedHere++;
            if (linear.Blue is < 0d or > 1d) clippedHere++;
            clippedChannels += clippedHere; if (clippedHere > 0) clippedPixels++;
            var encoded = colorSpace.Encode(new LinearRgbColor(Math.Clamp(linear.Red, 0d, 1d),
                Math.Clamp(linear.Green, 0d, 1d), Math.Clamp(linear.Blue, 0d, 1d))).ToBytes();
            output.SetRgb(problem.TargetX[i], problem.TargetY[i], encoded.Red, encoded.Green, encoded.Blue);
        }
        return new(output, new(clippedChannels, clippedPixels));
    }
}

/// <summary>
/// 同遮罩、同偏移的线性光直接 Alpha 对照。V1 预检要求源及 halo 不透明，因此域内通常等价于硬克隆，
/// 仍保留通用公式作为独立诚实基线，避免让 UI 或 Poisson 求解器承担第二种算法。
/// </summary>
internal sealed class DirectAlphaCompositor(SrgbColorSpace colorSpace)
{
    public PixelImage Compose(PixelImage source, PixelImage target, PoissonBinaryMask mask, ImageOffset offset)
    {
        var output = target.Clone();
        for (var sy = 0; sy < source.Size.Height; sy++) for (var sx = 0; sx < source.Size.Width; sx++)
        {
            if (!mask.Contains(sx, sy)) continue;
            var tx = sx + offset.Dx; var ty = sy + offset.Dy;
            if ((uint)tx >= (uint)target.Size.Width || (uint)ty >= (uint)target.Size.Height)
                throw new ArgumentOutOfRangeException(nameof(offset), "遮罩映射超出目标图。 ");
            var sourcePixel = source.GetPixel(sx, sy); var targetPixel = target.GetPixel(tx, ty);
            var a = sourcePixel.A / 255d;
            var s = colorSpace.Decode(SrgbColor.FromBytes(sourcePixel.R, sourcePixel.G, sourcePixel.B));
            var t = colorSpace.Decode(SrgbColor.FromBytes(targetPixel.R, targetPixel.G, targetPixel.B));
            var encoded = colorSpace.Encode(new LinearRgbColor(
                (a * s.Red) + ((1d - a) * t.Red), (a * s.Green) + ((1d - a) * t.Green),
                (a * s.Blue) + ((1d - a) * t.Blue))).ToBytes();
            output.SetRgb(tx, ty, encoded.Red, encoded.Green, encoded.Blue);
        }
        return output;
    }
}

/// <summary>生成只供显示的热图；归一化后的 byte 永远不会反馈到 RHS 或下一次迭代。</summary>
internal sealed class PoissonResidualProjector
{
    public PixelImage Project(PoissonProblem problem, PoissonSolverState state)
    {
        var residuals = new double[problem.UnknownCount]; double maximum = 0d;
        for (var i = 0; i < problem.UnknownCount; i++)
        {
            double square = 0d;
            for (var channel = 0; channel < problem.ChannelCount; channel++)
            {
                var flat = (i * problem.ChannelCount) + channel; var lhs = 4d * state.Values[flat];
                for (var d = 0; d < 4; d++) { var n = problem.NeighborIndices[(i * 4) + d]; if (n >= 0) lhs -= state.Values[(n * problem.ChannelCount) + channel]; }
                var r = problem.Rhs[flat] - lhs; square += r * r;
            }
            residuals[i] = Math.Sqrt(square / problem.ChannelCount); maximum = Math.Max(maximum, residuals[i]);
        }
        var bytes = new byte[checked((int)(problem.TargetSize.PixelCount * 4))];
        for (var p = 0; p < problem.TargetSize.PixelCount; p++) bytes[(p * 4) + 3] = 255;
        for (var i = 0; i < problem.UnknownCount; i++)
        {
            var normalized = maximum == 0d ? 0d : residuals[i] / maximum;
            var value = (byte)Math.Round(normalized * 255d, MidpointRounding.ToEven);
            var offset = ((problem.TargetY[i] * problem.TargetSize.Width) + problem.TargetX[i]) * 4;
            bytes[offset] = value; bytes[offset + 1] = (byte)(255 - value); bytes[offset + 2] = 64;
        }
        return new PixelImage(problem.TargetSize, bytes);
    }
}

/// <summary>计算方程/梯度解释指标；数值较小只说明更贴近所选 guidance，不代表主观视觉更好。</summary>
internal sealed class PoissonBlendDiagnosticsAnalyzer(SrgbColorSpace colorSpace, PoissonGuidanceCatalog catalog)
{
    private static readonly (int X, int Y)[] Directions = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    public PoissonBlendDiagnostics Analyze(PixelImage source, PixelImage target, PixelImage output,
        PoissonBinaryMask mask, ImageOffset offset, PoissonProblem problem, PoissonSolverState state,
        PoissonClampStatistics clamp)
    {
        var strategy = catalog.Resolve(problem.Mode); double boundary = 0d, interior = 0d; long boundaryTerms = 0, interiorTerms = 0;
        for (var sy = 0; sy < source.Size.Height; sy++) for (var sx = 0; sx < source.Size.Width; sx++)
        {
            if (!mask.Contains(sx, sy)) continue;
            foreach (var d in Directions)
            {
                var nsx = sx + d.X; var nsy = sy + d.Y;
                if ((uint)nsx >= (uint)source.Size.Width || (uint)nsy >= (uint)source.Size.Height) continue;
                var tx = sx + offset.Dx; var ty = sy + offset.Dy; var ntx = nsx + offset.Dx; var nty = nsy + offset.Dy;
                if ((uint)ntx >= (uint)target.Size.Width || (uint)nty >= (uint)target.Size.Height) continue;
                var guidance = strategy.Evaluate(Decode(source, sx, sy), Decode(source, nsx, nsy), Decode(target, tx, ty), Decode(target, ntx, nty));
                var op = Decode(output, tx, ty); var oq = Decode(output, ntx, nty);
                double error = 0d;
                for (var channel = 0; channel < strategy.ChannelCount; channel++)
                {
                    var gradient = strategy.ChannelCount == 1 ? MonochromeGuidanceStrategy.Luma(op) - MonochromeGuidanceStrategy.Luma(oq)
                        : PoissonProblemBuilder.Channel(op, channel) - PoissonProblemBuilder.Channel(oq, channel);
                    var difference = gradient - guidance.Get(channel); error += difference * difference;
                }
                // 内部无向边只按右/下方向计一次；跨边界边必须覆盖四个方向，否则会漏掉区域左侧和上侧。
                if (mask.Contains(nsx, nsy))
                { if (d.X > 0 || d.Y > 0) { interior += error; interiorTerms += strategy.ChannelCount; } }
                else { boundary += error; boundaryTerms += strategy.ChannelCount; }
            }
        }
        var last = state.History[^1]; var totalMixed = problem.SourceGuidanceEdges + problem.TargetGuidanceEdges;
        return new(boundaryTerms == 0 ? 0d : Math.Sqrt(boundary / boundaryTerms),
            interiorTerms == 0 ? 0d : Math.Sqrt(interior / interiorTerms), last.Rms, last.MaxAbs,
            problem.Mode == PoissonBlendMode.MixedGradient && totalMixed > 0 ? problem.SourceGuidanceEdges / (double)totalMixed : null, clamp);
    }

    private LinearRgbColor Decode(PixelImage image, int x, int y)
    { var p = image.GetPixel(x, y); return colorSpace.Decode(SrgbColor.FromBytes(p.R, p.G, p.B)); }
}
