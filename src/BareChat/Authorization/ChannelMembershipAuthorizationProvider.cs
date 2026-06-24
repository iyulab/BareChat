using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Authorization;

/// <summary>
/// Default authorization once private channels exist: public channels stay open to any authenticated user,
/// while private channels are gated by membership (read and write). An unknown channel is denied. This is
/// the <see cref="IChatAuthorizationProvider"/> seam activated — hosts can still substitute their own.
/// </summary>
public sealed class ChannelMembershipAuthorizationProvider : IChatAuthorizationProvider
{
    private readonly IChannelStore _channels;

    public ChannelMembershipAuthorizationProvider(IChannelStore channels) => _channels = channels;

    public Task<bool> CanReadAsync(ChatUserContext user, string channelId, CancellationToken ct = default)
        => AllowedAsync(user, channelId, ct);

    public Task<bool> CanWriteAsync(ChatUserContext user, string channelId, CancellationToken ct = default)
        => AllowedAsync(user, channelId, ct);

    private async Task<bool> AllowedAsync(ChatUserContext user, string channelId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return false;

        var channel = await _channels.GetChannelAsync(channelId, ct).ConfigureAwait(false);
        if (channel is null) return false;          // can't access a channel that doesn't exist
        if (!channel.IsPrivate) return true;        // public: any authenticated user

        return await _channels.IsMemberAsync(channelId, user.UserId, ct).ConfigureAwait(false);
    }
}
