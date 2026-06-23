using BareChat.Core;
using BareChat.Core.Domain;
using BareChat.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BareChat.Endpoints;

/// <summary>REST surface: channel CRUD, membership, message history and programmatic publish.</summary>
public static class ChatApiEndpoints
{
    public static void MapChatApi(this IEndpointRouteBuilder app, string prefix)
    {
        var api = app.MapGroup($"{prefix}/api");

        // ---- identity ----
        api.MapGet("/whoami", async (HttpContext http, IChatAuthProvider auth) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            return Results.Ok(new { userId = user.UserId, displayName = user.DisplayName, avatarUrl = user.AvatarUrl });
        });

        // ---- channels ----
        api.MapGet("/channels", async (HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();

            var all = await channels.GetChannelsAsync();
            var mine = (await channels.GetUserChannelsAsync(user.UserId)).Select(c => c.ChannelId).ToHashSet(StringComparer.Ordinal);
            return Results.Ok(all.Select(c => ChannelDto.From(c, mine.Contains(c.ChannelId))));
        });

        api.MapGet("/channels/mine", async (HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            var mine = await channels.GetUserChannelsAsync(user.UserId);
            return Results.Ok(mine.Select(c => ChannelDto.From(c, isMember: true)));
        });

        api.MapPost("/channels", async (HttpContext http, IChatAuthProvider auth, IChannelStore channels, CreateChannelRequest req) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name is required." });

            var slug = Slugify(string.IsNullOrWhiteSpace(req.ChannelId) ? req.Name : req.ChannelId);
            try
            {
                var created = await channels.CreateChannelAsync(new Channel
                {
                    ChannelId = slug,
                    Name = req.Name.Trim(),
                    CreatedBy = user.UserId,
                    IsDefault = false
                });
                await channels.JoinAsync(created.ChannelId, user.UserId); // creator auto-joins
                return Results.Created($"{prefix}/api/channels/{created.ChannelId}", ChannelDto.From(created, isMember: true));
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new { error = $"Channel '{slug}' already exists." });
            }
        });

        api.MapPost("/channels/{id}/join", async (string id, HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (await channels.GetChannelAsync(id) is null) return Results.NotFound();
            await channels.JoinAsync(id, user.UserId);
            return Results.NoContent();
        });

        api.MapPost("/channels/{id}/leave", async (string id, HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            var channel = await channels.GetChannelAsync(id);
            if (channel is null) return Results.NotFound();
            if (channel.IsDefault) return Results.BadRequest(new { error = "The default channel cannot be left." });
            await channels.LeaveAsync(id, user.UserId);
            return Results.NoContent();
        });

        api.MapDelete("/channels/{id}", async (string id, HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            var channel = await channels.GetChannelAsync(id);
            if (channel is null) return Results.NotFound();
            if (channel.IsDefault) return Results.BadRequest(new { error = "The default channel cannot be deleted." });
            if (!string.Equals(channel.CreatedBy, user.UserId, StringComparison.Ordinal))
                return Results.Forbid();
            await channels.DeleteChannelAsync(id);
            return Results.NoContent();
        });

        // ---- messages ----
        api.MapGet("/channels/{id}/messages", async (
            string id, int? limit, DateTime? before,
            HttpContext http, IChatAuthProvider auth, IChatAuthorizationProvider authz,
            IChannelStore channels, IChatStorageProvider storage) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (await channels.GetChannelAsync(id) is null) return Results.NotFound();
            if (!await authz.CanReadAsync(user, id)) return Results.Forbid();

            var page = await storage.GetMessagesAsync(id, limit is > 0 and <= 200 ? limit.Value : 50, before);
            return Results.Ok(page.Select(MessageDto.From));
        });

        // ---- blobs (images) ----
        api.MapPost("/upload", async (
            HttpContext http, IChatAuthProvider auth, IBlobStore blobs, IOptions<BareChatOptions> opt, IFormFile file) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "No file." });
            if (file.Length > opt.Value.MaxImageSizeInBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            // Never trust the client-supplied Content-Type: sniff magic bytes and allow only raster images.
            // (text/html or image/svg+xml served same-origin would be stored XSS.)
            using var buffer = new MemoryStream();
            await using (var raw = file.OpenReadStream())
                await raw.CopyToAsync(buffer);
            var sniffed = ImageContentTypes.Detect(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            if (sniffed is null)
                return Results.BadRequest(new { error = "Only PNG, JPEG, GIF or WebP images are allowed." });

            buffer.Position = 0;
            var info = await blobs.SaveAsync(buffer, sniffed, file.FileName);
            return Results.Ok(new { blobId = info.BlobId, url = $"{prefix}/api/blobs/{info.BlobId}", contentType = info.ContentType, size = info.Size });
        }).DisableAntiforgery();

        api.MapGet("/blobs/{id}", async (string id, HttpContext http, IBlobStore blobs) =>
        {
            var info = await blobs.GetInfoAsync(id);
            if (info is null) return Results.NotFound();
            var stream = await blobs.OpenReadAsync(id);
            if (stream is null) return Results.NotFound();

            // Defense-in-depth on serve: stored type is already an allowlisted raster image, but block
            // MIME sniffing and sandbox the response so a blob can never execute as active content.
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            http.Response.Headers["Content-Disposition"] = "inline";
            return Results.Stream(stream, info.ContentType);
        });

        // Programmatic publish (event feed). Default content type = System.
        app.MapPost($"{prefix}/messages", async (
            HttpContext http, IChatAuthProvider auth, IChatAuthorizationProvider authz,
            IChannelStore channels, IMessagePublisher publisher, PublishRequest req) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.ChannelId)) return Results.BadRequest(new { error = "channelId is required." });
            if (await channels.GetChannelAsync(req.ChannelId) is null) return Results.NotFound();
            if (!await authz.CanWriteAsync(user, req.ChannelId)) return Results.Forbid();

            var contentType = Enum.TryParse<MessageType>(req.ContentType, ignoreCase: true, out var ct) ? ct : MessageType.System;
            var saved = await publisher.PublishAsync(new ChatMessage
            {
                ChannelId = req.ChannelId,
                SenderId = user.UserId,
                SenderName = user.DisplayName,
                SenderAvatarUrl = user.AvatarUrl,
                ContentType = contentType,
                Payload = req.Payload ?? string.Empty
            });
            return Results.Created($"{prefix}/api/channels/{req.ChannelId}/messages", MessageDto.From(saved));
        });
    }

    internal static string Slugify(string input)
    {
        var lowered = input.Trim().ToLowerInvariant();
        var sb = new System.Text.StringBuilder(lowered.Length);
        foreach (var c in lowered)
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        var slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return slug.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : slug;
    }
}

public sealed record CreateChannelRequest(string? ChannelId, string Name);
public sealed record PublishRequest(string ChannelId, string? Payload, string? ContentType);

public sealed record ChannelDto(string Id, string Name, bool IsDefault, string CreatedBy, bool IsMember)
{
    public static ChannelDto From(Channel c, bool isMember) => new(c.ChannelId, c.Name, c.IsDefault, c.CreatedBy, isMember);
}

public sealed record MessageDto(
    Guid MessageId, string ChannelId, string SenderId, string SenderName, string SenderAvatarUrl,
    string ContentType, string Payload, DateTime CreatedAtUtc, bool IsDeleted, DateTime? EditedAtUtc)
{
    public static MessageDto From(ChatMessage m) => new(
        m.MessageId, m.ChannelId, m.SenderId, m.SenderName, m.SenderAvatarUrl,
        m.ContentType.ToString(), m.Payload, m.CreatedAtUtc, m.IsDeleted, m.EditedAtUtc);
}
