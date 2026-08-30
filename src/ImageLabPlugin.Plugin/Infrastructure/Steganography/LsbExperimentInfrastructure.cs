using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using ImageLabPlugin.Application.LsbSteganography;
using ImageLabPlugin.Application.Ports;

namespace ImageLabPlugin.Infrastructure.Steganography;

/// <summary>先检查文件元数据、读取后再检查实际长度，堵住读取期间文件变化造成的超限。</summary>
internal sealed class LsbPayloadFileReader : ILsbPayloadFileReader
{
    public async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("二进制 Payload 文件不存在。", path);
        if (info.Length > Domain.Steganography.LsbPayload.MaximumBytes) throw new InvalidOperationException("二进制 Payload 读取前检查超过 64 KiB。 ");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > Domain.Steganography.LsbPayload.MaximumBytes) throw new InvalidOperationException("二进制 Payload 读取后检查超过 64 KiB。 ");
        return bytes;
    }
}

/// <summary>版本化报告适配器；明确排除 Payload、Frame 原文、绝对路径、用户名和异常堆栈。</summary>
internal sealed class LsbExperimentReportSerializer
{
    public byte[] SerializeJson(LsbExperimentSession session) => JsonSerializer.SerializeToUtf8Bytes(CreateModel(session), new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    public byte[] SerializeCsv(LsbExperimentSession session)
    {
        var value = CreateModel(session);
        var header = "schema_version,width,height,opaque_pixels,eligible_slots,recipe_id,bit_plane,seed,frame_bytes,changed_slots,unchanged_slots,mse_rgb,psnr_rgb_db,scope,samples,cover_one_ratio,stego_one_ratio,cover_chi_square,stego_chi_square,cover_p_value,stego_p_value,frame_status,fragility_preset,frame_ber,r_cover_one_ratio,r_stego_one_ratio,g_cover_one_ratio,g_stego_one_ratio,b_cover_one_ratio,b_stego_one_ratio,notice\r\n";
        string F(object? item) => item switch
        {
            null => string.Empty,
            double number when !double.IsFinite(number) => string.Empty,
            IFormattable formattable => Escape(formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => Escape(item.ToString() ?? string.Empty)
        };
        value.Channels.TryGetValue("R", out var red); value.Channels.TryGetValue("G", out var green); value.Channels.TryGetValue("B", out var blue);
        var row = string.Join(',', new object?[] { value.SchemaVersion, value.Width, value.Height, value.OpaquePixels, value.EligibleSlots, value.RecipeId, value.BitPlane, value.Seed,
            value.FrameBytes, value.ChangedSlots, value.UnchangedSlots, value.MseRgb, value.PsnrRgbDb, value.Scope, value.Samples, value.CoverOneRatio, value.StegoOneRatio,
            value.CoverChiSquare, value.StegoChiSquare, value.CoverPValue, value.StegoPValue, value.FrameStatus, value.FragilityPreset, value.FrameBer,
            red?.CoverOneRatio, red?.StegoOneRatio, green?.CoverOneRatio, green?.StegoOneRatio, blue?.CoverOneRatio, blue?.StegoOneRatio, value.Notice }.Select(F));
        return Encoding.UTF8.GetBytes(header + row + "\r\n");
    }

    private static LsbReportModel CreateModel(LsbExperimentSession session)
    {
        var facts = session.EmbeddingFacts!;
        var statistics = session.Statistics!;
        var recipe = session.Recipe!.Value;
        var channels = statistics.ByChannel.ToDictionary(
            item => item.Key switch { Domain.Steganography.LsbChannel.Red => "R", Domain.Steganography.LsbChannel.Green => "G", _ => "B" },
            item => new LsbChannelReportModel(item.Value.Cover.SampleCount, item.Value.Cover.Distribution.OneRatio, item.Value.Stego.Distribution.OneRatio,
                item.Value.Cover.PairOfValues.Value, item.Value.Stego.PairOfValues.Value, item.Value.Cover.PairOfValues.PValue, item.Value.Stego.PairOfValues.PValue,
                item.Value.Cover.Horizontal.TransitionRate, item.Value.Stego.Horizontal.TransitionRate, item.Value.Cover.Vertical.TransitionRate, item.Value.Stego.Vertical.TransitionRate));
        return new(1, session.SourceImage.Size.Width, session.SourceImage.Size.Height, session.Layout.OpaquePixelCount,
            session.Layout.GetEligibleSlotCount(recipe.Channels), recipe.StableId, recipe.BitPlane, recipe.Seed, "公开复现实验参数，不是密码或密钥",
            session.Frame.Length, facts.ChangedSlots, facts.UnchangedSlots, facts.MseRgb, facts.PsnrRgbDb,
            statistics.Cover.Scope.ToString(), statistics.Cover.SampleCount, statistics.Cover.Distribution.OneRatio, statistics.Stego.Distribution.OneRatio,
            statistics.Cover.PairOfValues.Value, statistics.Stego.PairOfValues.Value, statistics.Cover.PairOfValues.PValue, statistics.Stego.PairOfValues.PValue,
            session.SelfCheck!.Status.ToString(), session.Fragility?.Preset.ToString(), session.Fragility?.FrameBer.Ratio, channels,
            "教学与实验用途；统计结果不证明存在隐写或不可检测；CRC 不提供认证；本协议不同于 DCT 鲁棒水印。");
    }

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
