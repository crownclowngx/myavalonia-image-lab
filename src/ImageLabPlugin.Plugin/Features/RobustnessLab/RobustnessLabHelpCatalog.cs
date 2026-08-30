using ImageLabPlugin.Domain.Robustness;

namespace ImageLabPlugin.Features.RobustnessLab;

/// <summary>
/// 描述一个可扫描参数在界面中的中文含义。
/// </summary>
/// <remarks>
/// <see cref="ParameterId"/> 仍是写入配方和报告的稳定英文 ID；其余字段只服务于展示，避免把中文文案
/// 混入领域协议。默认值与步进仅用于编辑体验，最终合法性仍由领域层验证器统一判定。
/// </remarks>
internal sealed record RobustnessParameterHelp(
    string ParameterId,
    string DisplayName,
    string UnitAndRange,
    string Direction,
    string SuggestedScan,
    decimal DefaultValue,
    decimal Increment)
{
    public string DisplayLabel => $"{DisplayName}（{ParameterId}）";
    public override string ToString() => DisplayLabel;

    public static RobustnessParameterHelp Unknown(string parameterId) => new(
        parameterId,
        "未知参数",
        "当前版本没有该参数的范围说明",
        "请先预检；未知参数会被安全阻断",
        "不要在不了解来源时运行恢复的旧配方",
        0m,
        0.01m);
}

/// <summary>
/// 描述一种扰动（攻击）要模拟的现实处理及其主要观察目的。
/// </summary>
internal sealed record RobustnessAttackHelp(
    PerturbationKind Kind,
    string DisplayName,
    string Description,
    string Purpose,
    string Caution,
    IReadOnlyList<RobustnessParameterHelp> Parameters,
    string? UnrecognizedKindId = null)
{
    public string KindId => UnrecognizedKindId ?? Kind.ToStableId();
    public string DisplayLabel => $"{DisplayName}（{KindId}）";
    public override string ToString() => DisplayLabel;
}

/// <summary>
/// 鲁棒性实验室的中文帮助目录。
/// </summary>
/// <remarks>
/// 这里采用朴素的只读目录，而不是让每个算法对象承担 UI 文案职责。这样算法仍只负责像素变换，展示层可以
/// 单独改进术语和教学说明；同时每一种领域枚举都必须在测试中拥有且只拥有一条帮助记录。
/// </remarks>
internal static class RobustnessLabHelpCatalog
{
    private static RobustnessParameterHelp P(string id, string name, string range, string direction, string scan, decimal value, decimal increment = 0.01m) =>
        new(id, name, range, direction, scan, value, increment);

    public static IReadOnlyList<RobustnessAttackHelp> Attacks { get; } =
    [
        new(PerturbationKind.JpegReencode, "JPEG 重新压缩",
            "把当前图片编码为 JPEG 后再解码，模拟社交平台、聊天软件或重复导出造成的有损压缩。",
            "观察频域水印在量化和高频细节丢失后的恢复边界。",
            "JPEG 不保留透明通道；载体含非不透明像素时预检会阻止执行。",
            [P("quality", "JPEG 质量", "整数 1–100", "数值越低，压缩通常越强；100 也不是无损", "建议先从 95 降到 50，步长 5", 95m, 1m)]),
        new(PerturbationKind.Scale, "双线性缩放",
            "按宽、高比例重新采样图片，模拟平台缩略图、尺寸调整和非等比拉伸。",
            "观察水印对尺寸变化、插值以及 DCT 分块位置变化的敏感程度。",
            "只扫描一个方向会产生非等比拉伸；尺寸改变后 PSNR/SSIM 会显示 N/A。",
            [P("scale-x", "水平缩放倍数", "0.05–8；1 表示不变", "离 1 越远，尺寸变化越大", "等比实验应分别保持两个方向一致；V1 一次只扫描一个参数", 1m), P("scale-y", "垂直缩放倍数", "0.05–8；1 表示不变", "离 1 越远，尺寸变化越大", "可用 1→0.5、步长 0.1 观察缩小边界", 1m)]),
        new(PerturbationKind.GaussianNoise, "高斯随机噪声",
            "为每个 RGB 通道加入零均值高斯噪声，同一种子和案例会得到完全相同的噪声。",
            "模拟传感器噪声、传输扰动或后期处理中均匀分布于全图的细小误差。",
            "需要统计稳定性时增加 trial；Alpha 通道保持不变。",
            [P("sigma", "噪声标准差 σ", "0–100 个灰度级；0 表示不变", "σ 越大，噪声越强", "建议 0→30、步长 2 或 5，并使用 3–5 次 trial", 0m)]),
        new(PerturbationKind.SaltPepperNoise, "椒盐噪声",
            "按精确比例随机选择像素并置为纯黑或纯白，模拟脉冲噪声和坏点。",
            "观察少量极端像素错误对水印投票与纠错能力的影响。",
            "比例是 0–1 的小数，不是百分数；随机实验建议增加 trial。",
            [P("ratio", "受污染像素比例", "0–1；0.05 表示 5%", "比例越大，攻击越强", "建议 0→0.2、步长 0.01 或 0.02", 0m, 0.01m)]),
        new(PerturbationKind.DeterministicPixel, "确定性像素扰动",
            "按固定棋盘符号给 RGB 通道加减相同幅度，不使用随机数。",
            "提供完全可复现的逐像素压力测试，便于定位数值边界。",
            "它不是自然噪声模型，结果适合回归比较，不应冒充真实平台行为。",
            [P("amplitude", "像素扰动幅度", "整数 0–255；0 表示不变", "数值越大，明暗交替误差越强", "建议 0→40、步长 2 或 5", 0m, 1m)]),
        new(PerturbationKind.GaussianBlur, "高斯模糊",
            "使用高斯核平滑邻域像素，逐渐移除边缘和高频纹理。",
            "观察依赖中频 DCT 系数的水印在平滑处理后的衰减。",
            "σ 增大时计算量也会上升；边界采用复制边缘策略。",
            [P("sigma", "模糊标准差 σ", "0–10 像素；0 表示不变", "σ 越大，模糊越强", "建议 0→3、步长 0.25 或 0.5", 0m, 0.25m)]),
        new(PerturbationKind.MedianBlur, "中值滤波",
            "用邻域中值替代当前像素，常用于去除椒盐噪声，同时也会改变细节。",
            "观察非线性去噪对水印系数和局部纹理的破坏。",
            "当前只接受 3×3 或 5×5；没有 1×1 恒等档。",
            [P("kernel-size", "滤波核边长", "仅整数 3 或 5", "5×5 通常比 3×3 更强", "使用显式的 3、5 两点比较；范围扫描可设 3→5、步长 2", 3m, 2m)]),
        new(PerturbationKind.UnsharpMask, "反锐化遮罩锐化",
            "从原图减去模糊图得到细节，再按比例叠加回原图，以增强边缘。",
            "观察过度锐化、边缘振铃和像素截断是否破坏水印。",
            "锐化不是模糊的简单逆过程；较大数值可能快速产生饱和截断。",
            [P("amount", "锐化量", "0–5；0 表示不变", "数值越大，边缘增强越强", "建议 0→2、步长 0.1 或 0.25", 0m, 0.1m)]),
        new(PerturbationKind.Crop, "边缘裁剪",
            "从指定边缘删除像素，输出图片尺寸随之缩小。",
            "模拟截图、二次构图，并观察内容缺失和 DCT 网格重定位的影响。",
            "四边总裁剪量必须保留至少 1×1 像素；尺寸改变后全参考质量为 N/A。",
            [P("left", "左侧裁剪", "非负整数，单位：像素", "数值越大，左侧删除越多", "建议从 0 开始按 1、2 或 8 像素递增", 0m, 1m), P("top", "顶部裁剪", "非负整数，单位：像素", "数值越大，顶部删除越多", "可重点测试非 8 的倍数以观察分块错位", 0m, 1m), P("right", "右侧裁剪", "非负整数，单位：像素", "数值越大，右侧删除越多", "建议先单边扫描，避免原因混杂", 0m, 1m), P("bottom", "底部裁剪", "非负整数，单位：像素", "数值越大，底部删除越多", "建议先单边扫描，避免原因混杂", 0m, 1m)]),
        new(PerturbationKind.Pad, "边缘补边",
            "在指定边缘增加透明黑色像素，原图内容不缩放但画布尺寸变大。",
            "观察内容在画布中的位置变化和 DCT 分块起点偏移。",
            "尺寸改变后 PSNR/SSIM 为 N/A；当前补边颜色固定为透明黑。",
            [P("left", "左侧补边", "非负整数，单位：像素", "数值越大，原图向右偏移越多", "建议 0→16、步长 1，重点观察 8 像素周期", 0m, 1m), P("top", "顶部补边", "非负整数，单位：像素", "数值越大，原图向下偏移越多", "建议 0→16、步长 1", 0m, 1m), P("right", "右侧补边", "非负整数，单位：像素", "只扩展右侧画布，不移动原点", "建议与左侧补边分开测试", 0m, 1m), P("bottom", "底部补边", "非负整数，单位：像素", "只扩展底部画布，不移动原点", "建议与顶部补边分开测试", 0m, 1m)]),
        new(PerturbationKind.Translate, "固定画布平移",
            "保持画布尺寸不变，将内容水平或垂直移动，移出区域丢弃，空白区域填透明黑。",
            "直接测试水印对像素坐标和 8×8 分块同步偏移的敏感性。",
            "参数必须是整数；正 dx 向右、正 dy 向下。",
            [P("dx", "水平位移 dx", "整数像素，绝对值不超过 100000", "正数向右，负数向左；绝对值越大偏移越强", "建议 -8→8 或 0→16、步长 1", 0m, 1m), P("dy", "垂直位移 dy", "整数像素，绝对值不超过 100000", "正数向下，负数向上；绝对值越大偏移越强", "建议单独扫描，避免与水平位移混杂", 0m, 1m)]),
        new(PerturbationKind.Rotate, "固定画布旋转",
            "围绕图片中心旋转并保持原画布尺寸，边缘超出部分被裁掉，空白处填透明黑。",
            "模拟轻微校正或拍摄倾斜，观察几何不同步对水印的影响。",
            "允许 -15° 到 15°；正负表示相反方向，0° 不变。",
            [P("degrees", "旋转角度", "-15–15，单位：度", "绝对值越大，几何攻击通常越强", "建议 -5→5 或 0→10、步长 0.5", 0m, 0.5m)]),
        new(PerturbationKind.Perspective, "轻度透视变换",
            "按四个角的归一化偏移扭曲图片，模拟斜拍、投影或轻微梯形变形。",
            "观察水印在非线性坐标对应和双线性重采样后的恢复能力。",
            "每次只扫描一个角的一个方向；数值范围 -0.1–0.1，0 表示不偏移。",
            [P("top-left-x", "左上角水平偏移", "-0.1–0.1，相对图片宽度", "绝对值越大，扭曲越强", "建议 -0.05→0.05、步长 0.01", 0m), P("top-left-y", "左上角垂直偏移", "-0.1–0.1，相对图片高度", "绝对值越大，扭曲越强", "建议单角单方向扫描", 0m), P("top-right-x", "右上角水平偏移", "-0.1–0.1，相对图片宽度", "绝对值越大，扭曲越强", "建议单角单方向扫描", 0m), P("top-right-y", "右上角垂直偏移", "-0.1–0.1，相对图片高度", "绝对值越大，扭曲越强", "建议单角单方向扫描", 0m), P("bottom-right-x", "右下角水平偏移", "-0.1–0.1，相对图片宽度", "绝对值越大，扭曲越强", "建议单角单方向扫描", 0m), P("bottom-right-y", "右下角垂直偏移", "-0.1–0.1，相对图片高度", "绝对值越大，扭曲越强", "建议单角单方向扫描", 0m), P("bottom-left-x", "左下角水平偏移", "-0.1–0.1，相对图片宽度", "绝对值越大，扭曲越强", "建议单角单方向扫描", 0m), P("bottom-left-y", "左下角垂直偏移", "-0.1–0.1，相对图片高度", "绝对值越大，扭曲越强", "建议单角单方向扫描", 0m)]),
        new(PerturbationKind.Brightness, "亮度偏移",
            "给 RGB 三个通道统一加上固定灰度值，并将结果截断到 0–255。",
            "模拟曝光、提亮或压暗处理对水印的影响。",
            "正数变亮，负数变暗；绝对值过大会产生大片纯白或纯黑。",
            [P("offset", "亮度偏移量", "整数 -255–255；0 表示不变", "正数提亮，负数压暗；绝对值越大越强", "建议 -50→50 或分别从 0 向两侧扫描", 0m, 1m)]),
        new(PerturbationKind.Contrast, "对比度调整",
            "以 127.5 为中心拉伸或压缩 RGB 灰度差异。",
            "模拟图片编辑器中的对比度处理以及高光/阴影截断。",
            "1 不变；0 变成中灰；大于 1 增强对比度。",
            [P("factor", "对比度系数", "0–4；1 表示不变", "离 1 越远变化越大；大于 1 增强，小于 1 降低", "建议 0.5→2、步长 0.1", 1m, 0.1m)]),
        new(PerturbationKind.Gamma, "Gamma 校正",
            "按 output = 255 × (input/255)^(1/gamma) 重新映射亮度。",
            "模拟非线性明暗调整，观察水印对中间调变化的敏感性。",
            "1 不变；本实现中大于 1 变亮，小于 1 变暗。",
            [P("gamma", "Gamma 值", "0.1–10；1 表示不变", "离 1 越远，非线性变化越强", "建议 0.5→2、步长 0.1", 1m, 0.1m)]),
        new(PerturbationKind.Saturation, "饱和度调整",
            "保持亮度基准，缩放每个 RGB 通道相对亮度的色彩差异。",
            "模拟去色、增强色彩等常见后期操作。",
            "0 为灰度，1 不变，大于 1 增强色彩并可能截断。",
            [P("factor", "饱和度系数", "0–4；1 表示不变", "离 1 越远变化越大", "建议 0→2、步长 0.1", 1m, 0.1m)]),
        new(PerturbationKind.ColorBias, "RGB 色偏",
            "只给红、绿或蓝通道增加固定偏移，模拟白平衡错误和色彩偏移。",
            "区分水印对不同颜色通道变化的敏感程度。",
            "V1 一次只扫描一个通道；正数增强该色，负数减弱该色。",
            [P("red", "红色通道偏移", "整数 -255–255；0 表示不变", "正数增红，负数减红；绝对值越大越强", "建议 -50→50、步长 5", 0m, 1m), P("green", "绿色通道偏移", "整数 -255–255；0 表示不变", "正数增绿，负数减绿；绝对值越大越强", "建议单通道扫描", 0m, 1m), P("blue", "蓝色通道偏移", "整数 -255–255；0 表示不变", "正数增蓝，负数减蓝；绝对值越大越强", "建议单通道扫描", 0m, 1m)])
    ];

    public static RobustnessAttackHelp? Find(string kindId) =>
        Attacks.FirstOrDefault(value => StringComparer.Ordinal.Equals(value.KindId, kindId));

    public static RobustnessAttackHelp FindOrUnknown(string kindId) => Find(kindId) ?? new(
        (PerturbationKind)(-1),
        "未知扰动",
        $"恢复的配方包含当前版本不认识的稳定 ID：{kindId}。",
        "未知扰动不会被猜测执行，以免产生含义错误的实验结果。",
        "请删除该步骤或使用创建它的兼容版本；预检会保持失败。",
        [],
        kindId);
}
