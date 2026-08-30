namespace ImageLabPlugin.Features.LsbSteganographyLab;

/// <summary>集中保存界面与测试共同约束的教学边界，避免视图散落互相矛盾的安全措辞。</summary>
internal static class LsbSteganographyHelpCatalog
{
    public const string PrimaryNotice = "教学与实验用途；不保证不可检测；不是频域鲁棒水印。";
    public const string SeedNotice = "seed 只用于复现实验位置，不是密码或密钥。";
    public const string StatisticsNotice = "p 值、位分布和邻接只描述当前样本与模型，不是图片含隐写的概率，也不证明安全或不可检测。";
    public const string CrcNotice = "CRC 只检查常见意外损坏，不认证来源，也不能抵抗恶意篡改。";
}
