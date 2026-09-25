using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Server.Data;
using Nova.Server.Models;

namespace Nova.Server;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();

        api.MapGet("/users/search", async (string? q, NovaDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<object>());
            var term = q.Trim();
            var users = await db.Users.AsNoTracking()
                .Where(x => x.Username.ToLower().Contains(term.ToLower()) || x.DisplayName.ToLower().Contains(term.ToLower()))
                .OrderBy(x => x.Username).Take(25)
                .Select(x => new { x.Id, x.Username, x.DisplayName })
                .ToListAsync();
            return Results.Ok(users);
        });

        api.MapGet("/users/{userId:guid}/profile", async (Guid userId, NovaDbContext db) =>
        {
            var profile = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId);
            return Results.Ok(profile ?? new UserProfile { UserId = userId, UpdatedAt = DateTimeOffset.UtcNow });
        });

        api.MapPut("/me/profile", async (ProfileRequest request, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p, out var uid)) return Results.Unauthorized();
            var profile = await db.UserProfiles.FindAsync(uid) ?? new UserProfile { UserId = uid };
            profile.Bio = request.Bio?.Trim();
            profile.AvatarUrl = request.AvatarUrl?.Trim();
            profile.BannerUrl = request.BannerUrl?.Trim();
            profile.Theme = request.Theme?.Trim();
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            db.UserProfiles.Update(profile);
            await db.SaveChangesAsync();
            return Results.Ok(profile);
        });

        api.MapPost("/friends/requests", async (FriendRequestCreate r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p, out var uid)) return Results.Unauthorized();
            if (uid == r.UserId || !await db.Users.AnyAsync(x => x.Id == r.UserId)) return Results.BadRequest();
            if (await db.UserBlocks.AnyAsync(x => (x.UserId == uid && x.BlockedUserId == r.UserId) || (x.UserId == r.UserId && x.BlockedUserId == uid)))
                return Results.Conflict(new { error = "Users are blocked." });
            var a = Min(uid, r.UserId); var b = Max(uid, r.UserId);
            if (await db.Friendships.AnyAsync(x => x.UserAId == a && x.UserBId == b)) return Results.Conflict(new { error = "Already friends." });
            var pending = await db.FriendRequests.AnyAsync(x => x.Status == FriendRequestStatus.Pending &&
                ((x.SenderId == uid && x.RecipientId == r.UserId) || (x.SenderId == r.UserId && x.RecipientId == uid)));
            if (pending) return Results.Conflict(new { error = "Request already pending." });
            var item = new FriendRequest { Id=Guid.NewGuid(), SenderId=uid, RecipientId=r.UserId, Status=FriendRequestStatus.Pending, CreatedAt=DateTimeOffset.UtcNow };
            db.FriendRequests.Add(item);
            db.Notifications.Add(new Notification { Id=Guid.NewGuid(), UserId=r.UserId, Type=NotificationType.FriendRequest, Payload=JsonSerializer.Serialize(new { requestId=item.Id, fromUserId=uid }), CreatedAt=DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/friends/requests/{item.Id}", item);
        });

        api.MapPost("/friends/requests/{id:guid}/respond", async (Guid id, FriendResponseRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p, out var uid)) return Results.Unauthorized();
            var req = await db.FriendRequests.SingleOrDefaultAsync(x => x.Id == id && x.RecipientId == uid && x.Status == FriendRequestStatus.Pending);
            if (req is null) return Results.NotFound();
            req.Status = r.Accept ? FriendRequestStatus.Accepted : FriendRequestStatus.Declined;
            req.RespondedAt = DateTimeOffset.UtcNow;
            if (r.Accept)
            {
                db.Friendships.Add(new Friendship { Id=Guid.NewGuid(), UserAId=Min(req.SenderId,req.RecipientId), UserBId=Max(req.SenderId,req.RecipientId), CreatedAt=DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
            return Results.Ok(req);
        });

        api.MapGet("/friends", async (ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p, out var uid)) return Results.Unauthorized();
            var friends = await db.Friendships.AsNoTracking().Where(x=>x.UserAId==uid||x.UserBId==uid)
                .Select(x=>x.UserAId==uid?x.UserBId:x.UserAId).ToListAsync();
            return Results.Ok(await db.Users.AsNoTracking().Where(x=>friends.Contains(x.Id)).Select(x=>new{x.Id,x.Username,x.DisplayName}).ToListAsync());
        });

        api.MapPost("/blocks/{userId:guid}", async (Guid userId, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p,out var uid)) return Results.Unauthorized();
            if (uid==userId) return Results.BadRequest();
            if (!await db.UserBlocks.AnyAsync(x=>x.UserId==uid&&x.BlockedUserId==userId))
                db.UserBlocks.Add(new UserBlock { Id=Guid.NewGuid(),UserId=uid,BlockedUserId=userId,CreatedAt=DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(); return Results.NoContent();
        });

        api.MapDelete("/blocks/{userId:guid}", async (Guid userId, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p,out var uid)) return Results.Unauthorized();
            await db.UserBlocks.Where(x=>x.UserId==uid&&x.BlockedUserId==userId).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        api.MapPost("/messages/{messageId:guid}/reactions", async (Guid messageId, ReactionRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p,out var uid)) return Results.Unauthorized();
            var message=await db.Messages.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==messageId);
            if(message is null) return Results.NotFound();
            if(!await db.ConversationMembers.AnyAsync(x=>x.ConversationId==message.ConversationId&&x.UserId==uid)) return Results.NotFound();
            if(string.IsNullOrWhiteSpace(r.Emoji)||r.Emoji.Length>64) return Results.BadRequest();
            var item=await db.MessageReactions.SingleOrDefaultAsync(x=>x.MessageId==messageId&&x.UserId==uid&&x.Emoji==r.Emoji);
            if(item is null){item=new MessageReaction{Id=Guid.NewGuid(),MessageId=messageId,UserId=uid,Emoji=r.Emoji,CreatedAt=DateTimeOffset.UtcNow};db.MessageReactions.Add(item);}
            else db.MessageReactions.Remove(item);
            await db.SaveChangesAsync(); return Results.Ok(new{added=item is not null && db.Entry(item).State!=EntityState.Deleted});
        });

        api.MapPut("/messages/{messageId:guid}/state", async (Guid messageId, MessageStateRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p,out var uid)) return Results.Unauthorized();
            var msg=await db.Messages.SingleOrDefaultAsync(x=>x.Id==messageId&&x.SenderId==uid);
            if(msg is null) return Results.NotFound();
            var state=await db.MessageStates.FindAsync(messageId) ?? new MessageState{MessageId=messageId};
            state.ReplyToMessageId=r.ReplyToMessageId; state.ThreadRootMessageId=r.ThreadRootMessageId; state.EditCiphertext=r.EditCiphertext; state.EditedAt=DateTimeOffset.UtcNow;
            db.MessageStates.Update(state); await db.SaveChangesAsync(); return Results.Ok(state);
        });

        api.MapDelete("/messages/{messageId:guid}", async (Guid messageId, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if (!UserId(p,out var uid)) return Results.Unauthorized();
            var msg=await db.Messages.SingleOrDefaultAsync(x=>x.Id==messageId&&x.SenderId==uid);
            if(msg is null) return Results.NotFound();
            var state=await db.MessageStates.FindAsync(messageId) ?? new MessageState{MessageId=messageId};
            state.DeletedAt=DateTimeOffset.UtcNow; db.MessageStates.Update(state); await db.SaveChangesAsync(); return Results.NoContent();
        });

        api.MapPut("/conversations/{conversationId:guid}/read", async (Guid conversationId, ReadRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            if(!await db.ConversationMembers.AnyAsync(x=>x.ConversationId==conversationId&&x.UserId==uid)) return Results.NotFound();
            var state=await db.ReadStates.SingleOrDefaultAsync(x=>x.ConversationId==conversationId&&x.UserId==uid) ?? new ReadState{Id=Guid.NewGuid(),ConversationId=conversationId,UserId=uid};
            state.LastReadMessageId=r.MessageId; state.LastReadAt=DateTimeOffset.UtcNow; db.ReadStates.Update(state); await db.SaveChangesAsync(); return Results.Ok(state);
        });

        api.MapPost("/servers/{serverId:guid}/invites", async (Guid serverId, InviteRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            if(!await Permissions.Has(db,serverId,uid,ServerPermission.CreateInvites)) return Results.Forbid();
            var code=Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
            var invite=new ServerInvite{Id=Guid.NewGuid(),ServerId=serverId,CreatedByUserId=uid,Code=code,MaxUses=Math.Clamp(r.MaxUses??0,0,100000),CreatedAt=DateTimeOffset.UtcNow,ExpiresAt=r.ExpiresAt};
            db.ServerInvites.Add(invite); await db.SaveChangesAsync(); return Results.Created($"/api/v1/invites/{code}",invite);
        });

        api.MapPost("/invites/{code}", async (string code, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            var invite=await db.ServerInvites.SingleOrDefaultAsync(x=>x.Code==code&&!x.Revoked&&(x.ExpiresAt==null||x.ExpiresAt>DateTimeOffset.UtcNow));
            if(invite is null || (invite.MaxUses>0&&invite.Uses>=invite.MaxUses)) return Results.NotFound();
            if(await db.ServerBans.AnyAsync(x=>x.ServerId==invite.ServerId&&x.UserId==uid&&(x.ExpiresAt==null||x.ExpiresAt>DateTimeOffset.UtcNow))) return Results.Forbid();
            if(!await db.ServerMembers.AnyAsync(x=>x.ServerId==invite.ServerId&&x.UserId==uid)){db.ServerMembers.Add(new ServerMember{ServerId=invite.ServerId,UserId=uid,JoinedAt=DateTimeOffset.UtcNow});invite.Uses++;}
            await db.SaveChangesAsync(); return Results.Ok(new{invite.ServerId,joined=true});
        });

        api.MapPost("/servers/{serverId:guid}/bans", async (Guid serverId, BanRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            if(!await Permissions.Has(db,serverId,uid,ServerPermission.BanMembers)) return Results.Forbid();
            if(r.UserId==uid) return Results.BadRequest();
            var ban=await db.ServerBans.SingleOrDefaultAsync(x=>x.ServerId==serverId&&x.UserId==r.UserId);
            if(ban is null){ban=new ServerBan{Id=Guid.NewGuid(),ServerId=serverId,UserId=r.UserId};db.ServerBans.Add(ban);}
            ban.ModeratorId=uid;ban.Reason=r.Reason?.Trim();ban.ExpiresAt=r.ExpiresAt;ban.CreatedAt=DateTimeOffset.UtcNow;
            await db.ServerMembers.Where(x=>x.ServerId==serverId&&x.UserId==r.UserId).ExecuteDeleteAsync(); await db.SaveChangesAsync(); return Results.Ok(ban);
        });

        api.MapPost("/reports", async (ReportRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            if(string.IsNullOrWhiteSpace(r.Reason)||r.Reason.Length>512) return Results.BadRequest();
            var report=new ModerationReport{Id=Guid.NewGuid(),ReporterId=uid,ServerId=r.ServerId,TargetUserId=r.TargetUserId,MessageId=r.MessageId,Reason=r.Reason.Trim(),Details=r.Details?.Trim(),CreatedAt=DateTimeOffset.UtcNow};
            db.ModerationReports.Add(report); await db.SaveChangesAsync(); return Results.Created($"/api/v1/reports/{report.Id}",report);
        });

        api.MapGet("/notifications", async (ClaimsPrincipal p, NovaDbContext db, bool? unread) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            var q=db.Notifications.AsNoTracking().Where(x=>x.UserId==uid);
            if(unread==true) q=q.Where(x=>!x.Read);
            return Results.Ok(await q.OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync());
        });

        api.MapPost("/notifications/read", async (ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            await db.Notifications.Where(x=>x.UserId==uid&&!x.Read).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Read,true));
            return Results.NoContent();
        });

        api.MapPut("/servers/{serverId:guid}/discovery", async (Guid serverId, DiscoveryRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            var server=await db.Servers.FindAsync(serverId);
            if(server is null||server.OwnerId!=uid) return Results.Forbid();
            var item=await db.DiscoveryListings.FindAsync(serverId) ?? new DiscoveryListing{ServerId=serverId};
            item.Category=string.IsNullOrWhiteSpace(r.Category)?"general":r.Category.Trim();item.Public=r.Public;item.Tags=r.Tags?.Trim();item.ShortDescription=r.ShortDescription?.Trim();item.UpdatedAt=DateTimeOffset.UtcNow;
            db.DiscoveryListings.Update(item);await db.SaveChangesAsync();return Results.Ok(item);
        });

        app.MapGet("/api/v1/discovery/servers", async (NovaDbContext db, string? q, string? category) =>
        {
            var query=db.DiscoveryListings.AsNoTracking().Where(x=>x.Public);
            if(!string.IsNullOrWhiteSpace(category))query=query.Where(x=>x.Category==category);
            if(!string.IsNullOrWhiteSpace(q))query=query.Where(x=>x.Tags!.ToLower().Contains(q.ToLower())||x.ShortDescription!.ToLower().Contains(q.ToLower()));
            var items=await query.OrderByDescending(x=>x.UpdatedAt).Take(100).Join(db.Servers,s=>s.ServerId,v=>v.Id,(s,v)=>new{s.ServerId,v.Name,v.IconUrl,s.Category,s.Tags,s.ShortDescription}).ToListAsync();
            return Results.Ok(items);
        }).AllowAnonymous();

        api.MapPost("/calls", async (CallCreateRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            if(!await db.ConversationMembers.AnyAsync(x=>x.ConversationId==r.ConversationId&&x.UserId==uid)) return Results.NotFound();
            var call=new CallSession{Id=Guid.NewGuid(),ConversationId=r.ConversationId,InitiatorId=uid,Type=r.Type,State=CallState.Ringing,StartedAt=DateTimeOffset.UtcNow};
            db.CallSessions.Add(call);db.CallParticipants.Add(new CallParticipant{Id=Guid.NewGuid(),CallSessionId=call.Id,UserId=uid,JoinedAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();return Results.Created($"/api/v1/calls/{call.Id}",call);
        });

        api.MapPost("/calls/{callId:guid}/join", async (Guid callId, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid)) return Results.Unauthorized();
            var call=await db.CallSessions.FindAsync(callId);if(call is null)return Results.NotFound();
            if(!await db.ConversationMembers.AnyAsync(x=>x.ConversationId==call.ConversationId&&x.UserId==uid))return Results.Forbid();
            if(!await db.CallParticipants.AnyAsync(x=>x.CallSessionId==callId&&x.UserId==uid))db.CallParticipants.Add(new CallParticipant{Id=Guid.NewGuid(),CallSessionId=callId,UserId=uid,JoinedAt=DateTimeOffset.UtcNow});
            call.State=CallState.Active;await db.SaveChangesAsync();return Results.Ok(call);
        });

        api.MapPost("/calls/{callId:guid}/signal", async (Guid callId, SignalRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            var call=await db.CallSessions.FindAsync(callId);if(call is null)return Results.NotFound();
            if(!await db.CallParticipants.AnyAsync(x=>x.CallSessionId==callId&&x.UserId==uid))return Results.Forbid();
            return Results.Ok(new{type="call.signal",callId,toUserId=r.ToUserId,signal=r.Signal});
        });

        api.MapPost("/devices/{deviceId:guid}/key-bundle", async (Guid deviceId, KeyBundleRequest r, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            var device=await db.Devices.SingleOrDefaultAsync(x=>x.Id==deviceId&&x.UserId==uid&&!x.RevokedAt.HasValue);if(device is null)return Results.NotFound();
            if(string.IsNullOrWhiteSpace(r.IdentityPublicKey)||string.IsNullOrWhiteSpace(r.SignedPreKey)||string.IsNullOrWhiteSpace(r.PreKeySignature))return Results.BadRequest();
            var b=await db.DeviceKeyBundles.FindAsync(deviceId) ?? new DeviceKeyBundle{DeviceId=deviceId};
            b.IdentityPublicKey=r.IdentityPublicKey;b.SignedPreKey=r.SignedPreKey;b.PreKeySignature=r.PreKeySignature;b.OneTimePreKeys=r.OneTimePreKeys;b.ProtocolVersion=r.ProtocolVersion; b.UpdatedAt=DateTimeOffset.UtcNow;
            db.DeviceKeyBundles.Update(b);await db.SaveChangesAsync();return Results.Ok(new{b.DeviceId,b.ProtocolVersion,b.UpdatedAt});
        });

        api.MapGet("/users/{userId:guid}/key-bundles", async (Guid userId, NovaDbContext db) =>
            Results.Ok(await db.DeviceKeyBundles.AsNoTracking().Where(x=>x.DeviceId!=Guid.Empty&&db.Devices.Any(d=>d.Id==x.DeviceId&&d.UserId==userId&&!d.RevokedAt.HasValue)).ToListAsync()));

        api.MapGet("/realtime/resume", async (long? after, ClaimsPrincipal p, NovaDbContext db) =>
        {
            if(!UserId(p,out var uid))return Results.Unauthorized();
            var events=await db.RealtimeEvents.AsNoTracking().Where(x=>(x.UserId==null||x.UserId==uid)&&x.Sequence>(after??0)).OrderBy(x=>x.Sequence).Take(500).ToListAsync();
            return Results.Ok(new{events,nextCursor=events.Count==0?(after??0):events[^1].Sequence});
        });
    }

    private static bool UserId(ClaimsPrincipal p,out Guid id)=>Guid.TryParse(p.FindFirstValue("sub")??p.FindFirstValue(ClaimTypes.NameIdentifier),out id);
    private static Guid Min(Guid a,Guid b)=>a.CompareTo(b)<0?a:b;
    private static Guid Max(Guid a,Guid b)=>a.CompareTo(b)>0?a:b;
}

public static class Permissions
{
    public static async Task<bool> Has(NovaDbContext db, Guid serverId, Guid userId, ServerPermission permission)
    {
        var server=await db.Servers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==serverId);
        if(server?.OwnerId==userId)return true;
        var member=await db.ServerMembers.AsNoTracking().SingleOrDefaultAsync(x=>x.ServerId==serverId&&x.UserId==userId);
        if(member is null)return false;
        var roles=await db.ServerMemberRoles.AsNoTracking().Where(x=>x.ServerId==serverId&&x.UserId==userId).Join(db.Roles,r=>r.RoleId,role=>role.Id,(r,role)=>role.Permissions).ToListAsync();
        var everyone=await db.Roles.AsNoTracking().Where(x=>x.ServerId==serverId&&x.IsDefault).Select(x=>x.Permissions).FirstOrDefaultAsync();
        var bits=roles.Aggregate(everyone,(a,b)=>a|b);
        return ((ServerPermission)bits&ServerPermission.Administrator)==ServerPermission.Administrator || ((ServerPermission)bits&permission)==permission;
    }
}

public record ProfileRequest(string? Bio,string? AvatarUrl,string? BannerUrl,string? Theme);
public record FriendRequestCreate(Guid UserId);
public record FriendResponseRequest(bool Accept);
public record ReactionRequest(string Emoji);
public record MessageStateRequest(string? ReplyToMessageId,Guid? ThreadRootMessageId,string? EditCiphertext);
public record ReadRequest(Guid? MessageId);
public record InviteRequest(int? MaxUses,DateTimeOffset? ExpiresAt);
public record BanRequest(Guid UserId,string? Reason,DateTimeOffset? ExpiresAt);
public record ReportRequest(Guid? ServerId,Guid? TargetUserId,Guid? MessageId,string Reason,string? Details);
public record DiscoveryRequest(string? Category,bool Public,string? Tags,string? ShortDescription);
public record CallCreateRequest(Guid ConversationId,CallType Type);
public record SignalRequest(Guid? ToUserId,object Signal);
public record KeyBundleRequest(string IdentityPublicKey,string SignedPreKey,string PreKeySignature,string? OneTimePreKeys,int ProtocolVersion=1);
