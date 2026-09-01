using CommunityToolkit.Mvvm.ComponentModel;
using ImageLabPlugin.Domain.HybridImage;

namespace ImageLabPlugin.Features.HybridImage;

/// <summary>Presentation 层可编辑的一整对控制点；半对草稿不会进入领域模型。</summary>
internal sealed partial class HybridAlignmentPointRow : ObservableObject
{
    public HybridAlignmentPointRow(int id, double ax, double ay, double bx, double by)
    {
        Id = id; _pointAX = ax; _pointAY = ay; _pointBX = bx; _pointBY = by;
    }

    public int Id { get; }
    [ObservableProperty] private double _pointAX;
    [ObservableProperty] private double _pointAY;
    [ObservableProperty] private double _pointBX;
    [ObservableProperty] private double _pointBY;

    public HybridAlignmentPointPair ToDomain() => new(Id,
        new HybridNormalizedPoint(PointAX, PointAY), new HybridNormalizedPoint(PointBX, PointBY));

    public void Swap()
    {
        (PointAX, PointBX) = (PointBX, PointAX);
        (PointAY, PointBY) = (PointBY, PointAY);
    }
}
