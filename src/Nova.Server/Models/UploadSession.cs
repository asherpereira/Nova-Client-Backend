namespace Nova.Server.Models;

public sealed class UploadSession
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long TotalSize { get; set; }
    public long ReceivedSize { get; set; }
    public string StorageKey { get; set; } = "";
    public bool Completed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}