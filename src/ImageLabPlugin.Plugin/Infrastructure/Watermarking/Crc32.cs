namespace ImageLabPlugin.Infrastructure.Watermarking;

/// <summary>计算 IEEE CRC-32，仅用于控制头随机损坏检测，不承担密码学认证。</summary>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xEDB88320u;
            }

            table[i] = value;
        }

        return table;
    }
}
