using System.ComponentModel.DataAnnotations;

namespace Nova.Server.Models;

public enum FriendRequestStatus { Pending = 0, Accepted = 1, Declined = 2, Cancelled = 3, Blocked = 4 }
public enum NotificationType { DirectMessage = 0, Mention = 1, FriendRequest = 2, ServerInvite = 3, ServerUpdate = 4, System = 5 }
public enum CallType { Voice = 0, Video = 1, ScreenShare = 2 }
public enum CallState { Ringing = 0, Active = 1, Ended = 2 }
public enum ApplicationType { Bot = 0, Integration = 1 }
public enum AuditAction { ServerCreated, MemberJoined, MemberLeft, MemberKicked, MemberBanned, MemberUnbanned, RoleCreated, RoleUpdated, ChannelCreated, ChannelUpdated, ChannelDeleted, MessageDeleted, MessageEdited, InviteCreated, InviteRevoked, SettingsUpdated }

public sealed class UserProfile
{
    [Key] public Guid UserId { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? Theme { get; set; }
    public bool IsDiscoverable { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class FriendRequest
{
    [Key] public Guid Id { get; set; }
    public Guid SenderId { get; set; }
    public Guid RecipientId { get; set; }
    public FriendRequestStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}

public sealed class Friendship
{
    [Key] public Guid Id { get; set; }
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UserBlock
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BlockedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MessageState
{
    [Key] public Guid MessageId { get; set; }
    public string? ReplyToMessageId { get; set; }
    public Guid? ThreadRootMessageId { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? EditCiphertext { get; set; }
}

public sealed class MessageReaction
{
    [Key] public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReadState
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? LastReadMessageId { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
}

public sealed class ServerInvite
{
    [Key] public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Code { get; set; } = "";
    public int MaxUses { get; set; }
    public int Uses { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}

public sealed class ServerBan
{
    [Key] public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid UserId { get; set; }
    public Guid ModeratorId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class ChannelPermissionOverride
{
    [Key] public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid? RoleId { get; set; }
    public Guid? UserId { get; set; }
    public long Allow { get; set; }
    public long Deny { get; set; }
}

public sealed class Attachment
{
    [Key] public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? MessageId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string StorageKey { get; set; } = "";
    public string StorageProvider { get; set; } = "local";
    public string EncryptionVersion { get; set; } = "pending-e2ee";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Presence
{
    [Key] public Guid UserId { get; set; }
    public string Status { get; set; } = "offline";
    public string? CustomStatus { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class Notification
{
    [Key] public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationType Type { get; set; }
    public string Payload { get; set; } = "{}";
    public bool Read { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ModerationReport
{
    [Key] public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid? ServerId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? MessageId { get; set; }
    public string Reason { get; set; } = "";
    public string? Details { get; set; }
    public string Status { get; set; } = "open";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class AuditLogEntry
{
    [Key] public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid ActorId { get; set; }
    public AuditAction Action { get; set; }
    public Guid? TargetId { get; set; }
    public string? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Application
{
    [Key] public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ApplicationType Type { get; set; }
    public string? IconUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ApplicationToken
{
    [Key] public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class Webhook
{
    [Key] public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class DiscoveryListing
{
    [Key] public Guid ServerId { get; set; }
    public string Category { get; set; } = "general";
    public bool Public { get; set; }
    public string? Tags { get; set; }
    public string? ShortDescription { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CallSession
{
    [Key] public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid InitiatorId { get; set; }
    public CallType Type { get; set; }
    public CallState State { get; set; }
    public string? SfuProvider { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class CallParticipant
{
    [Key] public Guid Id { get; set; }
    public Guid CallSessionId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}

public sealed class DeviceKeyBundle
{
    [Key] public Guid DeviceId { get; set; }
    public string IdentityPublicKey { get; set; } = "";
    public string SignedPreKey { get; set; } = "";
    public string PreKeySignature { get; set; } = "";
    public string? OneTimePreKeys { get; set; }
    public int ProtocolVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RealtimeEvent
{
    [Key] public long Sequence { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ServerId { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
