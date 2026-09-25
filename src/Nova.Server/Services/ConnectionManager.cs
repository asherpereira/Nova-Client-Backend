using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Nova.Server.Services;

public sealed class ConnectionManager
{
    private sealed class Connection
    {
        public Connection(WebSocket socket) => Socket = socket;

        public WebSocket Socket { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<WebSocket, Connection>> _connections = new();

    public void Add(Guid userId, WebSocket socket)
    {
        var connections = _connections.GetOrAdd(
            userId,
            _ => new ConcurrentDictionary<WebSocket, Connection>());

        connections.TryAdd(socket, new Connection(socket));
    }

    public void Remove(Guid userId, WebSocket socket)
    {
        if (!_connections.TryGetValue(userId, out var connections))
            return;

        if (connections.TryRemove(socket, out var connection))
            connection.SendLock.Dispose();

        if (connections.IsEmpty)
            _connections.TryRemove(userId, out _);
    }

    public async Task BroadcastAsync(
        Guid userId,
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(userId, out var connections))
            return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var dead = new List<WebSocket>();

        foreach (var pair in connections)
        {
            var socket = pair.Key;
            var connection = pair.Value;

            if (socket.State != WebSocketState.Open)
            {
                dead.Add(socket);
                continue;
            }

            try
            {
                await connection.SendLock.WaitAsync(cancellationToken);

                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        await socket.SendAsync(
                            bytes,
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            cancellationToken);
                    }
                }
                finally
                {
                    connection.SendLock.Release();
                }
            }
            catch (WebSocketException)
            {
                dead.Add(socket);
            }
            catch (ObjectDisposedException)
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

    public async Task BroadcastToUsersAsync(
        IEnumerable<Guid> userIds,
        object payload,
        CancellationToken cancellationToken = default)
    {
        foreach (var userId in userIds.Distinct())
            await BroadcastAsync(userId, payload, cancellationToken);
    }

    public int GetConnectionCount(Guid userId)
        => _connections.TryGetValue(userId, out var sockets) ? sockets.Count : 0;
}
