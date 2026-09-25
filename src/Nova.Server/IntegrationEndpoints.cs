using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nova.Server.Data;
using Nova.Server.Models;

namespace Nova.Server;

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1").RequireAuthorization();

        api.MapPut("/presence", async (PresenceRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            var item=await db.Presences.FindAsync(uid)??new Presence{UserId=uid};
            item.Status=string.IsNullOrWhiteSpace(r.Status)?"online":r.Status.Trim().ToLowerInvariant();
            item.CustomStatus=r.CustomStatus?.Trim();item.LastSeenAt=DateTimeOffset.UtcNow;
            db.Presences.Update(item);await db.SaveChangesAsync();return Results.Ok(item);
        });

        api.MapGet("/presence/{userId:guid}", async (Guid userId,NovaDbContext db)=>
            Results.Ok(await db.Presences.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==userId)??new Presence{UserId=userId,Status="offline"}));

        api.MapPost("/attachments/prepare", async (AttachmentPrepareRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            if(string.IsNullOrWhiteSpace(r.FileName)||r.FileName.Length>512||r.Size<0||r.Size>2_147_483_647)return Results.BadRequest(new{error="Invalid attachment metadata."});
            var id=Guid.NewGuid();
            var item=new Attachment{Id=id,OwnerId=uid,MessageId=r.MessageId,FileName=r.FileName.Trim(),ContentType=string.IsNullOrWhiteSpace(r.ContentType)?"application/octet-stream":r.ContentType.Trim(),Size=r.Size,StorageKey=$"attachments/{uid:N}/{id:N}",StorageProvider="local",EncryptionVersion="pending-e2ee",CreatedAt=DateTimeOffset.UtcNow};
            db.Attachments.Add(item);await db.SaveChangesAsync();
            return Results.Ok(new{id=item.Id,uploadUrl=(string?)null,storageKey=item.StorageKey,encryptionVersion=item.EncryptionVersion});
        });

        api.MapGet("/attachments/{id:guid}", async(Guid id,ClaimsPrincipal p,NovaDbContext db)=>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            var item=await db.Attachments.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.OwnerId==uid);
            return item is null?Results.NotFound():Results.Ok(item);
        });

        api.MapPost("/applications", async(ApplicationCreateRequest r,ClaimsPrincipal p,NovaDbContext db)=>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            if(string.IsNullOrWhiteSpace(r.Name)||r.Name.Length>100)return Results.BadRequest();
            var item=new Application{Id=Guid.NewGuid(),OwnerId=uid,Name=r.Name.Trim(),Description=r.Description?.Trim(),Type=r.Type,IconUrl=r.IconUrl?.Trim(),CreatedAt=DateTimeOffset.UtcNow};
            db.Applications.Add(item);await db.SaveChangesAsync();return Results.Created($"/api/v1/applications/{item.Id}",item);
        });

        api.MapPost("/applications/{applicationId:guid}/tokens", async(Guid applicationId,ClaimsPrincipal p,NovaDbContext db)=>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            if(!await db.Applications.AnyAsync(x=>x.Id==applicationId&&x.OwnerId==uid))return Results.NotFound();
            var raw=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace("+","-").Replace("/","_").TrimEnd('=');
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            db.ApplicationTokens.Add(new ApplicationToken{Id=Guid.NewGuid(),ApplicationId=applicationId,TokenHash=hash,CreatedAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();return Results.Ok(new{token=raw});
        });

        api.MapPost("/servers/{serverId:guid}/webhooks", async(Guid serverId,WebhookCreateRequest r,ClaimsPrincipal p,NovaDbContext db)=>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            if(!await Permissions.Has(db,serverId,uid,ServerPermission.ManageChannels))return Results.Forbid();
            if(!await db.Channels.AnyAsync(x=>x.Id==r.ChannelId&&x.ServerId==serverId))return Results.BadRequest();
            var raw=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+","-").Replace("/","_").TrimEnd('=');
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            var item=new Webhook{Id=Guid.NewGuid(),ServerId=serverId,ChannelId=r.ChannelId,CreatedByUserId=uid,Name=r.Name.Trim(),TokenHash=hash,CreatedAt=DateTimeOffset.UtcNow};
            db.Webhooks.Add(item);await db.SaveChangesAsync();return Results.Created($"/api/v1/webhooks/{item.Id}",new{item.Id,item.Name,item.ChannelId,token=raw});
        });

        api.MapGet("/servers/{serverId:guid}/audit-log", async(Guid serverId,ClaimsPrincipal p,NovaDbContext db)=>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            if(!await Permissions.Has(db,serverId,uid,ServerPermission.ManageServer))return Results.Forbid();
            return Results.Ok(await db.AuditLogEntries.AsNoTracking().Where(x=>x.ServerId==serverId).OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync());
        });

        api.MapGet("/servers/{serverId:guid}/reports", async(Guid serverId,ClaimsPrincipal p,NovaDbContext db)=>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            if(!await Permissions.Has(db,serverId,uid,ServerPermission.ManageServer))return Results.Forbid();
            return Results.Ok(await db.ModerationReports.AsNoTracking().Where(x=>x.ServerId==serverId).OrderByDescending(x=>x.CreatedAt).Take(200).ToListAsync());
        });

        api.MapPut("/channels/{channelId:guid}/permission-overrides", async(Guid channelId,PermissionOverrideRequest r,ClaimsPrincipal p,NovaDbContext db)=>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            var channel=await db.Channels.FindAsync(channelId);if(channel is null)return Results.NotFound();
            if(!await Permissions.Has(db,channel.ServerId,uid,ServerPermission.ManageChannels))return Results.Forbid();
            var item=await db.ChannelPermissionOverrides.SingleOrDefaultAsync(x=>x.ChannelId==channelId&&x.RoleId==r.RoleId&&x.UserId==r.UserId);
            if(item is null){item=new ChannelPermissionOverride{Id=Guid.NewGuid(),ChannelId=channelId,RoleId=r.RoleId,UserId=r.UserId};db.ChannelPermissionOverrides.Add(item);}
            item.Allow=r.Allow;item.Deny=r.Deny;await db.SaveChangesAsync();return Results.Ok(item);
        });
    }

    private static bool UserId(ClaimsPrincipal p,out Guid id)=>Guid.TryParse(p.FindFirstValue("sub")??p.FindFirstValue(ClaimTypes.NameIdentifier),out id);
}
public record PresenceRequest(string Status,string? CustomStatus);
public record AttachmentPrepareRequest(string FileName,string? ContentType,long Size,Guid? MessageId);
public record ApplicationCreateRequest(string Name,string? Description,ApplicationType Type,string? IconUrl);
public record WebhookCreateRequest(string Name,Guid ChannelId);
public record PermissionOverrideRequest(Guid? RoleId,Guid? UserId,long Allow,long Deny);
