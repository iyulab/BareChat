using System.Security.Claims;
using BareChat.Core.Domain;
using Microsoft.AspNetCore.Http;

namespace BareChat.Authorization;

/// <summary>
/// Default <see cref="IChatAuthProvider"/>: projects the host's authenticated <c>context.User</c> into a
/// <see cref="ChatUserContext"/>. Unauthenticated requests resolve to <see cref="ChatUserContext.Anonymous"/>.
/// </summary>
public sealed class HttpUserChatAuthProvider : IChatAuthProvider
{
    public Task<ChatUserContext> ResolveUserAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
            return Task.FromResult(ChatUserContext.Anonymous);

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.Identity.Name
                     ?? "unknown";
        var displayName = user.Identity.Name ?? userId;
        var avatar = user.FindFirst("avatar")?.Value ?? string.Empty;

        return Task.FromResult(new ChatUserContext
        {
            UserId = userId,
            DisplayName = displayName,
            AvatarUrl = avatar,
            IsAuthenticated = true
        });
    }
}
