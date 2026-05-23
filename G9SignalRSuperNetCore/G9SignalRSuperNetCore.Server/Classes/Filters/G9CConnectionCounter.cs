using System.Collections.Concurrent;
using System.Threading;

namespace G9SignalRSuperNetCore.Server.Classes.Filters;

/// <summary>
///     Process-local counter that tracks how many simultaneous connections are open per key
///     (user id or remote IP). Increment is conditional on a configurable cap.
/// </summary>
/// <remarks>
///     All operations are lock-free using <see cref="Interlocked"/> on the per-key counter cell.
///     The cell is removed when its counter falls back to zero so the dictionary doesn't grow
///     unboundedly.
/// </remarks>
internal sealed class G9CConnectionCounter
{
    private readonly ConcurrentDictionary<string, Cell> _cells = new(StringComparer.Ordinal);

    /// <summary>Atomically increments the counter for <paramref name="key"/> if it would not exceed <paramref name="cap"/>.</summary>
    /// <returns>True when the increment succeeded; false when it would have exceeded the cap.</returns>
    public bool TryIncrement(string key, int cap)
    {
        if (cap <= 0) return true;
        var cell = _cells.GetOrAdd(key, static _ => new Cell());
        while (true)
        {
            var current = Volatile.Read(ref cell.Value);
            if (current >= cap) return false;
            if (Interlocked.CompareExchange(ref cell.Value, current + 1, current) == current) return true;
        }
    }

    /// <summary>Decrements the counter for <paramref name="key"/>. Removes the cell when it reaches zero.</summary>
    public void Decrement(string key)
    {
        if (!_cells.TryGetValue(key, out var cell)) return;
        var newValue = Interlocked.Decrement(ref cell.Value);
        if (newValue <= 0)
            _cells.TryRemove(new KeyValuePair<string, Cell>(key, cell));
    }

    /// <summary>Returns the current count for <paramref name="key"/> (0 when absent).</summary>
    public int Read(string key) =>
        _cells.TryGetValue(key, out var cell) ? Volatile.Read(ref cell.Value) : 0;

    private sealed class Cell { public int Value; }
}
