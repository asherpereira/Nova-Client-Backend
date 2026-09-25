namespace Nova.Server.Models;

public sealed class NovaServer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public Guid OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ServerMember> Members { get; set; } = new List<ServerMember>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Channel> Channels { get; set; } = new List<Channel>();
}
