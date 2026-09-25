namespace Nova.Server.Services;

public interface IObjectStorage
{
    Task CreateAsync(string key, CancellationToken cancellationToken = default);
    Task<long> AppendAsync(string key, long offset, Stream source, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}