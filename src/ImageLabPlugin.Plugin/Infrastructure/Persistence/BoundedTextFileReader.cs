using ImageLabPlugin.Application.Ports;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>在分配完整缓冲前后执行大小门禁，拒绝超限配方文本。</summary>
internal sealed class BoundedTextFileReader : ITextFileReader
{
    public async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var length = new FileInfo(path).Length;
        if (length > maximumBytes) throw new InvalidDataException($"文件超过 {maximumBytes} 字节上限。");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > maximumBytes) throw new InvalidDataException($"文件读取后超过 {maximumBytes} 字节上限。");
        return bytes;
    }
}
