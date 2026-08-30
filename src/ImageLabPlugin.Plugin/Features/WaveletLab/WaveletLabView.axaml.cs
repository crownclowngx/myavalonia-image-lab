using Avalonia.Controls;

namespace ImageLabPlugin.Features.WaveletLab;

/// <summary>只加载 Wavelet Lab 布局；业务、文件和 Bitmap 生命周期全部由 Document 与窄用例负责。</summary>
public sealed partial class WaveletLabView : UserControl
{
    public WaveletLabView() => InitializeComponent();
}
