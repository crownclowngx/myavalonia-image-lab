using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageLabPlugin.Application.SpectralArt;
using ImageLabPlugin.Domain.SpectralArt;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>严格读写 Spectral Art recipe schema 1，并对 Pattern 权重做无损 Brotli 压缩。</summary>
/// <remarks>
/// DTO 固定字段、禁止未知成员；解析前再遍历 JSON 对象拒绝重复属性，避免同一关键参数因“最后一个值生效”而产生
/// 歧义。Pattern 使用 little-endian IEEE 754 原始位模式压缩，解压前后都检查尺寸和精确字节数，因此配方往返不会
/// 改变灰度权重或指纹。该 serializer 不读取类型名，也不使用反射式多态。
/// </remarks>
internal sealed class SpectralArtRecipeSerializer : ISpectralArtRecipeSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public byte[] Serialize(SpectralArtRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var dto = new RecipeDto(SpectralArtProtocol.RecipeSchema, SpectralArtProtocol.RecipeProtocol,
            "1.0.0", recipe.Pattern.Width, recipe.Pattern.Height,
            recipe.Pattern.SamplingMode.ToString(), recipe.Pattern.SourceKind.ToString(),
            Compress(recipe.Pattern.WeightSpan), recipe.Pattern.Fingerprint,
            new RegionDto(recipe.Region.Left, recipe.Region.Top, recipe.Region.Right, recipe.Region.Bottom),
            recipe.FitMode.ToString(), recipe.Strength, recipe.Fingerprint());
        return JsonSerializer.SerializeToUtf8Bytes(dto, Options);
    }

    public SpectralArtRecipe Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty || json.Length > ImportSpectralArtRecipeUseCase.MaximumJsonBytes)
            throw new InvalidDataException("Spectral Art 配方为空或超过 4 MiB 上限。");
        StrictJson.EnsureNoDuplicateProperties(json);
        RecipeDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<RecipeDto>(json, Options)
                ?? throw new InvalidDataException("Spectral Art 配方为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Spectral Art 配方结构无效：{exception.Message}", exception);
        }
        if (dto.Schema != SpectralArtProtocol.RecipeSchema ||
            !StringComparer.Ordinal.Equals(dto.Protocol, SpectralArtProtocol.RecipeProtocol))
            throw new InvalidDataException("不支持该 Spectral Art schema 或协议。");
        if (dto.PatternWidth is < 1 or > SpectralPattern.MaximumEdge ||
            dto.PatternHeight is < 1 or > SpectralPattern.MaximumEdge)
            throw new InvalidDataException("配方 Pattern 尺寸越界。");
        if (!Enum.TryParse<SpectralPatternSamplingMode>(dto.SamplingMode, false, out var sampling) ||
            !Enum.TryParse<SpectralPatternSourceKind>(dto.SourceKind, false, out var sourceKind) ||
            !Enum.TryParse<SpectralPatternFitMode>(dto.FitMode, false, out var fitMode))
            throw new InvalidDataException("配方包含未知枚举值。");
        if (dto.Region is null) throw new InvalidDataException("配方缺少区域。");
        var weights = Decompress(dto.PatternData, checked(dto.PatternWidth * dto.PatternHeight));
        var pattern = new SpectralPattern(dto.PatternWidth, dto.PatternHeight, weights, sampling, sourceKind);
        if (!StringComparer.Ordinal.Equals(pattern.Fingerprint, dto.PatternFingerprint))
            throw new InvalidDataException("Pattern 指纹校验失败。");
        var recipe = new SpectralArtRecipe(pattern,
            new SpectralArtRegion(dto.Region.Left, dto.Region.Top, dto.Region.Right, dto.Region.Bottom),
            fitMode, dto.Strength);
        if (!StringComparer.Ordinal.Equals(recipe.Fingerprint(), dto.RecipeFingerprint))
            throw new InvalidDataException("Recipe 指纹校验失败。");
        return recipe;
    }

    private static string Compress(ReadOnlySpan<double> weights)
    {
        var raw = new byte[checked(weights.Length * sizeof(double))];
        for (var i = 0; i < weights.Length; i++)
            BitConverter.TryWriteBytes(raw.AsSpan(i * sizeof(double), sizeof(double)),
                BitConverter.DoubleToInt64Bits(weights[i]));
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(raw);
        return Convert.ToBase64String(output.ToArray());
    }

    private static double[] Decompress(string? encoded, int sampleCount)
    {
        if (string.IsNullOrWhiteSpace(encoded)) throw new InvalidDataException("配方缺少 Pattern 数据。");
        byte[] compressed;
        try { compressed = Convert.FromBase64String(encoded); }
        catch (FormatException exception) { throw new InvalidDataException("Pattern Base64 无效。", exception); }
        var expectedBytes = checked(sampleCount * sizeof(double));
        if (compressed.Length > ImportSpectralArtRecipeUseCase.MaximumJsonBytes)
            throw new InvalidDataException("压缩 Pattern 超过预算。");
        using var input = new MemoryStream(compressed, writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        var raw = new byte[expectedBytes];
        var offset = 0;
        while (offset < raw.Length)
        {
            var read = brotli.Read(raw, offset, raw.Length - offset);
            if (read == 0) break;
            offset += read;
        }
        if (offset != expectedBytes || brotli.ReadByte() != -1)
            throw new InvalidDataException("Pattern 解压长度与声明尺寸不一致。");
        var result = new double[sampleCount];
        for (var i = 0; i < result.Length; i++)
            result[i] = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(raw, i * sizeof(double)));
        return result;
    }

    private sealed record RecipeDto(int Schema, string Protocol, string CreatedWithVersion,
        int PatternWidth, int PatternHeight, string SamplingMode, string SourceKind,
        string PatternData, string PatternFingerprint, RegionDto? Region, string FitMode,
        double Strength, string RecipeFingerprint);
    private sealed record RegionDto(double Left, double Top, double Right, double Bottom);
}

/// <summary>输出不含路径、原文字和图像像素的 JSON/CSV 实验事实。</summary>
internal sealed class SpectralArtReportSerializer : ISpectralArtReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // 强度 0 的无损 PSNR 是正无穷；JSON 以明确字符串 "Infinity" 表达，避免伪造有限上限。
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public byte[] SerializeJson(SpectralArtReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.SerializeToUtf8Bytes(report, Options);
    }

    public byte[] SerializeCsv(SpectralArtReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new (string Key, string Value)[]
        {
            ("protocol", report.Protocol), ("schema", report.Schema.ToString(CultureInfo.InvariantCulture)),
            ("sourceFingerprint", report.SourceFingerprint), ("width", report.Width.ToString(CultureInfo.InvariantCulture)),
            ("height", report.Height.ToString(CultureInfo.InvariantCulture)),
            ("paddedWidth", report.PaddedWidth.ToString(CultureInfo.InvariantCulture)),
            ("paddedHeight", report.PaddedHeight.ToString(CultureInfo.InvariantCulture)),
            ("patternSource", report.PatternSource.ToString()), ("patternFingerprint", report.PatternFingerprint),
            ("strength", report.Strength.ToString("R", CultureInfo.InvariantCulture)),
            ("psnrY", report.Quality.PsnrLumaDb.ToString("R", CultureInfo.InvariantCulture)),
            ("psnrRgb", report.Quality.PsnrRgbDb.ToString("R", CultureInfo.InvariantCulture)),
            ("ssimY", report.Quality.GlobalSsimLuma.ToString("R", CultureInfo.InvariantCulture)),
            ("changedBins", report.Frequency.TotalWrittenBins.ToString(CultureInfo.InvariantCulture)),
            ("energyIncreaseRatio", report.Frequency.EnergyIncreaseRatio.ToString("R", CultureInfo.InvariantCulture)),
            ("maximumConjugateResidual", report.Frequency.MaximumConjugateResidual.ToString("R", CultureInfo.InvariantCulture)),
            ("maximumImaginaryResidual", report.Raw.MaximumImaginaryResidual.ToString("R", CultureInfo.InvariantCulture)),
            ("visibilityAvailable", report.Frequency.Visibility.IsAvailable.ToString(CultureInfo.InvariantCulture)),
            ("visibilityIncrease", report.Frequency.Visibility.VisibilityIncrease.ToString("R", CultureInfo.InvariantCulture)),
            ("limitation", report.Limitation)
        };
        var builder = new StringBuilder("key,value\r\n");
        foreach (var row in rows) builder.Append(Escape(row.Key)).Append(',').Append(Escape(row.Value)).Append("\r\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string Escape(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>集中处理 Spectral Art 轻量工作区快照，使 Document 不包含 JSON DTO 或序列化策略。</summary>
internal sealed class SpectralArtSnapshotSerializer : ISpectralArtSnapshotSerializer
{
    private const int MaximumBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public JsonElement Serialize(SpectralArtSnapshotState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var payload = JsonSerializer.SerializeToElement(state, Options);
        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumBytes)
            throw new InvalidOperationException("Spectral Art 快照超过 32 KiB 上限。");
        return payload;
    }

    public SpectralArtSnapshotState? Deserialize(JsonElement payload)
    {
        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumBytes)
            throw new InvalidDataException("Spectral Art 快照超过 32 KiB 上限。");
        try { return payload.Deserialize<SpectralArtSnapshotState>(Options); }
        catch (JsonException exception) { throw new InvalidDataException($"Spectral Art 快照结构无效：{exception.Message}", exception); }
    }
}

internal static class StrictJson
{
    public static void EnsureNoDuplicateProperties(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            Visit(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"JSON 结构无效：{exception.Message}", exception);
        }
    }

    private static void Visit(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"JSON 对象包含重复字段：{property.Name}。");
                Visit(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) Visit(item);
        }
    }
}
