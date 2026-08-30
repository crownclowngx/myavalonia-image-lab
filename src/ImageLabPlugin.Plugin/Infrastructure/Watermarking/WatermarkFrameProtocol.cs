using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using ImageLabPlugin.Application.Ports;
using ImageLabPlugin.Domain.Watermarking;
using ImageLabPlugin.Infrastructure.ErrorCorrection;

namespace ImageLabPlugin.Infrastructure.Watermarking;

[Flags]
internal enum FrameFlags : byte
{
    None = 0,
    Compressed = 1,
    Encrypted = 2,
    Signed = 4
}

internal sealed record WatermarkFrameHeader(
    EmbeddingProfileId Profile,
    FrameFlags Flags,
    PayloadContentType ContentType,
    int ProtectedLength,
    int EncodedLength,
    int OriginalLength,
    byte[] Salt,
    byte[] Nonce,
    byte[] MappingSeed,
    byte[] DigestPrefix);

internal sealed record EncodedWatermarkFrame(
    WatermarkFrameHeader Header,
    byte[] EncodedHeader,
    byte[] EncodedData,
    byte[] MappingKey);

internal sealed record DecodedWatermarkFrame(
    WatermarkPayload Payload,
    int CorrectedSymbols,
    byte[] MappingKey,
    IntegrityStatus Integrity);

/// <summary>实现与图片载体无关的 V1 二进制 Frame。</summary>
/// <remarks>
/// 该类只协调压缩、认证加密和 Reed-Solomon，不知道 DCT、QIM 或文件路径。协议采用显式端序和固定 Header，
/// 避免直接序列化 CLR 对象造成运行时、属性顺序或版本漂移。
/// </remarks>
internal sealed class WatermarkFrameProtocol(ReedSolomonCodec errorCorrection, IRandomSource randomSource)
{
    public const int HeaderLength = 80;
    public const int EncodedHeaderLength = HeaderLength + ReedSolomonCodec.ParitySymbolsPerBlock;
    public const int Pbkdf2Iterations = 600_000;
    private const byte ProtocolVersion = 1;
    private static ReadOnlySpan<byte> Magic => "ILW1"u8;

    public EncodedWatermarkFrame Encode(
        WatermarkPayload payload,
        EmbeddingProfileId profile,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var original = payload.Bytes.ToArray();
        var digest = SHA256.HashData(original);
        var compressed = CompressIfSmaller(original);
        var flags = compressed.WasCompressed ? FrameFlags.Compressed : FrameFlags.None;
        var protectedPlaintext = compressed.Data;
        var encrypted = !string.IsNullOrEmpty(password);
        if (encrypted)
        {
            flags |= FrameFlags.Encrypted;
        }

        var salt = new byte[16];
        var nonce = new byte[12];
        var mappingSeed = new byte[16];
        randomSource.Fill(mappingSeed);
        if (encrypted)
        {
            randomSource.Fill(salt);
            randomSource.Fill(nonce);
        }

        var protectedLength = checked(protectedPlaintext.Length + (encrypted ? 16 : 0));
        var encodedLength = errorCorrection.GetEncodedLength(protectedLength);
        var header = new WatermarkFrameHeader(
            profile,
            flags,
            payload.ContentType,
            protectedLength,
            encodedLength,
            original.Length,
            salt,
            nonce,
            mappingSeed,
            digest[..8]);
        var serializedHeader = SerializeHeader(header);

        byte[] protectedData;
        byte[] mappingKey;
        if (encrypted)
        {
            var keys = DeriveKeys(password!, salt);
            try
            {
                mappingKey = CombineMappingKey(keys.MappingKey, mappingSeed);
                var cipherText = new byte[protectedPlaintext.Length];
                var tag = new byte[16];
                using var aes = new AesGcm(keys.EncryptionKey, tag.Length);
                aes.Encrypt(nonce, protectedPlaintext, cipherText, tag, serializedHeader.AsSpan(0, 76));
                protectedData = [.. cipherText, .. tag];
            }
            finally
            {
                keys.Clear();
            }
        }
        else
        {
            protectedData = protectedPlaintext;
            mappingKey = DerivePublicMappingKey(mappingSeed);
        }

        var encodedHeader = errorCorrection.Encode(serializedHeader);
        var encodedData = errorCorrection.Encode(protectedData);
        CryptographicOperations.ZeroMemory(original);
        CryptographicOperations.ZeroMemory(protectedPlaintext);

        return new EncodedWatermarkFrame(header, encodedHeader, encodedData, mappingKey);
    }

    public WatermarkFrameHeader DecodeHeader(ReadOnlySpan<byte> encodedHeader, out int correctedSymbols)
    {
        if (encodedHeader.Length != EncodedHeaderLength)
        {
            throw new InvalidDataException("控制信道长度不符合 V1 Header 规格。");
        }

        var decoded = errorCorrection.Decode(encodedHeader, HeaderLength);
        correctedSymbols = decoded.CorrectedSymbols;
        return ParseHeader(decoded.Data);
    }

    public DecodedWatermarkFrame DecodeData(
        WatermarkFrameHeader header,
        ReadOnlySpan<byte> encodedData,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (encodedData.Length != header.EncodedLength)
        {
            throw new InvalidDataException("数据信道长度与控制头声明不一致。");
        }

        var corrected = errorCorrection.Decode(encodedData, header.ProtectedLength);
        var protectedData = corrected.Data;
        byte[] compressedOrPlain;
        byte[] mappingKey;
        if (header.Flags.HasFlag(FrameFlags.Encrypted))
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new UnauthorizedAccessException("该水印需要密码才能提取。");
            }

            if (protectedData.Length < 16)
            {
                throw new InvalidDataException("加密数据信道缺少 AES-GCM Tag。");
            }

            var keys = DeriveKeys(password, header.Salt);
            try
            {
                mappingKey = CombineMappingKey(keys.MappingKey, header.MappingSeed);
                var cipherLength = protectedData.Length - 16;
                compressedOrPlain = new byte[cipherLength];
                var serializedHeader = SerializeHeader(header);
                using var aes = new AesGcm(keys.EncryptionKey, 16);
                aes.Decrypt(
                    header.Nonce,
                    protectedData.AsSpan(0, cipherLength),
                    protectedData.AsSpan(cipherLength, 16),
                    compressedOrPlain,
                    serializedHeader.AsSpan(0, 76));
            }
            finally
            {
                keys.Clear();
                CryptographicOperations.ZeroMemory(protectedData);
            }
        }
        else
        {
            mappingKey = DerivePublicMappingKey(header.MappingSeed);
            compressedOrPlain = protectedData;
        }

        byte[] original;
        try
        {
            original = header.Flags.HasFlag(FrameFlags.Compressed)
                ? DecompressBounded(compressedOrPlain, header.OriginalLength)
                : compressedOrPlain.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(compressedOrPlain);
        }

        if (original.Length != header.OriginalLength)
        {
            CryptographicOperations.ZeroMemory(original);
            throw new InvalidDataException("恢复后的 Payload 长度与控制头声明不一致。");
        }

        var digest = SHA256.HashData(original);
        if (!CryptographicOperations.FixedTimeEquals(digest.AsSpan(0, 8), header.DigestPrefix))
        {
            CryptographicOperations.ZeroMemory(original);
            throw new InvalidDataException("Payload 摘要校验失败，图片数据已超过可恢复边界。");
        }

        return new DecodedWatermarkFrame(
            new WatermarkPayload(original, header.ContentType),
            corrected.CorrectedSymbols,
            mappingKey,
            IntegrityStatus.Valid);
    }

    public byte[] ResolveMappingKey(WatermarkFrameHeader header, string? password)
    {
        if (!header.Flags.HasFlag(FrameFlags.Encrypted))
        {
            return DerivePublicMappingKey(header.MappingSeed);
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new UnauthorizedAccessException("该水印需要密码才能定位数据信道。");
        }

        var keys = DeriveKeys(password, header.Salt);
        try
        {
            return CombineMappingKey(keys.MappingKey, header.MappingSeed);
        }
        finally
        {
            keys.Clear();
        }
    }

    private static byte[] SerializeHeader(WatermarkFrameHeader header)
    {
        ValidateHeaderFields(header);
        var bytes = new byte[HeaderLength];
        Magic.CopyTo(bytes);
        bytes[4] = ProtocolVersion;
        bytes[5] = HeaderLength;
        bytes[6] = (byte)header.Profile;
        bytes[7] = (byte)header.Flags;
        bytes[8] = (byte)header.ContentType;
        bytes[9] = header.Flags.HasFlag(FrameFlags.Encrypted) ? (byte)1 : (byte)0; // KDF 1 = PBKDF2-SHA256
        bytes[10] = ReedSolomonCodec.ParitySymbolsPerBlock;
        bytes[11] = checked((byte)EmbeddingProfile.Resolve(header.Profile).DataRedundancy);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), header.ProtectedLength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), header.EncodedLength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20, 4), header.OriginalLength);
        header.Salt.CopyTo(bytes, 24);
        header.Nonce.CopyTo(bytes, 40);
        header.MappingSeed.CopyTo(bytes, 52);
        header.DigestPrefix.CopyTo(bytes, 68);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(76, 4), Crc32.Compute(bytes.AsSpan(0, 76)));
        return bytes;
    }

    private static WatermarkFrameHeader ParseHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != HeaderLength || !bytes[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("未找到 ImageLab V1 Watermark Magic。");
        }

        if (bytes[4] != ProtocolVersion || bytes[5] != HeaderLength)
        {
            throw new NotSupportedException("图片包含 ImageLab 水印，但协议版本或 Header 长度不受支持。");
        }

        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(bytes[76..80]);
        if (expectedCrc != Crc32.Compute(bytes[..76]))
        {
            throw new InvalidDataException("控制头 CRC 校验失败。");
        }

        var flags = (FrameFlags)bytes[7];
        if ((flags & ~(FrameFlags.Compressed | FrameFlags.Encrypted | FrameFlags.Signed)) != 0 ||
            flags.HasFlag(FrameFlags.Signed))
        {
            throw new NotSupportedException("控制头包含 V1 尚未支持的 Flag。");
        }

        var profile = (EmbeddingProfileId)bytes[6];
        _ = EmbeddingProfile.Resolve(profile);
        var contentType = (PayloadContentType)bytes[8];
        if (!Enum.IsDefined(contentType))
        {
            throw new InvalidDataException("控制头包含未知 Payload 类型。");
        }

        var protectedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..16]);
        var encodedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..20]);
        var originalLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[20..24]);
        var header = new WatermarkFrameHeader(
            profile,
            flags,
            contentType,
            protectedLength,
            encodedLength,
            originalLength,
            bytes[24..40].ToArray(),
            bytes[40..52].ToArray(),
            bytes[52..68].ToArray(),
            bytes[68..76].ToArray());
        ValidateHeaderFields(header);
        if (encodedLength != new ReedSolomonCodec().GetEncodedLength(protectedLength))
        {
            throw new InvalidDataException("控制头中的 Reed-Solomon 长度关系无效。");
        }

        return header;
    }

    private static void ValidateHeaderFields(WatermarkFrameHeader header)
    {
        _ = EmbeddingProfile.Resolve(header.Profile);
        if (header.ProtectedLength < 0 || header.ProtectedLength > WatermarkPayload.MaximumPayloadBytes + 16 ||
            header.EncodedLength < 0 || header.EncodedLength > 20 * 1024 * 1024 ||
            header.OriginalLength < 0 || header.OriginalLength > WatermarkPayload.MaximumPayloadBytes)
        {
            throw new InvalidDataException("控制头声明的长度超出 V1 安全上限。");
        }

        if (header.Salt.Length != 16 || header.Nonce.Length != 12 ||
            header.MappingSeed.Length != 16 || header.DigestPrefix.Length != 8)
        {
            throw new InvalidDataException("控制头固定字段长度无效。");
        }
    }

    private static (byte[] Data, bool WasCompressed) CompressIfSmaller(byte[] original)
    {
        if (original.Length < 64)
        {
            return (original.ToArray(), false);
        }

        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressor.Write(original);
        }

        var compressed = output.ToArray();
        return compressed.Length + 8 < original.Length ? (compressed, true) : (original.ToArray(), false);
    }

    private static byte[] DecompressBounded(byte[] compressed, int expectedLength)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        var output = new byte[expectedLength];
        var offset = 0;
        while (offset < output.Length)
        {
            var read = decompressor.Read(output, offset, output.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset != output.Length || decompressor.ReadByte() != -1)
        {
            CryptographicOperations.ZeroMemory(output);
            throw new InvalidDataException("Brotli 解压长度与控制头声明不一致。");
        }

        return output;
    }

    private static DerivedKeys DeriveKeys(string password, ReadOnlySpan<byte> salt)
    {
        if (password.Length > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(password), "密码长度超过 V1 的 1024 字符安全上限。");
        }

        var master = new byte[64];
        Rfc2898DeriveBytes.Pbkdf2(password, salt, master, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        try
        {
            using var hmac = new HMACSHA256(master);
            var encryption = hmac.ComputeHash("ImageLab/Watermark/V1/Encryption"u8.ToArray());
            var mapping = hmac.ComputeHash("ImageLab/Watermark/V1/Mapping"u8.ToArray());
            return new DerivedKeys(encryption, mapping);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }

    private static byte[] DerivePublicMappingKey(ReadOnlySpan<byte> mappingSeed)
    {
        var input = new byte[mappingSeed.Length + 31];
        mappingSeed.CopyTo(input);
        "ImageLab/Watermark/V1/PublicMap"u8.CopyTo(input.AsSpan(mappingSeed.Length));
        return SHA256.HashData(input);
    }

    private static byte[] CombineMappingKey(ReadOnlySpan<byte> key, ReadOnlySpan<byte> mappingSeed)
    {
        using var hmac = new HMACSHA256(key.ToArray());
        return hmac.ComputeHash(mappingSeed.ToArray());
    }

    private sealed record DerivedKeys(byte[] EncryptionKey, byte[] MappingKey)
    {
        public void Clear()
        {
            CryptographicOperations.ZeroMemory(EncryptionKey);
            CryptographicOperations.ZeroMemory(MappingKey);
        }
    }
}
