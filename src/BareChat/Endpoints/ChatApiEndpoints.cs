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
        api.MapGet("/channels", async (HttpContext http, IChatAuthProvider auth, IChannelStore channels, IChatStorageProvider storage) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();

            var all = await channels.GetChannelsAsync();
            var readBaselines = await channels.GetLastReadAtAsync(user.UserId);   // member channels only
            var unread = await storage.CountMessagesSinceAsync(readBaselines, excludeSenderId: user.UserId);  // one batched query
            var dtos = new List<ChannelDto>(all.Count);
            foreach (var c in all)
            {
                // Private channels are invisible to non-members (readBaselines keys == my memberships).
                if (c.IsPrivate && !readBaselines.ContainsKey(c.ChannelId)) continue;
                dtos.Add(ToDto(c, readBaselines, unread));
            }
            return Results.Ok(dtos);
        });

        api.MapGet("/channels/mine", async (HttpContext http, IChatAuthProvider auth, IChannelStore channels, IChatStorageProvider storage) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            var mine = await channels.GetUserChannelsAsync(user.UserId);
            var readBaselines = await channels.GetLastReadAtAsync(user.UserId);
            var unread = await storage.CountMessagesSinceAsync(readBaselines, excludeSenderId: user.UserId);  // one batched query
            var dtos = new List<ChannelDto>(mine.Count);
            foreach (var c in mine)
                dtos.Add(ToDto(c, readBaselines, unread));
            return Results.Ok(dtos);
        });

        // ---- read state (cross-session unread) ----
        api.MapPost("/channels/{id}/read", async (string id, HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            var channel = await channels.GetChannelAsync(id);
            if (channel is null || await HiddenFromUserAsync(channels, channel, user.UserId)) return Results.NotFound();
            await channels.SetLastReadAtAsync(id, user.UserId, DateTime.UtcNow);   // no-op for non-members
            return Results.NoContent();
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
                    IsDefault = false,
                    IsPrivate = req.IsPrivate ?? false
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
            var channel = await channels.GetChannelAsync(id);
            if (channel is null) return Results.NotFound();
            // Private channels can't be self-joined — the creator must add you (invite primitive below).
            // To a non-member the channel is invisible, so this reads as Not Found, not Forbidden.
            if (await HiddenFromUserAsync(channels, channel, user.UserId)) return Results.NotFound();
            await channels.JoinAsync(id, user.UserId);
            return Results.NoContent();
        });

        // Invite primitive: the creator adds a member (the only way into a private channel).
        api.MapPost("/channels/{id}/members", async (
            string id, HttpContext http, IChatAuthProvider auth, IChannelStore channels, AddMemberRequest req) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(req.UserId)) return Results.BadRequest(new { error = "userId is required." });
            var channel = await channels.GetChannelAsync(id);
            if (channel is null || await HiddenFromUserAsync(channels, channel, user.UserId)) return Results.NotFound();
            if (!string.Equals(channel.CreatedBy, user.UserId, StringComparison.Ordinal))
                return Results.Forbid();   // only the creator manages membership
            await channels.JoinAsync(id, req.UserId.Trim());
            return Results.NoContent();
        });

        api.MapPost("/channels/{id}/leave", async (string id, HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            var channel = await channels.GetChannelAsync(id);
            if (channel is null || await HiddenFromUserAsync(channels, channel, user.UserId)) return Results.NotFound();
            if (channel.IsDefault) return Results.BadRequest(new { error = "The default channel cannot be left." });
            await channels.LeaveAsync(id, user.UserId);
            return Results.NoContent();
        });

        api.MapDelete("/channels/{id}", async (string id, HttpContext http, IChatAuthProvider auth, IChannelStore channels) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            var channel = await channels.GetChannelAsync(id);
            if (channel is null || await HiddenFromUserAsync(channels, channel, user.UserId)) return Results.NotFound();
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
            var channel = await channels.GetChannelAsync(id);
            if (channel is null) return Results.NotFound();
            // Existence-hiding: a private channel a non-member can't read is reported Not Found, not
            // Forbidden — a 403 would leak that the (discovery-hidden) channel exists.
            if (!await authz.CanReadAsync(user, id))
                return channel.IsPrivate ? Results.NotFound() : Results.Forbid();

            var page = await storage.GetMessagesAsync(id, limit is > 0 and <= 200 ? limit.Value : 50, before);
            return Results.Ok(page.Select(MessageDto.From));
        });

        // ---- capabilities (UI feature-gating: edit/delete policy) ----
        api.MapGet("/capabilities", (IOptions<BareChatOptions> opt) =>
            Results.Ok(new
            {
                canEditMessages = opt.Value.Messages.AllowEditing,
                canDeleteMessages = opt.Value.Messages.AllowDeletion,
                canRenderMarkdown = opt.Value.Messages.AllowMarkdown
            }));

        // ---- message edit / delete (author-only, D6; host policy can disable) ----
        api.MapPut("/messages/{id:guid}", async (
            Guid id, HttpContext http, IChatAuthProvider auth,
            IChatStorageProvider storage, IMessageUpdateNotifier notifier, IOptions<BareChatOptions> opt, EditMessageRequest req) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (!opt.Value.Messages.AllowEditing) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(req.Payload)) return Results.BadRequest(new { error = "Payload is required." });

            var existing = await storage.GetMessageAsync(id);
            if (existing is null) return Results.NotFound();
            if (!string.Equals(existing.SenderId, user.UserId, StringComparison.Ordinal)) return Results.Forbid();
            if (existing.IsDeleted) return Results.BadRequest(new { error = "A deleted message cannot be edited." });
            if (existing.ContentType != MessageType.Text) return Results.BadRequest(new { error = "Only text messages can be edited." });

            var updated = await storage.UpdateMessageAsync(existing with { Payload = req.Payload, EditedAtUtc = DateTime.UtcNow });
            if (updated is null) return Results.NotFound();
            await notifier.NotifyUpdatedAsync(updated);
            return Results.Ok(MessageDto.From(updated));
        });

        api.MapDelete("/messages/{id:guid}", async (
            Guid id, HttpContext http, IChatAuthProvider auth,
            IChatStorageProvider storage, IMessageUpdateNotifier notifier, IOptions<BareChatOptions> opt) =>
        {
            var user = await auth.ResolveUserAsync(http);
            if (!user.IsAuthenticated) return Results.Unauthorized();
            if (!opt.Value.Messages.AllowDeletion) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var existing = await storage.GetMessageAsync(id);
            if (existing is null) return Results.NotFound();
            if (!string.Equals(existing.SenderId, user.UserId, StringComparison.Ordinal)) return Results.Forbid();

            // Soft delete: tombstone the row and clear the payload so the content is no longer retrievable.
            var updated = await storage.UpdateMessageAsync(existing with { IsDeleted = true, Payload = string.Empty });
            if (updated is null) return Results.NotFound();
            await notifier.NotifyUpdatedAsync(updated);
            return Results.Ok(MessageDto.From(updated));
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
            var channel = await channels.GetChannelAsync(req.ChannelId);
            if (channel is null) return Results.NotFound();
            // Existence-hiding (see GET messages): private + unauthorized → Not Found, not Forbidden.
            if (!await authz.CanWriteAsync(user, req.ChannelId))
                return channel.IsPrivate ? Results.NotFound() : Results.Forbid();

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

    /// <summary>
    /// Existence-hiding predicate: a private channel is invisible to non-members, so per-channel operations
    /// must report it as Not Found rather than Forbidden — a 403 would leak that the channel exists. Public
    /// channels are discoverable, so they are never hidden (the membership probe is skipped for them).
    /// </summary>
    private static async Task<bool> HiddenFromUserAsync(IChannelStore channels, Channel channel, string userId)
        => channel.IsPrivate && !await channels.IsMemberAsync(channel.ChannelId, userId);

    /// <summary>Maps a channel to its DTO, attaching the user's precomputed cross-session unread count when subscribed.</summary>
    private static ChannelDto ToDto(
        Channel c, IReadOnlyDictionary<string, DateTime> readBaselines, IReadOnlyDictionary<string, int> unreadByChannel)
    {
        // readBaselines contains only the user's subscribed channels → presence == membership.
        if (!readBaselines.ContainsKey(c.ChannelId))
            return ChannelDto.From(c, isMember: false);
        return ChannelDto.From(c, isMember: true, unreadByChannel.GetValueOrDefault(c.ChannelId));
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

public sealed record CreateChannelRequest(string? ChannelId, string Name, bool? IsPrivate = null);
public sealed record PublishRequest(string ChannelId, string? Payload, string? ContentType);
public sealed record EditMessageRequest(string Payload);
public sealed record AddMemberRequest(string UserId);

public sealed record ChannelDto(
    string Id, string Name, bool IsDefault, string CreatedBy, bool IsMember, int UnreadCount, bool IsPrivate)
{
    public static ChannelDto From(Channel c, bool isMember, int unreadCount = 0) =>
        new(c.ChannelId, c.Name, c.IsDefault, c.CreatedBy, isMember, unreadCount, c.IsPrivate);
}

public sealed record MessageDto(
    Guid MessageId, string ChannelId, string SenderId, string SenderName, string SenderAvatarUrl,
    string ContentType, string Payload, DateTime CreatedAtUtc, bool IsDeleted, DateTime? EditedAtUtc)
{
    public static MessageDto From(ChatMessage m) => new(
        m.MessageId, m.ChannelId, m.SenderId, m.SenderName, m.SenderAvatarUrl,
        m.ContentType.ToString(), m.Payload, m.CreatedAtUtc, m.IsDeleted, m.EditedAtUtc);
}
