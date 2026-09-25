using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Nova.Server.Data;
using Nova.Server;
using Nova.Server.Models;
using Nova.Server.Services;

var builder = WebApplication.CreateBuilder(args);

const string NovaVersion = "1.0.0";

builder.WebHost.ConfigureKestrel(options =>
{
    // REST messages are bounded by the endpoint itself; this is an outer guard
    // against unexpectedly large JSON requests.
    options.Limits.MaxRequestBodySize = 8 * 1024 * 1024;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddDbContext<NovaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NovaDatabase")
        ?? throw new InvalidOperationException("NovaDatabase connection string is missing.")));

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ConnectionManager>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partition authentication traffic by source address so one client cannot
    // consume the entire server's authentication budget.
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Partition message traffic by authenticated account. This keeps one noisy
    // account from starving other users while allowing multiple devices.
    options.AddPolicy("messages", context =>
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                     ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is missing.");

if (jwtKey.Length < 32 ||
    jwtKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
    jwtKey.Contains("replace-with", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Jwt:Key must be a strong, non-placeholder secret of at least 32 characters.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "nova",
            ValidateAudience = true,
            ValidAudience = "nova-client",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://nova.invalid/problems/internal-server-error",
            title = "An unexpected server error occurred.",
            status = 500,
            traceId = context.TraceIdentifier
        });
    });
});

app.UseRateLimiter();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.MapPlatformEndpoints();
app.MapIntegrationEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NovaDbContext>();
    await SchemaInitializer.InitializeAsync(db);
    await PlatformSchema.InitializeAsync(db);
}

app.MapGet("/health", async (NovaDbContext db, CancellationToken ct) =>
{
    var databaseAvailable = await db.Database.CanConnectAsync(ct);

    return databaseAvailable
        ? Results.Ok(new
        {
            status = "ok",
            service = "nova-backend",
            version = NovaVersion
        })
        : Results.Json(new
        {
            status = "degraded",
            service = "nova-backend",
            version = NovaVersion
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "alive",
    service = "nova-backend",
    version = NovaVersion
})).AllowAnonymous();

app.MapGet("/health/ready", async (NovaDbContext db, CancellationToken ct) =>
{
    var databaseAvailable = await db.Database.CanConnectAsync(ct);

    return databaseAvailable
        ? Results.Ok(new { status = "ready", service = "nova-backend", version = NovaVersion })
        : Results.Json(new { status = "not_ready", service = "nova-backend", version = NovaVersion },
            statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapPost("/api/v1/auth/register", async (
    RegisterRequest request,
    NovaDbContext db,
    IPasswordHasher<User> hasher) =>
{
    var username = request.Username.Trim();
    var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
        ? username
        : request.DisplayName.Trim();

    if (username.Length is < 3 or > 32)
        return Results.BadRequest(new { error = "Username must be 3-32 characters." });

    if (displayName.Length > 64)
        return Results.BadRequest(new { error = "Display name must be 64 characters or fewer." });

    if (request.Password.Length is < 12 or > 1024)
        return Results.BadRequest(new { error = "Password must be between 12 and 1024 characters." });

    if (await db.Users.AnyAsync(x => x.Username.ToLower() == username.ToLower()))
        return Results.Conflict(new { error = "Username is already taken." });

    var user = new User
    {
        Id = Guid.NewGuid(),
        Username = username,
        DisplayName = displayName,
        CreatedAt = DateTimeOffset.UtcNow
    };

    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/users/{user.Id}", new
    {
        user.Id,
        user.Username,
        user.DisplayName
    });
})
.RequireRateLimiting("auth");

app.MapPost("/api/v1/auth/login", async (
    LoginRequest request,
    NovaDbContext db,
    IPasswordHasher<User> hasher,
    TokenService tokens) =>
{
    var user = await db.Users.SingleOrDefaultAsync(
        x => x.Username.ToLower() == request.Username.Trim().ToLower());

    if (user is null)
        return Results.Unauthorized();

    var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

    if (result == PasswordVerificationResult.Failed)
        return Results.Unauthorized();

    var device = new Device
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        Name = string.IsNullOrWhiteSpace(request.DeviceName) ? "Unknown device" : request.DeviceName.Trim(),
        Platform = string.IsNullOrWhiteSpace(request.Platform) ? "unknown" : request.Platform.Trim().ToLowerInvariant(),
        CreatedAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow
    };

    var refresh = tokens.CreateRefreshToken();

    db.Devices.Add(device);
    db.RefreshSessions.Add(new RefreshSession
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        DeviceId = device.Id,
        TokenHash = refresh.Hash,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = refresh.ExpiresAt
    });

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        accessToken = tokens.CreateAccessToken(user, device.Id),
        refreshToken = refresh.RawToken,
        tokenType = "Bearer",
        expiresIn = 900,
        user = new { user.Id, user.Username, user.DisplayName },
        device = new { device.Id, device.Name, device.Platform }
    });
})
.RequireRateLimiting("auth");

app.MapPost("/api/v1/auth/refresh", async (
    RefreshRequest request,
    NovaDbContext db,
    TokenService tokens) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 512)
        return Results.BadRequest(new { error = "Refresh token is invalid." });

    var hash = TokenService.HashRefreshToken(request.RefreshToken);
    var session = await db.RefreshSessions
        .Include(x => x.User)
        .Include(x => x.Device)
        .SingleOrDefaultAsync(x => x.TokenHash == hash);

    if (session is null || session.RevokedAt is not null || session.ExpiresAt <= DateTimeOffset.UtcNow ||
        session.Device.RevokedAt is not null)
        return Results.Unauthorized();

    session.Device.LastSeenAt = DateTimeOffset.UtcNow;

    var next = tokens.CreateRefreshToken();
    session.RevokedAt = DateTimeOffset.UtcNow;

    db.RefreshSessions.Add(new RefreshSession
    {
        Id = Guid.NewGuid(),
        UserId = session.UserId,
        DeviceId = session.DeviceId,
        TokenHash = next.Hash,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = next.ExpiresAt
    });

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        accessToken = tokens.CreateAccessToken(session.User, session.DeviceId),
        refreshToken = next.RawToken,
        tokenType = "Bearer",
        expiresIn = 900
    });
})
.RequireRateLimiting("auth");

app.MapPost("/api/v1/auth/logout", async (
    RefreshRequest request,
    NovaDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
        return Results.NoContent();

    var hash = TokenService.HashRefreshToken(request.RefreshToken);
    var session = await db.RefreshSessions.SingleOrDefaultAsync(x => x.TokenHash == hash);

    if (session is not null)
    {
        session.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    return Results.NoContent();
});

app.MapGet("/api/v1/me", async (ClaimsPrincipal principal, NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId);
    return user is null
        ? Results.NotFound()
        : Results.Ok(new { user.Id, user.Username, user.DisplayName, user.CreatedAt });
}).RequireAuthorization();

app.MapGet("/api/v1/devices", async (ClaimsPrincipal principal, NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var devices = await db.Devices.AsNoTracking()
        .Where(x => x.UserId == userId && x.RevokedAt == null)
        .OrderByDescending(x => x.LastSeenAt)
        .Select(x => new { x.Id, x.Name, x.Platform, x.CreatedAt, x.LastSeenAt })
        .ToListAsync();

    return Results.Ok(devices);
}).RequireAuthorization();

app.MapPost("/api/v1/devices", async (
    DeviceRequest request,
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 ||
        string.IsNullOrWhiteSpace(request.Platform) || request.Platform.Length > 32)
        return Results.BadRequest(new { error = "A valid device name and platform are required." });

    var device = new Device
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Name = request.Name.Trim(),
        Platform = request.Platform.Trim().ToLowerInvariant(),
        PushToken = request.PushToken,
        IdentityPublicKey = request.IdentityPublicKey,
        SignedPreKey = request.SignedPreKey,
        PreKeySignature = request.PreKeySignature,
        CreatedAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow
    };

    db.Devices.Add(device);
    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/devices/{device.Id}", new
    {
        device.Id,
        device.Name,
        device.Platform,
        device.CreatedAt
    });
}).RequireAuthorization();

app.MapPost("/api/v1/devices/{deviceId:guid}/revoke", async (
    Guid deviceId,
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var device = await db.Devices.SingleOrDefaultAsync(x => x.Id == deviceId && x.UserId == userId);
    if (device is null)
        return Results.NotFound();

    device.RevokedAt = DateTimeOffset.UtcNow;

    await db.RefreshSessions
        .Where(x => x.DeviceId == deviceId && x.RevokedAt == null)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));

    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/v1/conversations", async (
    CreateConversationRequest request,
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var requestedMembers = request.UserIds.Distinct().Where(x => x != userId).ToList();

    if (request.Type == ConversationType.DirectMessage && requestedMembers.Count != 1)
        return Results.BadRequest(new { error = "A direct message requires exactly one other user." });

    if (request.Type == ConversationType.GroupDirectMessage && requestedMembers.Count is < 1 or > 49)
        return Results.BadRequest(new { error = "A group DM must contain 2-50 users." });

    var memberIds = requestedMembers.Append(userId).Distinct().ToList();
    var existingUsers = await db.Users.CountAsync(x => memberIds.Contains(x.Id));
    if (existingUsers != memberIds.Count)
        return Results.BadRequest(new { error = "One or more users do not exist." });

    if (request.Type == ConversationType.DirectMessage)
    {
        var otherId = requestedMembers[0];
        var existing = await db.Conversations
            .Where(x => x.Type == ConversationType.DirectMessage)
            .Where(x => x.Members.Any(m => m.UserId == userId))
            .Where(x => x.Members.Any(m => m.UserId == otherId))
            .Where(x => x.Members.Count == 2)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        if (existing != Guid.Empty)
            return Results.Ok(new { id = existing, existing = true });
    }

    var now = DateTimeOffset.UtcNow;
    var conversation = new Conversation
    {
        Id = Guid.NewGuid(),
        Type = request.Type,
        Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
        IconUrl = string.IsNullOrWhiteSpace(request.IconUrl) ? null : request.IconUrl.Trim(),
        CreatedByUserId = userId,
        CreatedAt = now,
        UpdatedAt = now
    };

    foreach (var memberId in memberIds)
    {
        conversation.Members.Add(new ConversationMember
        {
            ConversationId = conversation.Id,
            UserId = memberId,
            JoinedAt = now
        });
    }

    db.Conversations.Add(conversation);
    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/conversations/{conversation.Id}", new
    {
        conversation.Id,
        conversation.Type,
        conversation.Name,
        conversation.IconUrl,
        members = memberIds
    });
}).RequireAuthorization();

app.MapGet("/api/v1/conversations", async (
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var conversations = await db.Conversations.AsNoTracking()
        .Where(x => x.Members.Any(m => m.UserId == userId))
        .OrderByDescending(x => x.UpdatedAt)
        .Select(x => new
        {
            x.Id,
            x.Type,
            x.Name,
            x.IconUrl,
            x.CreatedAt,
            x.UpdatedAt,
            members = x.Members.Select(m => new
            {
                m.UserId,
                m.JoinedAt,
                m.LastReadAt,
                m.IsMuted
            })
        })
        .ToListAsync();

    return Results.Ok(conversations);
}).RequireAuthorization();

app.MapGet("/api/v1/conversations/{conversationId:guid}/messages", async (
    Guid conversationId,
    ClaimsPrincipal principal,
    NovaDbContext db,
    DateTimeOffset? before,
    int? limit) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var member = await db.ConversationMembers.FindAsync(conversationId, userId);
    if (member is null)
        return Results.NotFound();

    var take = Math.Clamp(limit ?? 50, 1, 100);

    var query = db.Messages.AsNoTracking()
        .Where(x => x.ConversationId == conversationId);

    if (before.HasValue)
        query = query.Where(x => x.CreatedAt < before.Value);

    var messages = await query
        .OrderByDescending(x => x.CreatedAt)
        .Take(take)
        .Select(x => new
        {
            x.Id,
            x.ClientMessageId,
            x.ConversationId,
            x.SenderId,
            x.Ciphertext,
            x.Nonce,
            x.EncryptionVersion,
            x.CreatedAt
        })
        .ToListAsync();

    messages.Reverse();
    return Results.Ok(messages);
}).RequireAuthorization();

app.MapPost("/api/v1/messages", async (
    EncryptedMessageRequest request,
    ClaimsPrincipal principal,
    NovaDbContext db,
    ConnectionManager connections) =>
{
    if (!TryGetUserId(principal, out var senderId))
        return Results.Unauthorized();

    if (request.ConversationId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.Ciphertext) ||
        string.IsNullOrWhiteSpace(request.Nonce))
        return Results.BadRequest(new { error = "Conversation, ciphertext and nonce are required." });

    if (request.Ciphertext.Length > 4_000_000 || request.Nonce.Length > 256)
        return Results.BadRequest(new { error = "Message payload is too large." });

    var memberIds = await db.ConversationMembers
        .Where(x => x.ConversationId == request.ConversationId)
        .Select(x => x.UserId)
        .ToListAsync();

    if (!memberIds.Contains(senderId))
        return Results.NotFound();

    if (request.ClientMessageId.HasValue)
    {
        var existing = await db.Messages.AsNoTracking()
            .Where(x => x.SenderId == senderId && x.ClientMessageId == request.ClientMessageId)
            .Select(x => new { x.Id, x.ConversationId, x.SenderId, x.CreatedAt })
            .SingleOrDefaultAsync();

        if (existing is not null)
            return Results.Ok(new { existing.Id, existing.ConversationId, existing.SenderId, existing.CreatedAt, duplicate = true });
    }

    var message = new EncryptedMessage
    {
        Id = Guid.NewGuid(),
        ConversationId = request.ConversationId,
        SenderId = senderId,
        ClientMessageId = request.ClientMessageId,
        Ciphertext = request.Ciphertext,
        Nonce = request.Nonce,
        EncryptionVersion = request.EncryptionVersion,
        CreatedAt = DateTimeOffset.UtcNow
    };

    db.Messages.Add(message);

    await db.Conversations
        .Where(x => x.Id == request.ConversationId)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, message.CreatedAt));

    await db.SaveChangesAsync();

    await connections.BroadcastToUsersAsync(
        memberIds,
        new
        {
            type = "message.created",
            message = new
            {
                message.Id,
                message.ClientMessageId,
                message.ConversationId,
                message.SenderId,
                message.Ciphertext,
                message.Nonce,
                message.EncryptionVersion,
                message.CreatedAt
            }
        },
        CancellationToken.None);

    return Results.Created($"/api/v1/messages/{message.Id}", new
    {
        message.Id,
        message.ClientMessageId,
        message.ConversationId,
        message.SenderId,
        message.CreatedAt
    });
})
.RequireAuthorization()
.RequireRateLimiting("messages");

app.MapPost("/api/v1/servers", async (
    CreateServerRequest request,
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var ownerId))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 1 or > 100)
        return Results.BadRequest(new { error = "Server name must be 1-100 characters." });

    var now = DateTimeOffset.UtcNow;
    var server = new NovaServer
    {
        Id = Guid.NewGuid(),
        Name = request.Name.Trim(),
        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        OwnerId = ownerId,
        CreatedAt = now,
        UpdatedAt = now
    };

    var everyoneRole = new Role
    {
        Id = Guid.NewGuid(),
        ServerId = server.Id,
        Name = "@everyone",
        Permissions = (long)(ServerPermission.ViewChannels | ServerPermission.SendMessages |
                             ServerPermission.Connect | ServerPermission.Speak | ServerPermission.AddReactions |
                             ServerPermission.AttachFiles),
        Position = 0,
        IsDefault = true,
        CreatedAt = now
    };

    var member = new ServerMember
    {
        ServerId = server.Id,
        UserId = ownerId,
        JoinedAt = now
    };

    db.Servers.Add(server);
    db.Roles.Add(everyoneRole);
    db.ServerMembers.Add(member);
    db.ServerMemberRoles.Add(new ServerMemberRole
    {
        ServerId = server.Id,
        UserId = ownerId,
        RoleId = everyoneRole.Id
    });

    db.Channels.AddRange(
        new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            Name = "general",
            Type = ChannelType.Text,
            Position = 0,
            CreatedAt = now,
            UpdatedAt = now
        },
        new Channel
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            Name = "General Voice",
            Type = ChannelType.Voice,
            Position = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/servers/{server.Id}", new
    {
        server.Id,
        server.Name,
        server.Description
    });
}).RequireAuthorization();

app.MapGet("/api/v1/servers", async (
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var servers = await db.ServerMembers.AsNoTracking()
        .Where(x => x.UserId == userId)
        .OrderByDescending(x => x.JoinedAt)
        .Select(x => new
        {
            x.Server.Id,
            x.Server.Name,
            x.Server.Description,
            x.Server.IconUrl,
            x.Server.OwnerId,
            x.JoinedAt
        })
        .ToListAsync();

    return Results.Ok(servers);
}).RequireAuthorization();

app.MapGet("/api/v1/servers/{serverId:guid}", async (
    Guid serverId,
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var isMember = await db.ServerMembers.AnyAsync(x => x.ServerId == serverId && x.UserId == userId);
    if (!isMember)
        return Results.NotFound();

    var server = await db.Servers.AsNoTracking()
        .Where(x => x.Id == serverId)
        .Select(x => new
        {
            x.Id,
            x.Name,
            x.Description,
            x.IconUrl,
            x.OwnerId,
            x.CreatedAt,
            x.UpdatedAt,
            channels = x.Channels.OrderBy(c => c.Position).Select(c => new
            {
                c.Id,
                c.Name,
                c.Topic,
                c.Type,
                c.ParentChannelId,
                c.Position,
                c.IsPrivate
            }),
            roles = x.Roles.OrderByDescending(r => r.Position).Select(r => new
            {
                r.Id,
                r.Name,
                r.Permissions,
                r.Position,
                r.IsDefault
            })
        })
        .SingleOrDefaultAsync();

    return server is null ? Results.NotFound() : Results.Ok(server);
}).RequireAuthorization();

app.MapPost("/api/v1/servers/{serverId:guid}/channels", async (
    Guid serverId,
    CreateChannelRequest request,
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    if (!TryGetUserId(principal, out var userId))
        return Results.Unauthorized();

    var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == serverId);
    if (server is null)
        return Results.NotFound();

    if (server.OwnerId != userId)
        return Results.Forbid();

    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 1 or > 100)
        return Results.BadRequest(new { error = "Channel name must be 1-100 characters." });

    var channel = new Channel
    {
        Id = Guid.NewGuid(),
        ServerId = serverId,
        ParentChannelId = request.ParentChannelId,
        Name = request.Name.Trim(),
        Topic = string.IsNullOrWhiteSpace(request.Topic) ? null : request.Topic.Trim(),
        Type = request.Type,
        Position = await db.Channels.Where(x => x.ServerId == serverId).Select(x => (int?)x.Position).MaxAsync() + 1 ?? 0,
        IsPrivate = request.IsPrivate,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    db.Channels.Add(channel);
    server.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/channels/{channel.Id}", new
    {
        channel.Id,
        channel.ServerId,
        channel.Name,
        channel.Topic,
        channel.Type,
        channel.Position,
        channel.IsPrivate
    });
}).RequireAuthorization();

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var connections = context.RequestServices.GetRequiredService<ConnectionManager>();
    var db = context.RequestServices.GetRequiredService<NovaDbContext>();

    Guid? userId = null;

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveTextAsync(socket, context.RequestAborted);
            if (message is null)
                break;

            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (userId is null)
            {
                if (!root.TryGetProperty("type", out var type) ||
                    type.GetString() != "auth" ||
                    !root.TryGetProperty("accessToken", out var tokenElement))
                {
                    await SendAsync(socket, new { type = "error", code = "AUTH_REQUIRED" }, context.RequestAborted);
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication required", context.RequestAborted);
                    break;
                }

                var principal = TokenService.Validate(tokenElement.GetString()!, signingKey);
                var sub = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);

                if (!Guid.TryParse(sub, out var parsedUserId))
                {
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid token", context.RequestAborted);
                    break;
                }

                var deviceIdClaim = principal?.FindFirstValue("device_id");
                if (!Guid.TryParse(deviceIdClaim, out var deviceId))
                {
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Invalid device", context.RequestAborted);
                    break;
                }

                var deviceActive = await db.Devices.AnyAsync(
                    x => x.Id == deviceId && x.UserId == parsedUserId && x.RevokedAt == null,
                    context.RequestAborted);

                if (!deviceActive)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Device revoked", context.RequestAborted);
                    break;
                }

                userId = parsedUserId;
                connections.Add(userId.Value, socket);

                await db.Devices
                    .Where(x => x.Id == deviceId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastSeenAt, DateTimeOffset.UtcNow),
                        context.RequestAborted);

                await SendAsync(socket, new { type = "ready", userId }, context.RequestAborted);
                continue;
            }

            await SendAsync(socket, new
            {
                type = "ack",
                received = root.TryGetProperty("type", out var eventType)
                    ? eventType.GetString()
                    : null
            }, context.RequestAborted);
        }
    }
    catch (OperationCanceledException) { }
    catch (WebSocketException) { }
    catch (JsonException)
    {
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Invalid JSON", CancellationToken.None);
        }
        catch { }
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("too large", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
        }
        catch { }
    }
    finally
    {
        if (userId.HasValue)
            connections.Remove(userId.Value, socket);

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None); }
            catch { }
        }
    }
});

app.Run();

static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
{
    return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}

static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
{
    var buffer = new byte[16 * 1024];
    using var ms = new MemoryStream();

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);

        if (result.MessageType == WebSocketMessageType.Close)
            return null;

        if (result.MessageType != WebSocketMessageType.Text)
            throw new JsonException("Only text WebSocket messages are supported.");

        ms.Write(buffer, 0, result.Count);

        if (result.EndOfMessage)
            return Encoding.UTF8.GetString(ms.ToArray());

        if (ms.Length > 1024 * 1024)
            throw new InvalidOperationException("WebSocket message too large.");
    }
}

static Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
    return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
}

public record RegisterRequest(string Username, string Password, string? DisplayName);
public record LoginRequest(string Username, string Password, string? DeviceName, string? Platform);
public record RefreshRequest(string RefreshToken);
public record DeviceRequest(string Name, string Platform, string? PushToken, string? IdentityPublicKey, string? SignedPreKey, string? PreKeySignature);
public record CreateConversationRequest(ConversationType Type, List<Guid> UserIds, string? Name, string? IconUrl);
public record EncryptedMessageRequest(Guid ConversationId, string Ciphertext, string Nonce, int EncryptionVersion = 1, Guid? ClientMessageId = null);
public record CreateServerRequest(string Name, string? Description);
public record CreateChannelRequest(string Name, ChannelType Type, string? Topic, Guid? ParentChannelId, bool IsPrivate);
