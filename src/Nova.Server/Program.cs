using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Nova.Server.Data;
using Nova.Server.Models;
using Nova.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NovaDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NovaDatabase")
        ?? throw new InvalidOperationException("NovaDatabase connection string is missing.")));

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ConnectionManager>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is missing.");
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

app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NovaDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "nova-backend",
    version = "0.1.0"
}));

app.MapPost("/api/v1/auth/register", async (
    RegisterRequest request,
    NovaDbContext db,
    IPasswordHasher<User> hasher) =>
{
    var username = request.Username.Trim();

    if (username.Length is < 3 or > 32)
        return Results.BadRequest(new { error = "Username must be 3-32 characters." });

    if (request.Password.Length < 12)
        return Results.BadRequest(new { error = "Password must be at least 12 characters." });

    if (await db.Users.AnyAsync(x => x.Username.ToLower() == username.ToLower()))
        return Results.Conflict(new { error = "Username is already taken." });

    var user = new User
    {
        Id = Guid.NewGuid(),
        Username = username,
        DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? username
            : request.DisplayName.Trim(),
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
});

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

    return Results.Ok(new
    {
        accessToken = tokens.Create(user),
        tokenType = "Bearer",
        user = new { user.Id, user.Username, user.DisplayName }
    });
});

app.MapGet("/api/v1/me", (ClaimsPrincipal user) =>
{
    var id = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
             ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    return Results.Ok(new
    {
        id,
        username = user.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
    });
}).RequireAuthorization();

app.MapPost("/api/v1/messages", async (
    EncryptedMessageRequest request,
    ClaimsPrincipal principal,
    NovaDbContext db) =>
{
    var senderIdText = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!Guid.TryParse(senderIdText, out var senderId))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Ciphertext) ||
        string.IsNullOrWhiteSpace(request.Nonce))
        return Results.BadRequest(new { error = "Ciphertext and nonce are required." });

    var message = new EncryptedMessage
    {
        Id = Guid.NewGuid(),
        ConversationId = request.ConversationId,
        SenderId = senderId,
        Ciphertext = request.Ciphertext,
        Nonce = request.Nonce,
        EncryptionVersion = request.EncryptionVersion,
        CreatedAt = DateTimeOffset.UtcNow
    };

    db.Messages.Add(message);
    await db.SaveChangesAsync();

    return Results.Created($"/api/v1/messages/{message.Id}", new
    {
        message.Id,
        message.ConversationId,
        message.SenderId,
        message.CreatedAt
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

    Guid? userId = null;

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveTextAsync(socket, context.RequestAborted);
            if (message is null) break;

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

                userId = parsedUserId;
                connections.Add(userId.Value, socket);

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

static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
{
    var buffer = new byte[16 * 1024];
    using var ms = new MemoryStream();

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);

        if (result.MessageType == WebSocketMessageType.Close)
            return null;

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
public record LoginRequest(string Username, string Password);
public record EncryptedMessageRequest(Guid ConversationId, string Ciphertext, string Nonce, int EncryptionVersion = 1);
