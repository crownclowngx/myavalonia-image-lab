using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageLabPlugin.Application.HybridImage;
using ImageLabPlugin.Domain.HybridImage;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>严格读写独立的 hybrid-image-v1 配方，不保存路径或图片像素。</summary>
/// <remarks>
/// DTO 固定所有协议字段，未知成员和重复属性都会拒绝。枚举事实显式写入并逐项核对，避免未来默认值变化后
/// 静默用另一套 Alpha、采样、边界或量化语义解释旧配方。
/// </remarks>
internal sealed class HybridImageRecipeSerializer : IHybridImageRecipeSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public byte[] Serialize(HybridImageRecipe recipe, string fingerprintA, string fingerprintB)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ValidateFingerprint(fingerprintA, nameof(fingerprintA));
        ValidateFingerprint(fingerprintB, nameof(fingerprintB));
        var dto = new RecipeDto(HybridImageProtocol.Schema, HybridImageProtocol.Recipe, "1.0.0",
            "A-low-reference", "B-high-aligned", fingerprintA, fingerprintB,
            recipe.Points.Select(static point => new PointDto(point.Id, point.PointA.X, point.PointA.Y,
                point.PointB.X, point.PointB.Y)).ToArray(),
            new CropDto(recipe.Crop.Left, recipe.Crop.Top, recipe.Crop.Right, recipe.Crop.Bottom),
            recipe.LowSigmaPixels, recipe.HighSigmaPixels, recipe.LowGain, recipe.HighGain,
            "gray-white-background", "gaussian-3sigma", "reflect101", "bilinear-pixel-center",
            "to-even-clamp", new[] { 1, 2, 4, 8 }, recipe.Fingerprint());
        return JsonSerializer.SerializeToUtf8Bytes(dto, Options);
    }

    public HybridImageRecipe Deserialize(ReadOnlySpan<byte> json, out string fingerprintA, out string fingerprintB)
    {
        if (json.IsEmpty || json.Length > HybridImageProtocol.MaximumJsonBytes)
            throw new InvalidDataException("Hybrid Image 配方为空或超过 256 KiB 上限。");
        StrictJson.EnsureNoDuplicateProperties(json);
        RecipeDto dto;
        try { dto = JsonSerializer.Deserialize<RecipeDto>(json, Options) ?? throw new InvalidDataException("配方为空。"); }
        catch (JsonException exception) { throw new InvalidDataException($"Hybrid Image 配方结构无效：{exception.Message}", exception); }
        if (dto.Schema != HybridImageProtocol.Schema || !StringComparer.Ordinal.Equals(dto.Protocol, HybridImageProtocol.Recipe))
            throw new InvalidDataException("不支持该 Hybrid Image schema 或协议。");
        if (dto.RoleA != "A-low-reference" || dto.RoleB != "B-high-aligned" ||
            dto.Color != "gray-white-background" || dto.Filter != "gaussian-3sigma" ||
            dto.Border != "reflect101" || dto.Sampling != "bilinear-pixel-center" ||
            dto.Quantization != "to-even-clamp" || dto.ScaleDivisors is null ||
            !dto.ScaleDivisors.SequenceEqual(new[] { 1, 2, 4, 8 }))
            throw new InvalidDataException("配方包含未知或不完整的固定算法事实。");
        if (dto.Points is null || dto.Crop is null) throw new InvalidDataException("配方缺少控制点或裁切矩形。");
        ValidateFingerprint(dto.FingerprintA, nameof(dto.FingerprintA));
        ValidateFingerprint(dto.FingerprintB, nameof(dto.FingerprintB));
        HybridImageRecipe recipe;
        try
        {
            var points = dto.Points.Select(point => new HybridAlignmentPointPair(point.Id,
                new HybridNormalizedPoint(point.AX, point.AY), new HybridNormalizedPoint(point.BX, point.BY))).ToArray();
            recipe = new HybridImageRecipe(points,
                new HybridNormalizedCrop(dto.Crop.Left, dto.Crop.Top, dto.Crop.Right, dto.Crop.Bottom),
                dto.LowSigmaPixels, dto.HighSigmaPixels, dto.LowGain, dto.HighGain);
        }
        catch (ArgumentException exception) { throw new InvalidDataException($"配方数值越界：{exception.Message}", exception); }
        if (!StringComparer.Ordinal.Equals(recipe.Fingerprint(), dto.RecipeFingerprint))
            throw new InvalidDataException("配方指纹校验失败。");
        fingerprintA = dto.FingerprintA!;
        fingerprintB = dto.FingerprintB!;
        return recipe;
    }

    private static void ValidateFingerprint(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 24 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{name} 不是 24 位内容指纹。");
    }

    private sealed record RecipeDto(int Schema, string? Protocol, string? CreatedWithVersion,
        string? RoleA, string? RoleB, string? FingerprintA, string? FingerprintB,
        PointDto[]? Points, CropDto? Crop, double LowSigmaPixels, double HighSigmaPixels,
        double LowGain, double HighGain, string? Color, string? Filter, string? Border,
        string? Sampling, string? Quantization, int[]? ScaleDivisors, string? RecipeFingerprint);
    private sealed record PointDto(int Id, double AX, double AY, double BX, double BY);
    private sealed record CropDto(double Left, double Top, double Right, double Bottom);
}

/// <summary>输出不含绝对路径、原图像素或截图的 Hybrid Image JSON/CSV 事实。</summary>
internal sealed class HybridImageReportSerializer : IHybridImageReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public byte[] SerializeJson(HybridImageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.SerializeToUtf8Bytes(report, Options);
    }

    public byte[] SerializeCsv(HybridImageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new (string Key, string Value)[]
        {
            ("protocol", report.Protocol), ("schema", F(report.Schema)),
            ("fingerprintA", report.FingerprintA), ("fingerprintB", report.FingerprintB),
            ("sizeA", $"{report.SizeA.Width}x{report.SizeA.Height}"),
            ("sizeB", $"{report.SizeB.Width}x{report.SizeB.Height}"),
            ("recipeFingerprint", report.RecipeFingerprint),
            ("scale", D(report.Alignment.Scale)), ("rotationDegrees", D(report.Alignment.RotationDegrees)),
            ("rmsResidualPixels", D(report.Alignment.RmsResidualPixels)),
            ("maximumResidualPixels", D(report.Alignment.MaximumResidualPixels)),
            ("coverageRatio", D(report.Alignment.CoverageRatio)),
            ("crop", $"{report.Crop.X},{report.Crop.Y},{report.Crop.Width},{report.Crop.Height}"),
            ("lowSigmaPixels", D(report.LowSigmaPixels)), ("highSigmaPixels", D(report.HighSigmaPixels)),
            ("lowF50", D(report.LowFiftyPercentCutoff)), ("highF50", D(report.HighFiftyPercentCutoff)),
            ("lowGain", D(report.LowGain)), ("highGain", D(report.HighGain)),
            ("rawMinimum", D(report.Raw.Minimum)), ("rawMaximum", D(report.Raw.Maximum)),
            ("rawMean", D(report.Raw.Mean)), ("clippedRatio", D(report.Raw.ClippedRatio)),
            ("elapsedMilliseconds", F(report.ElapsedMilliseconds)), ("limitation", report.Limitation)
        };
        var builder = new StringBuilder("key,value\r\n");
        foreach (var row in rows) builder.Append(Escape(row.Key)).Append(',').Append(Escape(row.Value)).Append("\r\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string F(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>集中读写轻量快照；恢复只还原意图，不触发文件访问或数值执行。</summary>
internal sealed class HybridImageSnapshotSerializer : IHybridImageSnapshotSerializer
{
    private const int MaximumBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public JsonElement Serialize(HybridImageSnapshotState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = JsonSerializer.SerializeToElement(state, Options);
        if (Encoding.UTF8.GetByteCount(result.GetRawText()) > MaximumBytes)
            throw new InvalidOperationException("Hybrid Image 快照超过 32 KiB 上限。");
        return result;
    }

    public HybridImageSnapshotState? Deserialize(JsonElement payload)
    {
        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumBytes)
            throw new InvalidDataException("Hybrid Image 快照超过 32 KiB 上限。");
        try { return payload.Deserialize<HybridImageSnapshotState>(Options); }
        catch (JsonException exception) { throw new InvalidDataException($"Hybrid Image 快照结构无效：{exception.Message}", exception); }
    }
}
