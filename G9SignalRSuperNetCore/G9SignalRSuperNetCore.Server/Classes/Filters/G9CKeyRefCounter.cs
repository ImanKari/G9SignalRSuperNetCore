using System.Collections.Concurrent;
using System.Threading;

namespace G9SignalRSuperNetCore.Server.Classes.Filters;

/// <summary>
///     Lock-free reference counter per key (2.9): how many live connections share a user- or IP-scoped rate-limit
///     bucket, so the bucket can be freed exactly when the last of them disconnects.
/// </summary>
/// <remarks>
///     A cell that reaches zero is marked dead (<c>-1</c>) before it is removed, and <see cref="Acquire"/> never
///     increments a dead cell: it helps remove it and starts a fresh one. That closes the race in which a connect
///     increments a cell that a concurrent last disconnect is about to delete.
/// </remarks>
internal sealed class G9CKeyRefCounter
{
    private readonly ConcurrentDictionary<string, Cell> _cells = new(StringComparer.Ordinal);

    /// <summary>Number of keys with at least one live reference.</summary>
    public int Count => _cells.Count;

    /// <summary>Adds one reference to <paramref name="key"/>.</summary>
    public void Acquire(string key)
    {
        while (true)
        {
            var cell = _cells.GetOrAdd(key, static _ => new Cell());
            var current = Volatile.Read(ref cell.Value);
            if (current < 0)
            {
                // Dead: help the remover, then retry with a fresh cell.
                _cells.TryRemove(new KeyValuePair<string, Cell>(key, cell));
                continue;
            }

            if (Interlocked.CompareExchange(ref cell.Value, current + 1, current) == current) return;
        }
    }

    /// <summary>Removes one reference from <paramref name="key"/>; returns <c>true</c> when it was the last one.</summary>
    public bool Release(string key)
    {
        if (!_cells.TryGetValue(key, out var cell)) return false;
        while (true)
        {
            var current = Volatile.Read(ref cell.Value);
            if (current <= 0) return false; // already dead, or unbalanced
            if (current == 1)
            {
                if (Interlocked.CompareExchange(ref cell.Value, -1, 1) != 1) continue;
                _cells.TryRemove(new KeyValuePair<string, Cell>(key, cell));
                return true;
            }

            if (Interlocked.CompareExchange(ref cell.Value, current - 1, current) == current) return false;
        }
    }

    private sealed class Cell
    {
        public int Value;
    }
}
