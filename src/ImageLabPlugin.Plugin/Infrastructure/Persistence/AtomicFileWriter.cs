using ImageLabPlugin.Application.Ports;

namespace ImageLabPlugin.Infrastructure.Persistence;

/// <summary>通过同目录临时文件发布结果，避免失败时留下半张图片或半份 Payload。</summary>
internal sealed class AtomicFileWriter : IAtomicFileWriter
{
    public async Task WriteAsync(
        string targetPath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new InvalidOperationException("目标文件没有可用目录。");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullTargetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
