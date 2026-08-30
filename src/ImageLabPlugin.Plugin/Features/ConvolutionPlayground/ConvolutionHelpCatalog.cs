namespace ImageLabPlugin.Features.ConvolutionPlayground;

/// <summary>卷积实验固定帮助文本；内容不参与快照，也不驱动领域分支。</summary>
internal static class ConvolutionHelpCatalog
{
    public const string Summary =
        "卷积与相关：本实验把矩阵项 (kx,ky) 乘以 f(x-kx,y-ky)，非对称核方向可由冲激图验证。\n" +
        "边界：Constant 使用显式常量；Replicate 复制边缘；Reflect-101 镜像但不重复边缘；Wrap 周期环绕。\n" +
        "归一化：None 保留原增益；KernelSum/AbsoluteSum/Explicit 的除数近零时会阻断，不会偷偷改成 1。\n" +
        "负响应与裁切：偏置可帮助显示正负响应，但不属于核；最终统一 AwayFromZero 舍入并裁切至 0..255。\n" +
        "频率响应：DC 位于中心，只描述归一化后的线性核，不包含边界、偏置、裁切和 Magnitude 非线性。\n" +
        "代理与完整尺寸：代理用于快速观察；修改任何配方都会让旧完整结果过期，必须重新执行后才能导出。";
}
