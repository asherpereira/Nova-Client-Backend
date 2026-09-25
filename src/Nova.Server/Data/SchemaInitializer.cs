using Microsoft.EntityFrameworkCore;

namespace Nova.Server.Data;

public static class SchemaInitializer
{
    public static async Task InitializeAsync(NovaDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        // Development-safe additive bootstrap. This keeps existing 0.1 development
        // databases usable while we move toward versioned EF migrations.
        const string sql = """
        CREATE TABLE IF NOT EXISTS "Conversations" (
            "Id" uuid PRIMARY KEY,
            "Type" integer NOT NULL,
            "Name" varchar(100),
            "IconUrl" varchar(2048),
            "CreatedByUserId" uuid NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "ConversationMembers" (
            "ConversationId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "JoinedAt" timestamptz NOT NULL,
            "LastReadAt" timestamptz,
            "IsMuted" boolean NOT NULL,
            PRIMARY KEY ("ConversationId", "UserId")
        );

        CREATE TABLE IF NOT EXISTS "Servers" (
            "Id" uuid PRIMARY KEY,
            "Name" varchar(100) NOT NULL,
            "Description" varchar(1000),
            "IconUrl" varchar(2048),
            "OwnerId" uuid NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "ServerMembers" (
            "ServerId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "JoinedAt" timestamptz NOT NULL,
            "Nickname" varchar(64),
            PRIMARY KEY ("ServerId", "UserId")
        );

        CREATE TABLE IF NOT EXISTS "Roles" (
            "Id" uuid PRIMARY KEY,
            "ServerId" uuid NOT NULL,
            "Name" varchar(100) NOT NULL,
            "Permissions" bigint NOT NULL,
            "Position" integer NOT NULL,
            "IsDefault" boolean NOT NULL,
            "CreatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "Channels" (
            "Id" uuid PRIMARY KEY,
            "ServerId" uuid NOT NULL,
            "ParentChannelId" uuid,
            "Name" varchar(100) NOT NULL,
            "Topic" varchar(1024),
            "Type" integer NOT NULL,
            "Position" integer NOT NULL,
            "IsPrivate" boolean NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS "Devices" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "Name" varchar(100) NOT NULL,
            "Platform" varchar(32) NOT NULL,
            "PushToken" varchar(4096),
            "IdentityPublicKey" varchar(8192),
            "SignedPreKey" varchar(8192),
            "PreKeySignature" varchar(8192),
            "CreatedAt" timestamptz NOT NULL,
            "LastSeenAt" timestamptz NOT NULL,
            "RevokedAt" timestamptz
        );

        CREATE TABLE IF NOT EXISTS "RefreshSessions" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "DeviceId" uuid NOT NULL,
            "TokenHash" varchar(128) NOT NULL,
            "CreatedAt" timestamptz NOT NULL,
            "ExpiresAt" timestamptz NOT NULL,
            "RevokedAt" timestamptz
        );

        CREATE INDEX IF NOT EXISTS "IX_ConversationMembers_UserId_ConversationId"
            ON "ConversationMembers" ("UserId", "ConversationId");
        CREATE INDEX IF NOT EXISTS "IX_ServerMembers_UserId"
            ON "ServerMembers" ("UserId");
        CREATE INDEX IF NOT EXISTS "IX_Roles_ServerId_Position"
            ON "Roles" ("ServerId", "Position");
        CREATE INDEX IF NOT EXISTS "IX_Channels_ServerId_Position"
            ON "Channels" ("ServerId", "Position");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshSessions_TokenHash"
            ON "RefreshSessions" ("TokenHash");

        ALTER TABLE "ConversationMembers"
            ADD CONSTRAINT "FK_ConversationMembers_Conversations"
            FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE;
        ALTER TABLE "ConversationMembers"
            ADD CONSTRAINT "FK_ConversationMembers_Users"
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE;
        ALTER TABLE "ServerMembers"
            ADD CONSTRAINT "FK_ServerMembers_Servers"
            FOREIGN KEY ("ServerId") REFERENCES "Servers" ("Id") ON DELETE CASCADE;
        ALTER TABLE "ServerMembers"
            ADD CONSTRAINT "FK_ServerMembers_Users"
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE;
        ALTER TABLE "Roles"
            ADD CONSTRAINT "FK_Roles_Servers"
            FOREIGN KEY ("ServerId") REFERENCES "Servers" ("Id") ON DELETE CASCADE;
        ALTER TABLE "Channels"
            ADD CONSTRAINT "FK_Channels_Servers"
            FOREIGN KEY ("ServerId") REFERENCES "Servers" ("Id") ON DELETE CASCADE;
        ALTER TABLE "Devices"
            ADD CONSTRAINT "FK_Devices_Users"
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE;
        ALTER TABLE "RefreshSessions"
            ADD CONSTRAINT "FK_RefreshSessions_Users"
            FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE;
        ALTER TABLE "RefreshSessions"
            ADD CONSTRAINT "FK_RefreshSessions_Devices"
            FOREIGN KEY ("DeviceId") REFERENCES "Devices" ("Id") ON DELETE CASCADE;
        """;

        // PostgreSQL does not support "ADD CONSTRAINT IF NOT EXISTS". The first run
        // therefore may report duplicate-constraint errors after a partial bootstrap.
        // Catch only duplicate-object errors and keep the rest fatal.
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Existing development schema: tables/indexes are already present.
        }
    }
}
