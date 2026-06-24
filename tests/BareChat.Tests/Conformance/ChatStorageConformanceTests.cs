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

    // ---- count-since (cross-session unread) ----

    [Fact]
    public async Task CountMessagesSince_counts_strictly_after_baseline_in_channel()
    {
        var store = CreateStore();
        await store.AddMessageAsync(Msg("general", "old", T0));
        await store.AddMessageAsync(Msg("general", "new1", T0.AddMinutes(2)));
        await store.AddMessageAsync(Msg("general", "new2", T0.AddMinutes(3)));
        await store.AddMessageAsync(Msg("other", "elsewhere", T0.AddMinutes(4)));

        // baseline == T0+1m → "old" (at T0) excluded, both "new" included, other channel ignored
        Assert.Equal(2, await store.CountMessagesSinceAsync("general", T0.AddMinutes(1)));
    }

    [Fact]
    public async Task CountMessagesSince_excludes_own_messages()
    {
        var store = CreateStore();
        await store.AddMessageAsync(Msg("general", "fromOther", T0.AddMinutes(1)) with { SenderId = "other" });
        await store.AddMessageAsync(Msg("general", "fromMe", T0.AddMinutes(2)) with { SenderId = "me" });

        Assert.Equal(1, await store.CountMessagesSinceAsync("general", T0, excludeSenderId: "me"));
        Assert.Equal(2, await store.CountMessagesSinceAsync("general", T0));
    }

    [Fact]
    public async Task CountMessagesSince_ignores_deleted_messages()
    {
        var store = CreateStore();
        await store.AddMessageAsync(Msg("general", "live", T0.AddMinutes(1)));
        await store.AddMessageAsync(Msg("general", "gone", T0.AddMinutes(2)) with { IsDeleted = true });

        Assert.Equal(1, await store.CountMessagesSinceAsync("general", T0));
    }

    // ---- update (edit / soft-delete) ----

    [Fact]
    public async Task UpdateMessage_persists_mutable_fields()
    {
        var store = CreateStore();
        var saved = await store.AddMessageAsync(Msg("general", "typo", T0));
        var editedAt = T0.AddMinutes(5);

        var updated = await store.UpdateMessageAsync(saved with { Payload = "fixed", EditedAtUtc = editedAt });

        Assert.NotNull(updated);
        var got = await store.GetMessageAsync(saved.MessageId);
        Assert.Equal("fixed", got!.Payload);
        Assert.Equal(editedAt, got.EditedAtUtc);
        Assert.False(got.IsDeleted);
    }

    [Fact]
    public async Task UpdateMessage_can_soft_delete()
    {
        var store = CreateStore();
        var saved = await store.AddMessageAsync(Msg("general", "secret", T0));

        await store.UpdateMessageAsync(saved with { IsDeleted = true, Payload = "" });

        var got = await store.GetMessageAsync(saved.MessageId);
        Assert.True(got!.IsDeleted);
        Assert.Equal("", got.Payload);
    }

    [Fact]
    public async Task UpdateMessage_unknown_id_returns_null()
    {
        var store = CreateStore();
        var ghost = Msg("general", "nope", T0);   // never added
        Assert.Null(await store.UpdateMessageAsync(ghost));
    }
}
