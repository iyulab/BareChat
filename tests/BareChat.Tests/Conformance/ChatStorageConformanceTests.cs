using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Tests.Conformance;

/// <summary>Behavioral contract every <see cref="IChatStorageProvider"/> must satisfy.</summary>
public abstract class ChatStorageConformanceTests
{
    protected abstract IChatStorageProvider CreateStore();

    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ChatMessage Msg(string channel, string text, DateTime at) =>
        new() { ChannelId = channel, SenderId = "u", SenderName = "U", Payload = text, CreatedAtUtc = at };

    [Fact]
    public async Task Add_then_get_by_id_round_trips_fields()
    {
        var store = CreateStore();
        var msg = Msg("general", "hi", T0) with
        {
            ContentType = MessageType.System,
            Metadata = new Dictionary<string, string> { ["k"] = "v" }
        };
        var saved = await store.AddMessageAsync(msg);

        var got = await store.GetMessageAsync(saved.MessageId);

        Assert.NotNull(got);
        Assert.Equal("hi", got!.Payload);
        Assert.Equal(MessageType.System, got.ContentType);
        Assert.Equal("v", got.Metadata["k"]);
        Assert.Equal(T0, got.CreatedAtUtc);
    }

    [Fact]
    public async Task GetMessages_filters_by_channel_and_orders_oldest_first()
    {
        var store = CreateStore();
        await store.AddMessageAsync(Msg("general", "first", T0));
        await store.AddMessageAsync(Msg("general", "second", T0.AddMinutes(1)));
        await store.AddMessageAsync(Msg("other", "noise", T0.AddMinutes(2)));

        var page = await store.GetMessagesAsync("general");

        Assert.Equal(2, page.Count);
        Assert.Equal("first", page[0].Payload);
        Assert.Equal("second", page[1].Payload);
    }

    [Fact]
    public async Task GetMessages_respects_limit_keeping_newest()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++)
            await store.AddMessageAsync(Msg("general", $"m{i}", T0.AddMinutes(i)));

        var page = await store.GetMessagesAsync("general", limit: 2);

        Assert.Equal(2, page.Count);
        Assert.Equal("m3", page[0].Payload);
        Assert.Equal("m4", page[1].Payload);
    }

    [Fact]
    public async Task GetMessages_before_pages_older_history()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++)
            await store.AddMessageAsync(Msg("general", $"m{i}", T0.AddMinutes(i)));

        var page = await store.GetMessagesAsync("general", limit: 2, beforeUtc: T0.AddMinutes(3));

        Assert.Equal("m1", page[0].Payload);
        Assert.Equal("m2", page[1].Payload);
    }
}
