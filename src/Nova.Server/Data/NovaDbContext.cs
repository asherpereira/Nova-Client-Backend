using Microsoft.EntityFrameworkCore;
using Nova.Server.Models;

namespace Nova.Server.Data;

public sealed class NovaDbContext(DbContextOptions<NovaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<EncryptedMessage> Messages => Set<EncryptedMessage>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<NovaServer> Servers => Set<NovaServer>();
    public DbSet<ServerMember> ServerMembers => Set<ServerMember>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<ServerMemberRole> ServerMemberRoles => Set<ServerMemberRole>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<MessageState> MessageStates => Set<MessageState>();
    public DbSet<MessageReaction> MessageReactions => Set<MessageReaction>();
    public DbSet<ReadState> ReadStates => Set<ReadState>();
    public DbSet<ServerInvite> ServerInvites => Set<ServerInvite>();
    public DbSet<ServerBan> ServerBans => Set<ServerBan>();
    public DbSet<ChannelPermissionOverride> ChannelPermissionOverrides => Set<ChannelPermissionOverride>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<Presence> Presences => Set<Presence>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ModerationReport> ModerationReports => Set<ModerationReport>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationToken> ApplicationTokens => Set<ApplicationToken>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<DiscoveryListing> DiscoveryListings => Set<DiscoveryListing>();
    public DbSet<CallSession> CallSessions => Set<CallSession>();
    public DbSet<CallParticipant> CallParticipants => Set<CallParticipant>();
    public DbSet<DeviceKeyBundle> DeviceKeyBundles => Set<DeviceKeyBundle>();
    public DbSet<RealtimeEvent> RealtimeEvents => Set<RealtimeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.Username).IsUnique(); e.Property(x=>x.Username).HasMaxLength(32).IsRequired(); e.Property(x=>x.DisplayName).HasMaxLength(64).IsRequired(); e.Property(x=>x.PasswordHash).IsRequired(); });
        modelBuilder.Entity<EncryptedMessage>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ConversationId,x.CreatedAt}); e.HasIndex(x=>new{x.SenderId,x.ClientMessageId}).IsUnique(); e.Property(x=>x.Ciphertext).IsRequired(); e.Property(x=>x.Nonce).HasMaxLength(256).IsRequired(); });
        modelBuilder.Entity<Conversation>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.UpdatedAt); e.Property(x=>x.Name).HasMaxLength(100); e.Property(x=>x.IconUrl).HasMaxLength(2048); });
        modelBuilder.Entity<ConversationMember>(e => { e.HasKey(x=>new{x.ConversationId,x.UserId}); e.HasIndex(x=>new{x.UserId,x.ConversationId}); });
        modelBuilder.Entity<NovaServer>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.OwnerId); e.Property(x=>x.Name).HasMaxLength(100).IsRequired(); e.Property(x=>x.Description).HasMaxLength(1000); e.Property(x=>x.IconUrl).HasMaxLength(2048); });
        modelBuilder.Entity<ServerMember>(e => { e.HasKey(x=>new{x.ServerId,x.UserId}); e.HasIndex(x=>x.UserId); });
        modelBuilder.Entity<Role>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ServerId,x.Position}); e.Property(x=>x.Name).HasMaxLength(100).IsRequired(); });
        modelBuilder.Entity<Channel>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ServerId,x.Position}); e.Property(x=>x.Name).HasMaxLength(100).IsRequired(); e.Property(x=>x.Topic).HasMaxLength(1024); });
        modelBuilder.Entity<ServerMemberRole>(e => e.HasKey(x=>new{x.ServerId,x.UserId,x.RoleId}));
        modelBuilder.Entity<Device>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.UserId,x.RevokedAt}); e.Property(x=>x.Name).HasMaxLength(100).IsRequired(); e.Property(x=>x.Platform).HasMaxLength(32).IsRequired(); });
        modelBuilder.Entity<RefreshSession>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.TokenHash).IsUnique(); e.HasIndex(x=>new{x.UserId,x.RevokedAt,x.ExpiresAt}); });
        modelBuilder.Entity<UserProfile>(e => { e.HasKey(x=>x.UserId); e.Property(x=>x.Bio).HasMaxLength(2000); e.Property(x=>x.AvatarUrl).HasMaxLength(2048); e.Property(x=>x.BannerUrl).HasMaxLength(2048); });
        modelBuilder.Entity<FriendRequest>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.SenderId,x.RecipientId,x.Status}); });
        modelBuilder.Entity<Friendship>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.UserAId,x.UserBId}).IsUnique(); });
        modelBuilder.Entity<UserBlock>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.UserId,x.BlockedUserId}).IsUnique(); });
        modelBuilder.Entity<MessageState>(e => e.HasKey(x=>x.MessageId));
        modelBuilder.Entity<MessageReaction>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.MessageId,x.UserId,x.Emoji}).IsUnique(); e.Property(x=>x.Emoji).HasMaxLength(64).IsRequired(); });
        modelBuilder.Entity<ReadState>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.UserId,x.ConversationId}).IsUnique(); });
        modelBuilder.Entity<ServerInvite>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.Code).IsUnique(); e.Property(x=>x.Code).HasMaxLength(64).IsRequired(); });
        modelBuilder.Entity<ServerBan>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ServerId,x.UserId}).IsUnique(); });
        modelBuilder.Entity<ChannelPermissionOverride>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.ChannelId); });
        modelBuilder.Entity<Attachment>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.OwnerId,x.CreatedAt}); e.Property(x=>x.FileName).HasMaxLength(512).IsRequired(); e.Property(x=>x.ContentType).HasMaxLength(256).IsRequired(); e.Property(x=>x.StorageKey).HasMaxLength(2048).IsRequired(); });
        modelBuilder.Entity<UploadSession>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.OwnerId,x.CreatedAt}); e.Property(x=>x.FileName).HasMaxLength(512).IsRequired(); e.Property(x=>x.ContentType).HasMaxLength(256).IsRequired(); e.Property(x=>x.StorageKey).HasMaxLength(2048).IsRequired(); });
        modelBuilder.Entity<Presence>(e => { e.HasKey(x=>x.UserId); e.Property(x=>x.Status).HasMaxLength(32).IsRequired(); });
        modelBuilder.Entity<Notification>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.UserId,x.Read,x.CreatedAt}); });
        modelBuilder.Entity<ModerationReport>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.Status,x.CreatedAt}); e.Property(x=>x.Reason).HasMaxLength(512).IsRequired(); });
        modelBuilder.Entity<AuditLogEntry>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ServerId,x.CreatedAt}); });
        modelBuilder.Entity<Application>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.OwnerId); e.Property(x=>x.Name).HasMaxLength(100).IsRequired(); });
        modelBuilder.Entity<ApplicationToken>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>x.TokenHash).IsUnique(); });
        modelBuilder.Entity<Webhook>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ServerId,x.ChannelId}); e.Property(x=>x.Name).HasMaxLength(100).IsRequired(); });
        modelBuilder.Entity<DiscoveryListing>(e => { e.HasKey(x=>x.ServerId); e.HasIndex(x=>new{x.Public,x.Category}); e.Property(x=>x.Category).HasMaxLength(64).IsRequired(); });
        modelBuilder.Entity<CallSession>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.ConversationId,x.StartedAt}); });
        modelBuilder.Entity<CallParticipant>(e => { e.HasKey(x=>x.Id); e.HasIndex(x=>new{x.CallSessionId,x.UserId}); });
        modelBuilder.Entity<DeviceKeyBundle>(e => { e.HasKey(x=>x.DeviceId); e.Property(x=>x.IdentityPublicKey).HasMaxLength(8192).IsRequired(); e.Property(x=>x.SignedPreKey).HasMaxLength(8192).IsRequired(); e.Property(x=>x.PreKeySignature).HasMaxLength(8192).IsRequired(); });
        modelBuilder.Entity<RealtimeEvent>(e => { e.HasKey(x=>x.Sequence); e.Property(x=>x.Type).HasMaxLength(128).IsRequired(); });
    }
}