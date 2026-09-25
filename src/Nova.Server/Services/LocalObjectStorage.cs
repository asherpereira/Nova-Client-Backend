namespace Nova.Server.Services;

public sealed class LocalObjectStorage(IConfiguration configuration) : IObjectStorage
{
    private readonly string _root = Path.GetFullPath(
        configuration["Storage:LocalPath"] ?? "/data/nova/uploads");

    public async Task CreateAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<long> AppendAsync(string key, long offset, Stream source, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 1024 * 1024, useAsync: true);
        stream.Position = offset;
        await source.CopyToAsync(stream, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return stream.Position;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(key))
            throw new ArgumentException("Invalid storage key.", nameof(key));

        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        return path;
    }
}