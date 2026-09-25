using Microsoft.EntityFrameworkCore;

namespace Nova.Server.Data;

public static class PlatformSchema
{
    public static async Task InitializeAsync(NovaDbContext db, CancellationToken ct=default)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS "UserProfiles" ("UserId" uuid PRIMARY KEY, "Bio" varchar(2000), "AvatarUrl" varchar(2048), "BannerUrl" varchar(2048), "Theme" varchar(128), "IsDiscoverable" boolean NOT NULL DEFAULT TRUE, "UpdatedAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "FriendRequests" ("Id" uuid PRIMARY KEY, "SenderId" uuid NOT NULL, "RecipientId" uuid NOT NULL, "Status" integer NOT NULL, "CreatedAt" timestamptz NOT NULL, "RespondedAt" timestamptz);
        CREATE TABLE IF NOT EXISTS "Friendships" ("Id" uuid PRIMARY KEY, "UserAId" uuid NOT NULL, "UserBId" uuid NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_Friendships_Users" ON "Friendships" ("UserAId","UserBId");
        CREATE TABLE IF NOT EXISTS "UserBlocks" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "BlockedUserId" uuid NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserBlocks_Users" ON "UserBlocks" ("UserId","BlockedUserId");
        CREATE TABLE IF NOT EXISTS "MessageStates" ("MessageId" uuid PRIMARY KEY, "ReplyToMessageId" varchar(64), "ThreadRootMessageId" uuid, "EditedAt" timestamptz, "DeletedAt" timestamptz, "EditCiphertext" text);
        CREATE TABLE IF NOT EXISTS "MessageReactions" ("Id" uuid PRIMARY KEY, "MessageId" uuid NOT NULL, "UserId" uuid NOT NULL, "Emoji" varchar(64) NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_MessageReactions_Unique" ON "MessageReactions" ("MessageId","UserId","Emoji");
        CREATE TABLE IF NOT EXISTS "ReadStates" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "ConversationId" uuid NOT NULL, "LastReadMessageId" uuid, "LastReadAt" timestamptz);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ReadStates_Unique" ON "ReadStates" ("UserId","ConversationId");
        CREATE TABLE IF NOT EXISTS "ServerInvites" ("Id" uuid PRIMARY KEY, "ServerId" uuid NOT NULL, "CreatedByUserId" uuid NOT NULL, "Code" varchar(64) NOT NULL, "MaxUses" integer NOT NULL, "Uses" integer NOT NULL, "CreatedAt" timestamptz NOT NULL, "ExpiresAt" timestamptz, "Revoked" boolean NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServerInvites_Code" ON "ServerInvites" ("Code");
        CREATE TABLE IF NOT EXISTS "ServerBans" ("Id" uuid PRIMARY KEY, "ServerId" uuid NOT NULL, "UserId" uuid NOT NULL, "ModeratorId" uuid NOT NULL, "Reason" varchar(2000), "CreatedAt" timestamptz NOT NULL, "ExpiresAt" timestamptz);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ServerBans_ServerUser" ON "ServerBans" ("ServerId","UserId");
        CREATE TABLE IF NOT EXISTS "ChannelPermissionOverrides" ("Id" uuid PRIMARY KEY, "ChannelId" uuid NOT NULL, "RoleId" uuid, "UserId" uuid, "Allow" bigint NOT NULL, "Deny" bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS "Attachments" ("Id" uuid PRIMARY KEY, "OwnerId" uuid NOT NULL, "MessageId" uuid, "FileName" varchar(512) NOT NULL, "ContentType" varchar(256) NOT NULL, "Size" bigint NOT NULL, "StorageKey" varchar(2048) NOT NULL, "StorageProvider" varchar(64) NOT NULL, "EncryptionVersion" varchar(64) NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "UploadSessions" ("Id" uuid PRIMARY KEY, "OwnerId" uuid NOT NULL, "FileName" varchar(512) NOT NULL, "ContentType" varchar(256) NOT NULL, "TotalSize" bigint NOT NULL, "ReceivedSize" bigint NOT NULL, "StorageKey" varchar(2048) NOT NULL, "Completed" boolean NOT NULL, "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL);
        CREATE INDEX IF NOT EXISTS "IX_UploadSessions_Owner_Created" ON "UploadSessions" ("OwnerId","CreatedAt");
        CREATE TABLE IF NOT EXISTS "Presences" ("UserId" uuid PRIMARY KEY, "Status" varchar(32) NOT NULL, "CustomStatus" varchar(512), "LastSeenAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "Notifications" ("Id" uuid PRIMARY KEY, "UserId" uuid NOT NULL, "Type" integer NOT NULL, "Payload" text NOT NULL, "Read" boolean NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "ModerationReports" ("Id" uuid PRIMARY KEY, "ReporterId" uuid NOT NULL, "ServerId" uuid, "TargetUserId" uuid, "MessageId" uuid, "Reason" varchar(512) NOT NULL, "Details" varchar(4000), "Status" varchar(32) NOT NULL, "CreatedAt" timestamptz NOT NULL, "ResolvedAt" timestamptz);
        CREATE TABLE IF NOT EXISTS "AuditLogEntries" ("Id" uuid PRIMARY KEY, "ServerId" uuid NOT NULL, "ActorId" uuid NOT NULL, "Action" integer NOT NULL, "TargetId" uuid, "Metadata" text, "CreatedAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "Applications" ("Id" uuid PRIMARY KEY, "OwnerId" uuid NOT NULL, "Name" varchar(100) NOT NULL, "Description" varchar(2000), "Type" integer NOT NULL, "IconUrl" varchar(2048), "CreatedAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "ApplicationTokens" ("Id" uuid PRIMARY KEY, "ApplicationId" uuid NOT NULL, "TokenHash" varchar(128) NOT NULL, "CreatedAt" timestamptz NOT NULL, "RevokedAt" timestamptz);
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ApplicationTokens_Hash" ON "ApplicationTokens" ("TokenHash");
        CREATE TABLE IF NOT EXISTS "Webhooks" ("Id" uuid PRIMARY KEY, "ServerId" uuid NOT NULL, "ChannelId" uuid NOT NULL, "CreatedByUserId" uuid NOT NULL, "Name" varchar(100) NOT NULL, "TokenHash" varchar(128) NOT NULL, "CreatedAt" timestamptz NOT NULL, "RevokedAt" timestamptz);
        CREATE TABLE IF NOT EXISTS "DiscoveryListings" ("ServerId" uuid PRIMARY KEY, "Category" varchar(64) NOT NULL, "Public" boolean NOT NULL, "Tags" varchar(1000), "ShortDescription" varchar(500), "UpdatedAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "CallSessions" ("Id" uuid PRIMARY KEY, "ConversationId" uuid NOT NULL, "InitiatorId" uuid NOT NULL, "Type" integer NOT NULL, "State" integer NOT NULL, "SfuProvider" varchar(128), "StartedAt" timestamptz NOT NULL, "EndedAt" timestamptz);
        CREATE TABLE IF NOT EXISTS "CallParticipants" ("Id" uuid PRIMARY KEY, "CallSessionId" uuid NOT NULL, "UserId" uuid NOT NULL, "JoinedAt" timestamptz NOT NULL, "LeftAt" timestamptz);
        CREATE TABLE IF NOT EXISTS "DeviceKeyBundles" ("DeviceId" uuid PRIMARY KEY, "IdentityPublicKey" varchar(8192) NOT NULL, "SignedPreKey" varchar(8192) NOT NULL, "PreKeySignature" varchar(8192) NOT NULL, "OneTimePreKeys" text, "ProtocolVersion" integer NOT NULL, "UpdatedAt" timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS "RealtimeEvents" ("Sequence" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, "UserId" uuid, "ServerId" uuid, "Type" varchar(128) NOT NULL, "Payload" text NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE INDEX IF NOT EXISTS "IX_RealtimeEvents_User_Sequence" ON "RealtimeEvents" ("UserId","Sequence");
        """;
        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}