namespace ChiliMusic;

public sealed class PlaybackOrder
{
    private readonly List<int> _remaining = [];
    private readonly List<int> _history = [];
    private readonly List<int> _priority = [];
    public IReadOnlyList<int> Priority => _priority;
    public void QueueNext(IEnumerable<int> indices) { var pending = indices.Concat(_priority).Where(i => i >= 0 && i < _count).Distinct().ToArray(); _priority.Clear(); _priority.AddRange(pending); }
    private int _cursor = -1, _count;
    public IReadOnlyList<int> History => _history;
    public IReadOnlyList<int> Remaining => _remaining;
    public int Cursor => _cursor;
    public void Remap(Func<int, int> map, int count, IEnumerable<int>? added = null)
    {
        _count = count;
        for (int i = _history.Count - 1; i >= 0; i--) { int next = map(_history[i]); if (next < 0 || next >= count) { _history.RemoveAt(i); if (i <= _cursor) _cursor--; } else _history[i] = next; }
        _cursor = Math.Clamp(_cursor, -1, _history.Count - 1);
        var remaining = _remaining.Select(map).Where(i => i >= 0 && i < count).Distinct().ToList(); _remaining.Clear(); _remaining.AddRange(remaining);
        var priority = _priority.Select(map).Where(i => i >= 0 && i < count).Distinct().ToList(); _priority.Clear(); _priority.AddRange(priority);
        foreach (int index in added ?? []) if (!_remaining.Contains(index)) _remaining.Insert(Random.Shared.Next(_remaining.Count + 1), index);
    }
    public void Reset(int count) { _count = count; _priority.Clear(); _remaining.Clear(); var round = Enumerable.Range(0, count).ToArray(); Random.Shared.Shuffle(round); _remaining.AddRange(round); _history.Clear(); _cursor = -1; }
    public void Restore(int count, QueueState state)
    {
        Reset(count); _priority.AddRange(state.PlayNext.Where(i => i >= 0 && i < count).Distinct()); _history.AddRange(state.History.Where(i => i >= 0 && i < count)); _cursor = Math.Clamp(state.HistoryPosition, -1, _history.Count - 1); if (_history.Count > 0) _remaining.Clear(); if (state.ShuffleRemaining.Count > 0) { _remaining.Clear(); _remaining.AddRange(state.ShuffleRemaining.Where(i => i >= 0 && i < count).Distinct()); }
    }
    public void Record(int index)
    {
        _priority.Remove(index);
        if (_cursor >= 0 && _history[_cursor] == index) return;
        if (_cursor < _history.Count - 1) _history.RemoveRange(_cursor + 1, _history.Count - _cursor - 1);
        _history.Add(index); if (_history.Count > 500) _history.RemoveAt(0); _cursor = _history.Count - 1; _remaining.Remove(index);
    }
    public int Previous() { if (_cursor > 0) return _history[--_cursor]; return _cursor >= 0 ? _history[_cursor] : -1; }
    public int PeekNext(int current, PlayMode mode, bool automatic)
    {
        if (_count == 0) return -1;
        if (_priority.Count > 0) return _priority[0];
        if (automatic && mode == PlayMode.RepeatOne) return current;
        if (_cursor < _history.Count - 1) return _history[_cursor + 1];
        if (mode == PlayMode.Shuffle)
        {
            if (_remaining.Count == 0) { var round = Enumerable.Range(0, _count).ToArray(); Random.Shared.Shuffle(round); _remaining.AddRange(round); if (_remaining.Count > 1 && _remaining[0] == current) (_remaining[0], _remaining[1]) = (_remaining[1], _remaining[0]); }
            return _remaining[0];
        }
        int next = current + 1; return next < _count ? next : mode == PlayMode.RepeatAll ? 0 : -1;
    }
    public int Next(int current, PlayMode mode, bool automatic)
    {
        if (_priority.Count == 0 && _cursor < _history.Count - 1 && !(automatic && mode == PlayMode.RepeatOne)) return _history[++_cursor];
        int next = PeekNext(current, mode, automatic); if (next >= 0) Record(next); return next;
    }
    public IReadOnlyList<int> Upcoming(int current, PlayMode mode, int maximum)
    {
        if (maximum <= 0 || PeekNext(current, mode, true) < 0) return [];
        var preview = new PlaybackOrder(); preview.Restore(_count, new QueueState { History = _history.ToList(), HistoryPosition = _cursor, ShuffleRemaining = _remaining.ToList(), PlayNext = _priority.ToList() });
        var result = new List<int>();
        for (int i = 0; i < Math.Min(maximum, _count); i++)
        {
            if (mode == PlayMode.Shuffle && preview._priority.Count == 0 && preview._cursor >= preview._history.Count - 1 && preview._remaining.Count == 0) break;
            int next = preview.Next(current, mode, true); if (next < 0) break; result.Add(next); current = next;
            if (mode == PlayMode.RepeatOne && preview.Priority.Count == 0) break;
        }
        return result;
    }
}
