using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nova.Server.Data;
using Nova.Server.Models;
using Nova.Server.Services;

namespace Nova.Server;

public static class StorageEndpoints
{
    private const long MaxFileSize = 2L * 1024 * 1024 * 1024;

    public static void MapStorageEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/uploads").RequireAuthorization();

        api.MapPost("/", async (CreateUploadRequest request, ClaimsPrincipal principal, NovaDbContext db, IObjectStorage storage) =>
        {
            if (!TryUser(principal, out var userId))
                return Results.Unauthorized();

            if (request.TotalSize is <= 0 or > MaxFileSize)
                return Results.BadRequest(new { error = "File size must be between 1 byte and 2 GiB." });

            var fileName = Path.GetFileName(request.FileName ?? "upload.bin");
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 512)
                return Results.BadRequest(new { error = "Invalid file name." });

            var id = Guid.NewGuid();
            var key = $"{userId:N}/{id:N}.bin";
            await storage.CreateAsync(key);

            var session = new UploadSession
            {
                Id = id,
                OwnerId = userId,
                FileName = fileName,
                ContentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType[..Math.Min(request.ContentType.Length, 256)],
                TotalSize = request.TotalSize,
                StorageKey = key,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            db.Set<UploadSession>().Add(session);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/uploads/{id}", new { id, session.FileName, session.ContentType, session.TotalSize, offset = 0L });
        });

        api.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, NovaDbContext db) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();

            var session = await db.Set<UploadSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId);
            return session is null
                ? Results.NotFound()
                : Results.Ok(new { session.Id, session.FileName, session.ContentType, session.TotalSize, offset = session.ReceivedSize, session.Completed });
        });

        api.MapPut("/{id:guid}", async (Guid id, HttpRequest request, ClaimsPrincipal principal, NovaDbContext db, IObjectStorage storage, CancellationToken ct) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();

            var session = await db.Set<UploadSession>().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId, ct);
            if (session is null) return Results.NotFound();
            if (session.Completed) return Results.Conflict(new { error = "Upload is already complete." });

            var requestedOffset = request.Headers["X-Upload-Offset"].FirstOrDefault();
            if (!long.TryParse(requestedOffset, out var offset) || offset != session.ReceivedSize)
                return Results.Conflict(new { error = "Upload offset mismatch.", offset = session.ReceivedSize });

            if (request.ContentLength is null or < 0 || request.ContentLength > session.TotalSize - offset)
                return Results.BadRequest(new { error = "Chunk exceeds the remaining upload size." });

            var newOffset = await storage.AppendAsync(session.StorageKey, offset, request.Body, ct);
            session.ReceivedSize = newOffset;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { id = session.Id, offset = session.ReceivedSize, complete = session.ReceivedSize == session.TotalSize });
        });

        api.MapPost("/{id:guid}/complete", async (Guid id, CompleteUploadRequest request, ClaimsPrincipal principal, NovaDbContext db) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();

            var session = await db.Set<UploadSession>().SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId);
            if (session is null) return Results.NotFound();
            if (session.ReceivedSize != session.TotalSize)
                return Results.Conflict(new { error = "Upload is incomplete.", offset = session.ReceivedSize, totalSize = session.TotalSize });

            session.Completed = true;
            session.UpdatedAt = DateTimeOffset.UtcNow;

            var attachment = new Attachment
            {
                Id = Guid.NewGuid(),
                OwnerId = userId,
                MessageId = request.MessageId,
                FileName = session.FileName,
                ContentType = session.ContentType,
                Size = session.TotalSize,
                StorageKey = session.StorageKey,
                StorageProvider = "local",
                EncryptionVersion = request.EncryptionVersion ?? "none",
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Attachments.Add(attachment);
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/attachments/{attachment.Id}", new
            {
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.Size,
                attachment.StorageProvider,
                attachment.EncryptionVersion
            });
        });

        app.MapGet("/api/v1/attachments/{id:guid}", async (Guid id, ClaimsPrincipal principal, NovaDbContext db, IObjectStorage storage) =>
        {
            if (!TryUser(principal, out var userId)) return Results.Unauthorized();

            var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (attachment is null) return Results.NotFound();

            var allowed = attachment.OwnerId == userId ||
                (attachment.MessageId.HasValue && await db.Messages.AnyAsync(m => m.Id == attachment.MessageId.Value &&
                    db.ConversationMembers.Any(cm => cm.ConversationId == m.ConversationId && cm.UserId == userId)));

            if (!allowed) return Results.NotFound();

            var stream = await storage.OpenReadAsync(attachment.StorageKey);
            return Results.Stream(stream, attachment.ContentType, attachment.FileName, enableRangeProcessing: true);
        }).RequireAuthorization();
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}

public record CreateUploadRequest(string FileName, string ContentType, long TotalSize);
public record CompleteUploadRequest(Guid? MessageId, string? EncryptionVersion);
