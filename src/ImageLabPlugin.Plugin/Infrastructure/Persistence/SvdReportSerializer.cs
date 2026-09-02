using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ImageLabPlugin.Application.SvdDecomposition;
using ImageLabPlugin.Domain.Shared.Analysis;
using ImageLabPlugin.Domain.SvdDecomposition;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>把已验证的 SVD 实验事实表达为 schema 1 JSON/CSV。</summary>
/// <remarks>
/// 序列化器不重新计算矩阵，也不评价“最佳策略”。PSNR 无穷只表示像素误差为零；JSON 用
/// isExact=true、psnrDb=null 表达，确保输出仍是严格 JSON 数字协议，而不是 NaN/Infinity 字符串。
/// </remarks>
internal sealed class SvdReportSerializer : ISvdReportSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public byte[] SerializeJson(SvdExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var payload = new
        {
            report.Schema,
            report.NumericProtocol,
            product = "SVD Decomposition／奇异值分解重建",
            report.CreatedAtUtc,
            source = new { report.SourcePath, width = report.SourceSize.Width, height = report.SourceSize.Height },
            proxy = new { width = report.ProxySize.Width, height = report.ProxySize.Height, report.AnalysisMaximumEdge, label = "分析代理" },
            recipe = new
            {
                strategy = report.Decomposition.Strategy.ToString(),
                singleChannel = report.Decomposition.SingleChannel.ToString(),
                rank = report.RankResult.Rank,
                report.RankResult.RecipeFingerprint
            },
            channels = report.Decomposition.Channels.Select((channel, index) => new
            {
                channel = channel.Channel.ToString(),
                channel.Neutral,
                rows = channel.Factors.Rows,
                columns = channel.Factors.Columns,
                singularValues = channel.Factors.SingularValues.ToArray(),
                energy = BuildEnergy(channel.Factors),
                diagnostics = channel.Factors.Diagnostics,
                rankResult = report.RankResult.MatrixErrors[index]
            }),
            aggregateRetainedEnergy = report.RankResult.AggregateRetainedEnergy,
            imageQuality = BuildQuality(report.RankResult.Quality),
            report.RankResult.Clipping,
            component = report.Component is null ? null : new
            {
                channel = report.Component.Channel.ToString(), report.Component.ComponentIndex,
                report.Component.SingularValue, report.Component.EnergyShare, report.Component.RawMinimum,
                report.Component.RawMaximum, report.Component.DisplayScale,
                limitation = "分量按独立对称色标显示；重复奇异值子空间内的单列方向不唯一。"
            },
            comparison = report.Comparison is null ? null : new
            {
                report.Comparison.CommonRank,
                completionStatus = report.Comparison.CompletionStatus.ToString(),
                cases = report.Comparison.Cases.Select(item => new
                {
                    strategy = item.Strategy.ToString(), item.MatrixCount, item.CommonRank,
                    item.RetainedEnergy, quality = BuildQuality(item.Quality), elapsedMilliseconds = item.Elapsed.TotalMilliseconds
                })
            },
            report.Limitations
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    public byte[] SerializeCsv(SvdExperimentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var builder = new StringBuilder(
            "recordType,strategy,channel,index,rank,sigma,relativeSigma,energyShare,cumulativeEnergy,retainedEnergy,frobeniusError,relativeError,psnrRgbDb,psnrRgbExact,ssimY,sweeps,converged,orthogonality,elapsedMs\r\n");
        foreach (var channel in report.Decomposition.Channels)
        {
            var energy = new SingularValueEnergyAnalyzer().Analyze(channel.Factors);
            foreach (var sample in energy.Samples)
            {
                AppendRow(builder, "singular-value", report.Decomposition.Strategy.ToString(), channel.Channel.ToString(),
                    sample.ComponentIndex, null, sample.SingularValue, sample.RelativeSingularValue, sample.EnergyShare,
                    sample.CumulativeEnergy, null, null, null, null, null, null, null, null, null, null);
            }
        }
        foreach (var error in report.RankResult.MatrixErrors)
        {
            AppendRow(builder, "rank-result", report.RankResult.Strategy.ToString(), error.Channel.ToString(), null,
                report.RankResult.Rank, null, null, null, null, error.RetainedEnergy, error.DirectFrobeniusError,
                error.RelativeFrobeniusError, FiniteOrNull(report.RankResult.Quality.PsnrRgbDb),
                double.IsPositiveInfinity(report.RankResult.Quality.PsnrRgbDb), report.RankResult.Quality.GlobalSsimLuma,
                null, null, null, report.RankResult.Elapsed.TotalMilliseconds);
        }
        if (report.Comparison is not null)
        foreach (var item in report.Comparison.Cases)
        {
            AppendRow(builder, "strategy-case", item.Strategy.ToString(), null, null, item.CommonRank, null, null, null,
                null, item.RetainedEnergy, null, null, FiniteOrNull(item.Quality.PsnrRgbDb),
                double.IsPositiveInfinity(item.Quality.PsnrRgbDb), item.Quality.GlobalSsimLuma,
                null, null, null, item.Elapsed.TotalMilliseconds);
        }
        foreach (var channel in report.Decomposition.Channels)
        {
            var diagnostics = channel.Factors.Diagnostics;
            AppendRow(builder, "diagnostics", report.Decomposition.Strategy.ToString(), channel.Channel.ToString(), null,
                null, null, null, null, null, null, null, null, null, null, null, diagnostics.Sweeps,
                diagnostics.Converged, Math.Max(diagnostics.MaximumUOrthogonalityError,
                    diagnostics.MaximumVOrthogonalityError), report.Decomposition.Elapsed.TotalMilliseconds);
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(builder.ToString());
    }

    private static object BuildEnergy(SvdFactors factors)
    {
        var report = new SingularValueEnergyAnalyzer().Analyze(factors);
        return new { report.TotalEnergy, report.NumericRank, report.NumericRankTolerance,
            status = report.Status.ToString(), samples = report.Samples };
    }

    private static object BuildQuality(FullReferenceQualityMetrics quality) => new
    {
        quality.MeanAbsoluteErrorRgb,
        quality.RootMeanSquareErrorRgb,
        psnrRgb = new { isExact = double.IsPositiveInfinity(quality.PsnrRgbDb), psnrDb = FiniteOrNull(quality.PsnrRgbDb) },
        psnrY = new { isExact = double.IsPositiveInfinity(quality.PsnrLumaDb), psnrDb = FiniteOrNull(quality.PsnrLumaDb) },
        ssimY = quality.GlobalSsimLuma,
        alpha = new { quality.MeanAbsoluteErrorAlpha, quality.RootMeanSquareErrorAlpha, quality.ChangedPixelCountAlpha }
    };

    private static double? FiniteOrNull(double value) => double.IsFinite(value) ? value : null;

    private static void AppendRow(StringBuilder builder, string recordType, string? strategy, string? channel,
        int? index, int? rank, double? sigma, double? relativeSigma, double? energyShare,
        double? cumulativeEnergy, double? retainedEnergy, double? frobeniusError, double? relativeError,
        double? psnrRgb, bool? psnrExact, double? ssim, int? sweeps, bool? converged,
        double? orthogonality, double? elapsed)
    {
        string?[] values = [recordType, strategy, channel, Format(index), Format(rank), Format(sigma),
            Format(relativeSigma), Format(energyShare), Format(cumulativeEnergy), Format(retainedEnergy),
            Format(frobeniusError), Format(relativeError), Format(psnrRgb), Format(psnrExact), Format(ssim),
            Format(sweeps), Format(converged), Format(orthogonality), Format(elapsed)];
        builder.AppendJoin(',', values.Select(Escape)).Append("\r\n");
    }

    private static string Format<T>(T? value) where T : struct, IFormattable =>
        value is null ? string.Empty : value.Value.ToString(null, CultureInfo.InvariantCulture);
    private static string Format(bool? value) => value is null ? string.Empty : value.Value ? "true" : "false";
    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
