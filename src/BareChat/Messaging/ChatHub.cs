using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace BareChat.Messaging;

/// <summary>
/// Real-time endpoint. Tracks presence on connect/disconnect, auto-joins the default channel, and
/// relays send/join/leave. Live delivery flows through <see cref="IMessagePublisher"/>.
/// </summary>
public sealed class ChatHub : Hub
{
    private readonly IChatAuthProvider _auth;
    private readonly IChannelStore _channels;
    private readonly IChatAuthorizationProvider _authz;
    private readonly IPresenceTracker _presence;
    private readonly IMessagePublisher _publisher;
    private readonly BareChatOptions _options;

    public ChatHub(
        IChatAuthProvider auth,
        IChannelStore channels,
        IChatAuthorizationProvider authz,
        IPresenceTracker presence,
        IMessagePublisher publisher,
        IOptions<BareChatOptions> options)
    {
        _auth = auth;
        _channels = channels;
        _authz = authz;
        _presence = presence;
        _publisher = publisher;
        _options = options.Value;
    }

    public override async Task OnConnectedAsync()
    {
        var user = await ResolveUserAsync();
        if (user.IsAuthenticated)
        {
            await _presence.SetOnlineAsync(user.UserId, Context.ConnectionId);
            if (await _channels.GetChannelAsync(_options.DefaultChannelId) is not null)
                await _channels.JoinAsync(_options.DefaultChannelId, user.UserId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = await ResolveUserAsync();
        if (user.IsAuthenticated)
            await _presence.SetOfflineAsync(user.UserId, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SendMessage(string channelId, string payload)
    {
        var user = await RequireUserAsync();
        if (!await _authz.CanWriteAsync(user, channelId))
            throw new HubException("Not allowed to write to this channel.");
        if (await _channels.GetChannelAsync(channelId) is null)
            throw new HubException($"Channel '{channelId}' does not exist.");

        await _publisher.PublishAsync(new ChatMessage
        {
            ChannelId = channelId,
            SenderId = user.UserId,
            SenderName = user.DisplayName,
            SenderAvatarUrl = user.AvatarUrl,
            ContentType = MessageType.Text,
            Payload = payload
        }, Context.ConnectionAborted);
    }

    public async Task SendImage(string channelId, string url)
    {
        var user = await RequireUserAsync();
        if (!await _authz.CanWriteAsync(user, channelId))
            throw new HubException("Not allowed to write to this channel.");
        if (await _channels.GetChannelAsync(channelId) is null)
            throw new HubException($"Channel '{channelId}' does not exist.");

        await _publisher.PublishAsync(new ChatMessage
        {
            ChannelId = channelId,
            SenderId = user.UserId,
            SenderName = user.DisplayName,
            SenderAvatarUrl = user.AvatarUrl,
            ContentType = MessageType.Image,
            Payload = url
        }, Context.ConnectionAborted);
    }

    public async Task JoinChannel(string channelId)
    {
        var user = await RequireUserAsync();
        if (await _channels.GetChannelAsync(channelId) is null)
            throw new HubException($"Channel '{channelId}' does not exist.");
        await _channels.JoinAsync(channelId, user.UserId);
    }

    public async Task LeaveChannel(string channelId)
    {
        var user = await RequireUserAsync();
        var channel = await _channels.GetChannelAsync(channelId);
        if (channel is null)
            return;
        if (channel.IsDefault)
            throw new HubException("The default channel cannot be left.");
        await _channels.LeaveAsync(channelId, user.UserId);
    }

    private Task<ChatUserContext> ResolveUserAsync()
    {
        var http = Context.GetHttpContext();
        return http is null ? Task.FromResult(ChatUserContext.Anonymous) : _auth.ResolveUserAsync(http);
    }

    private async Task<ChatUserContext> RequireUserAsync()
    {
        var user = await ResolveUserAsync();
        if (!user.IsAuthenticated)
            throw new HubException("Not authenticated.");
        return user;
    }
}
