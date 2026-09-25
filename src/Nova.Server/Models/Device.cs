namespace Nova.Server.Models;

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Platform { get; set; } = "";
    public string? PushToken { get; set; }
    public string? IdentityPublicKey { get; set; }
    public string? SignedPreKey { get; set; }
    public string? PreKeySignature { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public User User { get; set; } = null!;
}
