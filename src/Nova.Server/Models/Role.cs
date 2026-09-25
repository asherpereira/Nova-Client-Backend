namespace Nova.Server.Models;

[Flags]
public enum ServerPermission : long
{
    None = 0,
    ViewChannels = 1L << 0,
    SendMessages = 1L << 1,
    ManageMessages = 1L << 2,
    Connect = 1L << 3,
    Speak = 1L << 4,
    Stream = 1L << 5,
    ManageChannels = 1L << 6,
    ManageRoles = 1L << 7,
    ManageServer = 1L << 8,
    KickMembers = 1L << 9,
    BanMembers = 1L << 10,
    CreateInvites = 1L << 11,
    ManageInvites = 1L << 12,
    MentionEveryone = 1L << 13,
    AttachFiles = 1L << 14,
    AddReactions = 1L << 15,
    Administrator = 1L << 62
}

public sealed class Role
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Name { get; set; } = "";
    public long Permissions { get; set; }
    public int Position { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
