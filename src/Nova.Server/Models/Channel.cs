namespace Nova.Server.Models;

public enum ChannelType
{
    Text = 0,
    Voice = 1,
    Announcement = 2
}

public sealed class Channel
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid? ParentChannelId { get; set; }
    public string Name { get; set; } = "";
    public string? Topic { get; set; }
    public ChannelType Type { get; set; }
    public int Position { get; set; }
    public bool IsPrivate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public NovaServer Server { get; set; } = null!;
}
