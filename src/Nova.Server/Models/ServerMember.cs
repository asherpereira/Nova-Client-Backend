namespace Nova.Server.Models;

public sealed class ServerMember
{
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public string? Nickname { get; set; }

    public NovaServer Server { get; set; } = null!;
    public User User { get; set; } = null!;
}
