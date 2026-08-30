using System.Text.Json;
using ImageLabPlugin.Application.FrequencyMaskEditing;
using ImageLabPlugin.Domain.FrequencyMaskEditing;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>以固定 DTO 严格转换 schema 1 配方，不读取类型名，也不使用反射式多态。</summary>
internal sealed class FrequencyMaskRecipeSerializer : IFrequencyMaskRecipeSerializer
{
    private const int Schema = 1;
    private const string ProductId = "myavalonia.plugin.image.lab.document.frequency-mask-editor";
    private const string CoordinateProtocol = "centered-display-normalized-v1";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public byte[] Serialize(FrequencyMaskRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var operations = recipe.Operations.Select(ToDto).ToArray();
        var dto = new RecipeDto(Schema, ProductId, "1.0.0", CoordinateProtocol, "all-pass",
            recipe.Strength, recipe.OriginalPaddedWidth, recipe.OriginalPaddedHeight, operations, recipe.Fingerprint());
        return JsonSerializer.SerializeToUtf8Bytes(dto, Options);
    }

    public FrequencyMaskRecipe Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty || json.Length > ImportFrequencyMaskRecipeUseCase.MaximumJsonBytes)
            throw new InvalidDataException("配方 JSON 为空或超过 1 MiB 上限。");
        RecipeDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<RecipeDto>(json, Options)
                ?? throw new InvalidDataException("配方 JSON 为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"配方 JSON 结构无效：{exception.Message}", exception);
        }
        if (dto.Schema != Schema) throw new InvalidDataException($"不支持配方 schema {dto.Schema}。");
        if (!StringComparer.Ordinal.Equals(dto.ProductId, ProductId)) throw new InvalidDataException("配方产品稳定 ID 不匹配。");
        if (!StringComparer.Ordinal.Equals(dto.CoordinateProtocol, CoordinateProtocol)) throw new InvalidDataException("不支持该坐标协议。");
        if (!StringComparer.Ordinal.Equals(dto.Baseline, "all-pass")) throw new InvalidDataException("V1 只支持全通基线。");
        if (dto.Operations is null) throw new InvalidDataException("配方缺少操作序列。");
        var recipe = new FrequencyMaskRecipe(dto.Strength, dto.Operations.Select(FromDto), dto.OriginalPaddedWidth,
            dto.OriginalPaddedHeight);
        if (!StringComparer.OrdinalIgnoreCase.Equals(dto.Fingerprint, recipe.Fingerprint()))
            throw new InvalidDataException("配方指纹校验失败，文件可能被篡改或损坏。");
        return recipe;
    }

    private static OperationDto ToDto(FrequencyMaskOperation operation) => new(
        KindName(operation.Kind),
        operation.Points.Select(static point => new PointDto(point.X, point.Y)).ToArray(),
        new PointDto(operation.Start.X, operation.Start.Y),
        new PointDto(operation.End.X, operation.End.Y),
        operation.Radius, operation.InnerRadius, operation.OuterRadius, operation.TargetGain, operation.Opacity,
        operation.BandLock is { } band ? new BandDto(band.InnerRadius, band.OuterRadius) : null);

    private static FrequencyMaskOperation FromDto(OperationDto dto)
    {
        if (dto is null) throw new InvalidDataException("操作不能为空。");
        var points = dto.Points?.Select(static point => Point(point)).ToArray() ?? [];
        var start = Point(dto.Start);
        var end = Point(dto.End);
        var band = dto.BandLock is null ? (FrequencyBandLock?)null : new(dto.BandLock.InnerRadius, dto.BandLock.OuterRadius);
        return dto.Kind switch
        {
            "brush" => FrequencyMaskOperation.Brush(points, dto.Radius, dto.TargetGain, dto.Opacity, band),
            "erase" => FrequencyMaskOperation.Eraser(points, dto.Radius, dto.Opacity, band),
            "rectangle" => FrequencyMaskOperation.Rectangle(start, end, dto.TargetGain, dto.Opacity, band),
            "ring" => FrequencyMaskOperation.Ring(start, dto.InnerRadius, dto.OuterRadius, dto.TargetGain, dto.Opacity, band),
            "invertAll" => FrequencyMaskOperation.Invert(),
            "resetAllPass" => FrequencyMaskOperation.Reset(),
            _ => throw new InvalidDataException($"未知遮罩操作 kind：{dto.Kind}。")
        };
    }

    private static NormalizedFrequencyPoint Point(PointDto? point) => point is null
        ? throw new InvalidDataException("操作缺少坐标。")
        : new NormalizedFrequencyPoint(point.X, point.Y);

    private static string KindName(FrequencyMaskOperationKind kind) => kind switch
    {
        FrequencyMaskOperationKind.BrushStroke => "brush",
        FrequencyMaskOperationKind.EraseStroke => "erase",
        FrequencyMaskOperationKind.RectangleFill => "rectangle",
        FrequencyMaskOperationKind.RingFill => "ring",
        FrequencyMaskOperationKind.InvertAll => "invertAll",
        FrequencyMaskOperationKind.ResetAllPass => "resetAllPass",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record RecipeDto(int Schema, string ProductId, string CreatedWithVersion, string CoordinateProtocol,
        string Baseline, double Strength, int? OriginalPaddedWidth, int? OriginalPaddedHeight,
        OperationDto[]? Operations, string Fingerprint);
    private sealed record OperationDto(string Kind, PointDto[]? Points, PointDto? Start, PointDto? End,
        double Radius, double InnerRadius, double OuterRadius, double TargetGain, double Opacity, BandDto? BandLock);
    private sealed record PointDto(double X, double Y);
    private sealed record BandDto(double InnerRadius, double OuterRadius);
}
