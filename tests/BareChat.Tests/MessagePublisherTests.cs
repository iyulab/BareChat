using System.Collections.Concurrent;
using BareChat.Core;
using BareChat.Core.Domain;
using BareChat.Messaging;
using BareChat.Presence;
using BareChat.Storage;
using Microsoft.Extensions.Logging.Abstractions;

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

    private sealed class CapturingWakeUp : IWakeUpNotificationChannel
    {
        public ConcurrentBag<string> Woke { get; } = new();
        public Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct = default)
        {
            Woke.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingWakeUp : IWakeUpNotificationChannel
    {
        public Task NotifyAsync(string userId, ChatMessage message, CancellationToken ct = default)
            => throw new InvalidOperationException("push service unreachable");
    }

    private static MessagePublisher Create(
        IChatStorageProvider storage, IChannelStore channels, IPresenceTracker presence,
        INotificationChannel live, params IWakeUpNotificationChannel[] wakeUp)
        => new(storage, channels, presence, live, wakeUp, NullLogger<MessagePublisher>.Instance);

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
        var publisher = Create(storage, channels, presence, notifier);

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

    [Fact]
    public async Task Offline_members_are_routed_to_wake_up_channels_online_to_live()
    {
        var (channels, presence) = await SetupAsync();
        var live = new CapturingNotifier();
        var wakeUp = new CapturingWakeUp();
        var publisher = Create(new InMemoryChatStorageProvider(), channels, presence, live, wakeUp);

        await publisher.PublishAsync(new ChatMessage { ChannelId = "general", SenderId = "online", Payload = "hi" });

        // online member → live only; offline member → wake-up only.
        Assert.Equal(new[] { "online" }, live.Notified.Select(n => n.UserId).ToArray());
        Assert.Equal(new[] { "offline" }, wakeUp.Woke.ToArray());
    }

    [Fact]
    public async Task A_failing_wake_up_channel_does_not_break_delivery_to_others()
    {
        var (channels, presence) = await SetupAsync();
        await channels.JoinAsync("general", "offline2");   // second offline member
        var live = new CapturingNotifier();
        var good = new CapturingWakeUp();
        // Throwing channel first: its failure must not stop the good channel or the next member.
        var publisher = Create(new InMemoryChatStorageProvider(), channels, presence, live, new ThrowingWakeUp(), good);

        await publisher.PublishAsync(new ChatMessage { ChannelId = "general", SenderId = "online", Payload = "hi" });

        // both offline members still reached the working channel despite the throwing one.
        Assert.Contains("offline", good.Woke);
        Assert.Contains("offline2", good.Woke);
    }
}
