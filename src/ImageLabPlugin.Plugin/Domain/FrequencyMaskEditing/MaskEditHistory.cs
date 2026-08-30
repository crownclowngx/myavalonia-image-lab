namespace ImageLabPlugin.Domain.FrequencyMaskEditing;

/// <summary>保存有界操作描述和撤销游标，不保存 2048² 的逐步遮罩副本。</summary>
internal sealed class MaskEditHistory
{
    private readonly List<FrequencyMaskOperation> _operations = [];
    private int _cursor;

    public bool CanUndo => _cursor > 0;
    public bool CanRedo => _cursor < _operations.Count;
    public int Count => _cursor;
    public int TotalStoredCount => _operations.Count;

    public void Add(FrequencyMaskOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_cursor >= FrequencyMaskRecipe.MaximumOperations)
            throw new InvalidOperationException($"操作历史已达到 {FrequencyMaskRecipe.MaximumOperations} 条上限；旧工作未被丢弃。");
        var points = _operations.Take(_cursor).Sum(static item => item.PointCount) + operation.PointCount;
        if (points > FrequencyMaskRecipe.MaximumTotalPoints)
            throw new InvalidOperationException($"画笔采样点将超过 {FrequencyMaskRecipe.MaximumTotalPoints} 上限；本次操作未提交。");
        if (_cursor < _operations.Count) _operations.RemoveRange(_cursor, _operations.Count - _cursor);
        _operations.Add(operation);
        _cursor++;
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        _cursor--;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        _cursor++;
        return true;
    }

    public void Replace(IEnumerable<FrequencyMaskOperation> operations, int? cursor = null)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var materialized = operations.ToArray();
        _ = new FrequencyMaskRecipe(1d, materialized);
        var targetCursor = cursor ?? materialized.Length;
        if (targetCursor < 0 || targetCursor > materialized.Length) throw new ArgumentOutOfRangeException(nameof(cursor));
        _operations.Clear();
        _operations.AddRange(materialized);
        _cursor = targetCursor;
    }

    public FrequencyMaskRecipe CreateRecipe(double strength, int? paddedWidth = null, int? paddedHeight = null) =>
        new(strength, _operations.Take(_cursor), paddedWidth, paddedHeight);
}
