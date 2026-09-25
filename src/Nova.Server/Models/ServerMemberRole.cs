namespace Nova.Server.Models;

public sealed class ServerMemberRole
{
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }

    public ServerMember ServerMember { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
