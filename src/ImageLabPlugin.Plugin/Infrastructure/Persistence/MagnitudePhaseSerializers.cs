using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageLabPlugin.Application.MagnitudePhaseSwap;
using ImageLabPlugin.Domain.MagnitudePhaseSwap;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>严格读写 magnitude-phase-swap-v1 配方，拒绝未知/重复字段与指纹篡改。</summary>
/// <remarks>固定的白底、BT.601、FitContain、共轭、相位插值和投影事实全部进入协议，且不保存文件路径或像素。</remarks>
internal sealed class MagnitudePhaseRecipeSerializer : IMagnitudePhaseRecipeSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public byte[] Serialize(MagnitudePhaseRecipe recipe, string fingerprintA, string fingerprintB)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ValidateFingerprint(fingerprintA); ValidateFingerprint(fingerprintB);
        var dto = new RecipeDto(MagnitudePhaseProtocol.Schema, MagnitudePhaseProtocol.Recipe, "1.0.0",
            fingerprintA, fingerprintB, recipe.CanvasSize, recipe.MagnitudeMode.ToString(),
            recipe.MagnitudeAmount, recipe.PhaseMode.ToString(), recipe.PhaseAmount,
            recipe.ProjectionKind.ToString(), "white-srgb-bt601-fit-contain",
            "area-down-bilinear-pixel-center-up", "unshifted-conjugate-representative",
            "shortest-arc-positive-pi-tie", "to-even-clamp-or-p995-signed", recipe.Fingerprint());
        return JsonSerializer.SerializeToUtf8Bytes(dto, Options);
    }

    public MagnitudePhaseRecipe Deserialize(ReadOnlySpan<byte> json, out string fingerprintA,
        out string fingerprintB)
    {
        if (json.IsEmpty || json.Length > MagnitudePhaseProtocol.MaximumJsonBytes)
            throw new InvalidDataException("幅相交换配方为空或超过 256 KiB 上限。");
        StrictJson.EnsureNoDuplicateProperties(json);
        RecipeDto dto;
        try { dto = JsonSerializer.Deserialize<RecipeDto>(json, Options) ?? throw new InvalidDataException("配方为空。"); }
        catch (JsonException exception) { throw new InvalidDataException($"幅相交换配方结构无效：{exception.Message}", exception); }
        if (dto.Schema != MagnitudePhaseProtocol.Schema || dto.Protocol != MagnitudePhaseProtocol.Recipe ||
            dto.Canvas != "white-srgb-bt601-fit-contain" || dto.Sampling != "area-down-bilinear-pixel-center-up" ||
            dto.Conjugate != "unshifted-conjugate-representative" || dto.PhaseInterpolation != "shortest-arc-positive-pi-tie" ||
            dto.Projection != "to-even-clamp-or-p995-signed")
            throw new InvalidDataException("配方协议或固定算法事实不受支持。");
        ValidateFingerprint(dto.FingerprintA); ValidateFingerprint(dto.FingerprintB);
        if (!Enum.TryParse<MagnitudeComponentMode>(dto.MagnitudeMode, false, out var magnitudeMode) ||
            !Enum.TryParse<PhaseComponentMode>(dto.PhaseMode, false, out var phaseMode) ||
            !Enum.TryParse<MagnitudePhaseProjectionKind>(dto.ProjectionKind, false, out var projectionKind))
            throw new InvalidDataException("配方枚举值无效。");
        MagnitudePhaseRecipe recipe;
        try
        {
            recipe = new MagnitudePhaseRecipe(dto.CanvasSize, magnitudeMode, dto.MagnitudeAmount,
            phaseMode, dto.PhaseAmount, projectionKind);
        }
        catch (ArgumentException exception) { throw new InvalidDataException($"配方数值无效：{exception.Message}", exception); }
        if (recipe.Fingerprint() != dto.RecipeFingerprint) throw new InvalidDataException("配方指纹校验失败。");
        fingerprintA = dto.FingerprintA!; fingerprintB = dto.FingerprintB!;
        return recipe;
    }

    private static void ValidateFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 24 || !value.All(Uri.IsHexDigit))
            throw new InvalidDataException("内容指纹必须是 24 位十六进制字符串。");
    }

    private sealed record RecipeDto(int Schema, string? Protocol, string? CreatedWithVersion,
        string? FingerprintA, string? FingerprintB, int CanvasSize, string? MagnitudeMode,
        double MagnitudeAmount, string? PhaseMode, double PhaseAmount, string? ProjectionKind,
        string? Canvas, string? Sampling, string? Conjugate, string? PhaseInterpolation,
        string? Projection, string? RecipeFingerprint);
}

/// <summary>输出脱敏 JSON/CSV 指标事实，不包含路径、像素、频谱或异常堆栈。</summary>
internal sealed class MagnitudePhaseReportSerializer : IMagnitudePhaseReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public byte[] SerializeJson(MagnitudePhaseReport report) => JsonSerializer.SerializeToUtf8Bytes(report, Options);

    public byte[] SerializeCsv(MagnitudePhaseReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var d = report.Diagnostics;
        var rows = new (string Key, string Value)[]
        {
            ("protocol", report.Protocol), ("schema", F(report.Schema)),
            ("fingerprintA", report.FingerprintA), ("fingerprintB", report.FingerprintB),
            ("recipeFingerprint", report.RecipeFingerprint), ("canvasSize", F(report.Recipe.CanvasSize)),
            ("magnitudeMode", report.Recipe.MagnitudeMode.ToString()), ("phaseMode", report.Recipe.PhaseMode.ToString()),
            ("projectionKind", report.Recipe.ProjectionKind.ToString()),
            ("magnitudeError", D(d.Mix.RelativeMagnitudeError)), ("phaseErrorRadians", D(d.Mix.WeightedPhaseErrorRadians)),
            ("undefinedPhaseCount", F(d.Mix.UndefinedPhaseCount)), ("borrowedPhaseEnergyRatio", D(d.Mix.BorrowedPhaseEnergyRatio)),
            ("maximumConjugateError", D(d.Mix.MaximumConjugateError)),
            ("maximumImaginaryResidual", D(d.MaximumImaginaryResidual)),
            ("relativeImaginaryResidual", D(d.RelativeImaginaryResidual)),
            ("parsevalA", D(d.SourceA.ParsevalRelativeError)), ("parsevalB", D(d.SourceB.ParsevalRelativeError)),
            ("parsevalResult", D(d.Result.ParsevalRelativeError)),
            ("nccA", Metric(d.Spatial.NccA)), ("nccB", Metric(d.Spatial.NccB)),
            ("gradientA", Metric(d.Spatial.GradientCorrelationA)), ("gradientB", Metric(d.Spatial.GradientCorrelationB)),
            ("psnrA", Metric(d.Spatial.PsnrA)), ("psnrB", Metric(d.Spatial.PsnrB)),
            ("ssimA", Metric(d.Spatial.SsimA)), ("ssimB", Metric(d.Spatial.SsimB)),
            ("elapsedMilliseconds", F(report.ElapsedMilliseconds)), ("limitation", report.Limitation)
        };
        var builder = new StringBuilder("key,value\r\n");
        foreach (var row in rows) builder.Append(Escape(row.Key)).Append(',').Append(Escape(row.Value)).Append("\r\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string Metric(MagnitudePhaseMetric metric) => metric.Status == MagnitudePhaseMetricStatus.Available
        ? D(metric.Value!.Value) : $"{metric.Status}:{metric.Reason}";
    private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string F(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>读写只含意图的轻量快照；恢复不会访问磁盘或执行 FFT。</summary>
internal sealed class MagnitudePhaseSnapshotSerializer : IMagnitudePhaseSnapshotSerializer
{
    private const int MaximumBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public JsonElement Serialize(MagnitudePhaseSnapshotState state)
    {
        var result = JsonSerializer.SerializeToElement(state, Options);
        if (Encoding.UTF8.GetByteCount(result.GetRawText()) > MaximumBytes)
            throw new InvalidOperationException("幅相交换快照超过 32 KiB 上限。");
        return result;
    }

    public MagnitudePhaseSnapshotState? Deserialize(JsonElement payload)
    {
        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumBytes)
            throw new InvalidDataException("幅相交换快照超过 32 KiB 上限。");
        try { return payload.Deserialize<MagnitudePhaseSnapshotState>(Options); }
        catch (JsonException exception) { throw new InvalidDataException($"幅相交换快照无效：{exception.Message}", exception); }
    }
}
