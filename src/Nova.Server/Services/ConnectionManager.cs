using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Nova.Server.Services;

public sealed class ConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<WebSocket, byte>> _connections = new();

    public void Add(Guid userId, WebSocket socket)
    {
        var sockets = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<WebSocket, byte>());
        sockets.TryAdd(socket, 0);
    }

    public void Remove(Guid userId, WebSocket socket)
    {
        if (_connections.TryGetValue(userId, out var sockets))
        {
            sockets.TryRemove(socket, out _);

            if (sockets.IsEmpty)
                _connections.TryRemove(userId, out _);
        }
    }
}
