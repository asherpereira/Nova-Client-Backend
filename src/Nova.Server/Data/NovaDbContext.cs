using Microsoft.EntityFrameworkCore;
using Nova.Server.Models;

namespace Nova.Server.Data;

public sealed class NovaDbContext(DbContextOptions<NovaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<EncryptedMessage> Messages => Set<EncryptedMessage>();

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
        });
    }
}
