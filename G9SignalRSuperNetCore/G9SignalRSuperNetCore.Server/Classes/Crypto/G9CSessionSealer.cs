using System.Collections.Concurrent;

namespace G9SignalRSuperNetCore.Server.Classes.Crypto;

/// <summary>
///     Per-connection session-key cache that lets a hub method seal and open ciphertext without
///     carrying the ephemeral public key around on every call. Each connection performs the
///     handshake once (typically via a <c>BeginSession</c> hub method) and then reuses the
///     session key for the lifetime of the connection.
/// </summary>
/// <remarks>
///     <para>The cache is process-local and AOT-safe (only <see cref="ConcurrentDictionary{TKey,TValue}"/>
///     and <c>byte[]</c>). Keys are zeroed when the connection's session is removed.</para>
///     <para>Zero-cost when not used: the singleton has no state until a hub method actively
///     stores a session key.</para>
/// </remarks>
public sealed class G9CSessionSealer
{
    private readonly ConcurrentDictionary<string, byte[]> _sessions = new(StringComparer.Ordinal);

    /// <summary>Stores or replaces the session key for <paramref name="connectionId"/>.</summary>
    public void SetSession(string connectionId, byte[] sessionKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        if (sessionKey is null || sessionKey.Length != G9CHandshake.SessionKeySize)
            throw new ArgumentException($"Session key must be {G9CHandshake.SessionKeySize} bytes.", nameof(sessionKey));

        // Zero the previous key (if any) before overwriting.
        if (_sessions.TryGetValue(connectionId, out var existing))
            Array.Clear(existing);
        _sessions[connectionId] = sessionKey;
    }

    /// <summary>Removes the session for <paramref name="connectionId"/> and zeroes its key bytes.</summary>
    public void DropSession(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) return;
        if (_sessions.TryRemove(connectionId, out var key)) Array.Clear(key);
    }

    /// <summary>Returns the cached session key for <paramref name="connectionId"/> or null.</summary>
    public byte[]? TryGet(string connectionId)
        => string.IsNullOrEmpty(connectionId) ? null
           : _sessions.TryGetValue(connectionId, out var k) ? k : null;

    /// <summary>Seals <paramref name="plaintext"/> using the cached session key.</summary>
    public byte[] Seal(string connectionId, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        var key = TryGet(connectionId)
                  ?? throw new InvalidOperationException(
                      "No session key cached for this connection. Call BeginSession first.");
        return G9CHandshake.Seal(key, plaintext, associatedData);
    }

    /// <summary>Opens <paramref name="envelope"/> using the cached session key.</summary>
    public byte[] Open(string connectionId, ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> associatedData = default)
    {
        var key = TryGet(connectionId)
                  ?? throw new InvalidOperationException(
                      "No session key cached for this connection. Call BeginSession first.");
        return G9CHandshake.Open(key, envelope, associatedData);
    }
}
