namespace ImageLabPlugin.Domain.Shared.Checksums;

/// <summary>计算 IEEE CRC-32（多项式 0xEDB88320，初值与终值异或均为全 1）。</summary>
/// <remarks>
/// 这是协议中立的纯数值原语。CRC 只能发现常见的意外损坏，不能证明数据来源，也不能抵抗恶意篡改；
/// LSB Frame 与频域水印只共享这一数学算法，绝不共享 Magic、Header 或读取状态。
/// </remarks>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320u;
            }

            table[index] = value;
        }

        return table;
    }
}
