using System.Security.Cryptography;
using ImageLabPlugin.Application.Ports;

namespace ImageLabPlugin.Infrastructure.Cryptography;

/// <summary>生产随机源只委托平台 CSPRNG；确定性测试必须显式替换该端口。</summary>
internal sealed class CryptographicRandomSource : IRandomSource
{
    public void Fill(Span<byte> destination) => RandomNumberGenerator.Fill(destination);
}
