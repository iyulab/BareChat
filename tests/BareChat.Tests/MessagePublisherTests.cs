using System.Collections.Concurrent;
using BareChat.Core;
using BareChat.Core.Domain;
using BareChat.Messaging;
using BareChat.Presence;
using BareChat.Storage;

namespace BareChat.Tests;

public class MessagePublisherTests
{
    private sealed class CapturingNotifier : INotificationChannel
    {
        public ConcurrentBag<(string UserId, Guid MessageId)> Notified { get; } = new();
        public Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct = default)
        {
            Notified.Add((userId, message.MessageId));
            return Task.CompletedTask;
        }
    }

    private static async Task<(IChannelStore channels, IPresenceTracker presence)> SetupAsync()
    {
        var channels = new InMemoryChannelStore();
        await channels.CreateChannelAsync(new Channel { ChannelId = "general", Name = "General", CreatedBy = "system" });
        await channels.JoinAsync("general", "online");
        await channels.JoinAsync("general", "offline");
        var presence = new InMemoryPresenceTracker();
        await presence.SetOnlineAsync("online", "c1");
        return (channels, presence);
    }

    [Fact]
    public async Task Persists_message_and_live_delivers_only_to_online_members()
    {
        var (channels, presence) = await SetupAsync();
        var storage = new InMemoryChatStorageProvider();
        var notifier = new CapturingNotifier();
        var publisher = new MessagePublisher(storage, channels, presence, notifier);

        var saved = await publisher.PublishAsync(new ChatMessage
        {
            ChannelId = "general",
            SenderId = "online",
            Payload = "hi"
        });

        // persisted
        Assert.NotNull(await storage.GetMessageAsync(saved.MessageId));
        var inChannel = await storage.GetMessagesAsync("general");
        Assert.Single(inChannel);

        // delivered to online member only
        Assert.Single(notifier.Notified);
        Assert.Equal("online", notifier.Notified.Single().UserId);
        Assert.Equal(saved.MessageId, notifier.Notified.Single().MessageId);
    }
}
