using System.Buffers.Binary;
using System.Text.Json;

namespace ImageLabPlugin.Infrastructure.Workflow;

/// <summary>
/// 文件动作专用的基础校验，不扩散到纯效果领域。读取预算根据实际流计算；路径按每级父目录检查，
/// 防止仅检查操作目录却遗漏其祖先 junction。检查不宣称能消除同权限进程的文件系统竞态。
/// </summary>
internal static class WorkflowFileValidation
{
    internal static void RejectReparseAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Workflow 文件路径不能包含重解析点。");
            current = Path.GetDirectoryName(current);
        }
    }

    internal static async Task<byte[]> ReadBoundedAsync(string path, int maximum, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximum) throw new InvalidDataException("Workflow 文件超过读取预算。");
        using var result = new MemoryStream();
        var buffer = new byte[Math.Min(65536, maximum + 1)];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0,
                (int)Math.Min(buffer.Length, maximum - result.Length + 1)), token).ConfigureAwait(false);
            if (read == 0) break;
            if (result.Length + read > maximum) throw new InvalidDataException("Workflow 文件超过实际读取预算。");
            result.Write(buffer, 0, read);
        }
        token.ThrowIfCancellationRequested();
        return result.ToArray();
    }

    internal static void ValidatePngHeader(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13 || !bytes[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Artifact 缺少有效 PNG/IHDR 头部。");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
            throw new InvalidDataException("Workflow PNG 单边尺寸必须为 1–4096，已在像素解码前拒绝。");
    }

    internal static void RequireProperties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("参数必须是对象。");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!found.Add(property.Name) || !names.Contains(property.Name, StringComparer.Ordinal))
                throw new InvalidDataException("参数包含未知或重复字段。");
        if (found.Count != names.Length) throw new InvalidDataException("参数缺少必填字段。");
    }
}
