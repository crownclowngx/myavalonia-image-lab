namespace ImageLabPlugin.Domain.Fingerprinting;

/// <summary>感知指纹算法的稳定身份；身份同时冻结归一化、输入尺寸、阈值比较符号和位序。</summary>
internal readonly record struct FingerprintAlgorithmId
{
    public static readonly FingerprintAlgorithmId AverageHash = new("ahash-8x8-mean64-luma-v1");
    public static readonly FingerprintAlgorithmId DifferenceHash = new("dhash-horizontal-9x8-64-luma-v1");
    public static readonly FingerprintAlgorithmId PerceptualHash = new("phash-dct32-low8-median64-luma-v1");

    public FingerprintAlgorithmId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("算法 ID 不能为空。", nameof(value));
        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
