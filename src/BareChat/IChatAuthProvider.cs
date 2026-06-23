using BareChat.Core.Domain;
using Microsoft.AspNetCore.Http;

namespace BareChat;

/// <summary>
/// Resolves "who is this" from the host request. Lives in the ASP.NET Core package (not Core) because it
/// is coupled to <see cref="HttpContext"/>; keeping it out of Core preserves Core's zero-infrastructure rule.
/// Default implementation projects the host's <c>context.User</c>.
/// </summary>
public interface IChatAuthProvider
{
    Task<ChatUserContext> ResolveUserAsync(HttpContext context);
}
