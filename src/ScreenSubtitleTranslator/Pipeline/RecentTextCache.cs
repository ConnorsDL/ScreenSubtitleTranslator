namespace ScreenSubtitleTranslator.Pipeline;

public sealed class RecentTextCache
{
    private readonly int _capacity;
    private readonly Queue<(string Key, long SequenceId)> _items = new();
    private readonly Dictionary<string, long> _keys = new(StringComparer.Ordinal);

    public RecentTextCache(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool AddIfNew(string text)
    {
        return AddIfNew(text, sequenceId: 0, out _);
    }

    public bool AddIfNew(string text, long sequenceId, out long originalSequenceId)
    {
        var key = SubtitleTextUtilities.NormalizeForComparison(text);
        if (string.IsNullOrWhiteSpace(key))
        {
            originalSequenceId = 0;
            return false;
        }

        if (_keys.TryGetValue(key, out originalSequenceId))
        {
            return false;
        }

        _keys[key] = sequenceId;
        _items.Enqueue((key, sequenceId));
        while (_items.Count > _capacity)
        {
            var removed = _items.Dequeue();
            _keys.Remove(removed.Key);
        }

        originalSequenceId = 0;
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _keys.Clear();
    }
}
