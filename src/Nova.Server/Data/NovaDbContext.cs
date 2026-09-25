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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<EncryptedMessage>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            entity.Property(x => x.Ciphertext).IsRequired();
            entity.Property(x => x.Nonce).HasMaxLength(256).IsRequired();
            entity.Property(x => x.EncryptionVersion).IsRequired();
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UpdatedAt);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.IconUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<ConversationMember>(entity =>
        {
            entity.HasKey(x => new { x.ConversationId, x.UserId });
            entity.HasIndex(x => new { x.UserId, x.ConversationId });
            entity.HasOne(x => x.Conversation).WithMany(x => x.Members)
                .HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NovaServer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OwnerId);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.IconUrl).HasMaxLength(2048);
        });

        modelBuilder.Entity<ServerMember>(entity =>
        {
            entity.HasKey(x => new { x.ServerId, x.UserId });
            entity.HasIndex(x => x.UserId);
            entity.HasOne(x => x.Server).WithMany(x => x.Members)
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerId, x.Position });
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.Server).WithMany(x => x.Roles)
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ServerId, x.Position });
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Topic).HasMaxLength(1024);
            entity.HasOne(x => x.Server).WithMany(x => x.Channels)
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Channel>().WithMany()
                .HasForeignKey(x => x.ParentChannelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.UserId, x.RevokedAt });
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Platform).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PushToken).HasMaxLength(4096);
            entity.Property(x => x.IdentityPublicKey).HasMaxLength(8192);
            entity.Property(x => x.SignedPreKey).HasMaxLength(8192);
            entity.Property(x => x.PreKeySignature).HasMaxLength(8192);
            entity.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.RevokedAt, x.ExpiresAt });
            entity.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Device).WithMany()
                .HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
