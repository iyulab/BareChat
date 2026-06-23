using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Authorization;

/// <summary>
/// Default authorization for the public-only model: any authenticated user may read and write any channel.
/// Per-channel rules (private channels, roles) replace this via the <see cref="IChatAuthorizationProvider"/> seam in M3.
/// </summary>
public sealed class AllowAllAuthorizationProvider : IChatAuthorizationProvider
{
    public Task<bool> CanReadAsync(ChatUserContext user, string channelId, CancellationToken ct = default)
        => Task.FromResult(user.IsAuthenticated);

    public Task<bool> CanWriteAsync(ChatUserContext user, string channelId, CancellationToken ct = default)
        => Task.FromResult(user.IsAuthenticated);
}
