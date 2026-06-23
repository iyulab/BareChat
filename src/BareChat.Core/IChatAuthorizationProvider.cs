using BareChat.Core.Domain;

namespace BareChat.Core;

/// <summary>
/// Decides which channels a user may read/write. Default implementation allows all authenticated users
/// (public-only model); this seam exists so deployments can enforce per-channel rules later (M3).
/// </summary>
public interface IChatAuthorizationProvider
{
    Task<bool> CanReadAsync(ChatUserContext user, string channelId, CancellationToken ct = default);
    Task<bool> CanWriteAsync(ChatUserContext user, string channelId, CancellationToken ct = default);
}
