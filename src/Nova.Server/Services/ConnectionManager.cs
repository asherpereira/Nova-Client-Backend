using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

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

    public async Task BroadcastAsync(Guid userId, object payload, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(userId, out var sockets))
            return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var dead = new List<WebSocket>();

        foreach (var socket in sockets.Keys)
        {
            if (socket.State != WebSocketState.Open)
            {
                dead.Add(socket);
                continue;
            }

            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch (WebSocketException)
            {
                dead.Add(socket);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        foreach (var socket in dead)
            Remove(userId, socket);
    }

    public async Task BroadcastToUsersAsync(IEnumerable<Guid> userIds, object payload, CancellationToken cancellationToken = default)
    {
        foreach (var userId in userIds.Distinct())
            await BroadcastAsync(userId, payload, cancellationToken);
    }

    public int GetConnectionCount(Guid userId)
        => _connections.TryGetValue(userId, out var sockets) ? sockets.Count : 0;
}
