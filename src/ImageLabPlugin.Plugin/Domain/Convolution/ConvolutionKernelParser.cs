using System.Globalization;
using System.Text.RegularExpressions;

namespace ImageLabPlugin.Domain.Convolution;

internal sealed record KernelParseError(int Row, int Column, string Reason);
internal sealed record KernelParseResult(ConvolutionKernel? Kernel, IReadOnlyList<KernelParseError> Errors)
{
    public bool IsSuccess => Kernel is not null && Errors.Count == 0;
}

/// <summary>把使用不变文化数字协议的矩阵文本转换为不可变核，并保留精确行列错误。</summary>
internal sealed partial class ConvolutionKernelParser
{
    [GeneratedRegex("[\\t ,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnSeparators();

    public KernelParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Failure(1, 1, "矩阵不能为空。");
        var rows = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length is < ConvolutionKernel.MinimumSize or > ConvolutionKernel.MaximumSize || (rows.Length & 1) == 0)
            return Failure(1, 1, "矩阵必须包含 3 至 31 个奇数行。");
        var values = new List<double>(rows.Length * rows.Length);
        var errors = new List<KernelParseError>();
        for (var row = 0; row < rows.Length; row++)
        {
            var columns = ColumnSeparators().Split(rows[row].Trim()).Where(static value => value.Length > 0).ToArray();
            if (columns.Length != rows.Length)
            {
                errors.Add(new KernelParseError(row + 1, Math.Min(columns.Length + 1, rows.Length),
                    $"第 {row + 1} 行应有 {rows.Length} 列，实际为 {columns.Length} 列。"));
                continue;
            }
            for (var column = 0; column < columns.Length; column++)
            {
                if (!double.TryParse(columns[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    errors.Add(new KernelParseError(row + 1, column + 1, $"“{columns[column]}”不是使用点号小数的有限数字。"));
                else if (!double.IsFinite(value) || Math.Abs(value) > ConvolutionKernel.MaximumCoefficientMagnitude)
                    errors.Add(new KernelParseError(row + 1, column + 1, "系数必须有限且绝对值不超过 1024。"));
                else values.Add(value);
            }
        }
        if (errors.Count > 0) return new KernelParseResult(null, errors);
        return new KernelParseResult(new ConvolutionKernel(rows.Length, values.ToArray()), []);
    }

    public string Format(ConvolutionKernel kernel) => string.Join(Environment.NewLine,
        Enumerable.Range(0, kernel.Size).Select(row => string.Join(" ",
            Enumerable.Range(0, kernel.Size).Select(column => kernel[row, column].ToString("0.########", CultureInfo.InvariantCulture)))));

    private static KernelParseResult Failure(int row, int column, string reason) => new(null, [new(row, column, reason)]);
}
