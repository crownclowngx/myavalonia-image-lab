using System.Text.Json;
using System.Text.Json.Serialization;
using ImageLabPlugin.Application.PeriodicNoiseRemoval;
using ImageLabPlugin.Domain.Shared.Imaging;
using ImageLabPlugin.Domain.PeriodicNoiseRemoval;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>以固定 DTO 严格转换周期陷波 schema 1 配方。</summary>
/// <remarks>
/// 反序列化先检查输入大小、重复属性和未知字段，再验证产品 ID、版本、枚举、频率、数量与指纹。外部文件只提供
/// canonical 中心，逐 bin 遮罩永不进入配方边界；失败会抛出结构化异常且不会返回部分对象。
/// </remarks>
internal sealed class PeriodicNoiseRecipeSerializer : IPeriodicNoiseRecipeSerializer
{
    private const string AlgorithmVersion = "periodic-notch-v1";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public byte[] Serialize(PeriodicNoiseRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var dto = new RecipeDto(PeriodicNoiseRecipe.ProductId, recipe.SchemaVersion, AlgorithmVersion,
            ChannelName(recipe.Channel), recipe.Transition.ToString(), recipe.Radius, recipe.Strength,
            recipe.ButterworthOrder, recipe.Notches.Select(item => new NotchDto(item.CanonicalFrequency.Fx,
                item.CanonicalFrequency.Fy, item.Origin.ToString(), item.Enabled)).ToArray(), recipe.Fingerprint());
        return JsonSerializer.SerializeToUtf8Bytes(dto, Options);
    }

    public PeriodicNoiseRecipe Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty || json.Length > ImportPeriodicNoiseRecipeUseCase.MaximumJsonBytes)
            throw new InvalidDataException("配方 JSON 为空或超过 1 MiB 上限。");
        EnsureNoDuplicateProperties(json);
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
        if (!StringComparer.Ordinal.Equals(dto.ProductId, PeriodicNoiseRecipe.ProductId))
            throw new InvalidDataException("配方产品稳定 ID 不匹配。");
        if (dto.SchemaVersion != PeriodicNoiseRecipe.CurrentSchemaVersion)
            throw new InvalidDataException($"不支持配方 schema {dto.SchemaVersion}。");
        if (!StringComparer.Ordinal.Equals(dto.AlgorithmVersion, AlgorithmVersion))
            throw new InvalidDataException("不支持该周期陷波算法版本。");
        if (dto.Notches is null) throw new InvalidDataException("配方缺少 notches。");
        if (dto.Notches.Length > 32) throw new InvalidDataException("配方陷波中心超过 32 对上限。");
        try
        {
            var recipe = new PeriodicNoiseRecipe(ParseChannel(dto.Channel),
                Enum.Parse<PeriodicNotchTransition>(dto.Transition, ignoreCase: false), dto.Radius, dto.Strength,
                dto.ButterworthOrder, dto.Notches.Select(item => new PeriodicNotch(
                    new PeriodicFrequency(item.Fx, item.Fy),
                    Enum.Parse<PeriodicNotchOrigin>(item.Origin, ignoreCase: false), item.Enabled)), dto.SchemaVersion);
            if (!StringComparer.OrdinalIgnoreCase.Equals(dto.Fingerprint, recipe.Fingerprint()))
                throw new InvalidDataException("配方指纹校验失败，文件可能被修改或损坏。");
            return recipe;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"配方字段不合法：{exception.Message}", exception);
        }
    }

    private static void EnsureNoDuplicateProperties(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            CheckElement(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"配方 JSON 语法无效：{exception.Message}", exception);
        }
    }

    private static void CheckElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"配方 JSON 包含重复属性：{property.Name}。");
                CheckElement(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) CheckElement(child);
        }
    }

    private static string ChannelName(ImageChannel channel) => channel switch
    {
        ImageChannel.Red => "R",
        ImageChannel.Green => "G",
        ImageChannel.Blue => "B",
        ImageChannel.Luma => "Y",
        ImageChannel.ChromaBlue => "Cb",
        ImageChannel.ChromaRed => "Cr",
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    private static ImageChannel ParseChannel(string channel) => channel switch
    {
        "R" => ImageChannel.Red,
        "G" => ImageChannel.Green,
        "B" => ImageChannel.Blue,
        "Y" => ImageChannel.Luma,
        "Cb" => ImageChannel.ChromaBlue,
        "Cr" => ImageChannel.ChromaRed,
        _ => throw new InvalidDataException($"未知通道：{channel}。")
    };

    private sealed record RecipeDto(string ProductId, int SchemaVersion, string AlgorithmVersion, string Channel,
        string Transition, double Radius, double Strength, int ButterworthOrder, NotchDto[]? Notches,
        string Fingerprint);
    private sealed record NotchDto(double Fx, double Fy, string Origin, bool Enabled);
}
