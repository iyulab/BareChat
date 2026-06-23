using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Storage.Sqlite;

/// <summary>SQLite-backed <see cref="IPushSubscriptionStore"/>. Shares <c>chat.db</c> via <see cref="SqliteConnectionFactory"/>.</summary>
public sealed class SqlitePushSubscriptionStore : IPushSubscriptionStore
{
    private readonly SqliteConnectionFactory _factory;

    public SqlitePushSubscriptionStore(SqliteConnectionFactory factory) => _factory = factory;

    public async Task SaveAsync(PushSubscription subscription, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO push_subscriptions (endpoint, p256dh, auth, user_id, created_at_utc)
            VALUES ($e, $p, $a, $u, $at)
            ON CONFLICT (endpoint) DO UPDATE SET
                p256dh = excluded.p256dh, auth = excluded.auth, user_id = excluded.user_id
            """;
        cmd.Parameters.AddWithValue("$e", subscription.Endpoint);
        cmd.Parameters.AddWithValue("$p", subscription.P256dh);
        cmd.Parameters.AddWithValue("$a", subscription.Auth);
        cmd.Parameters.AddWithValue("$u", subscription.UserId);
        cmd.Parameters.AddWithValue("$at", SqliteChannelStore.Iso(subscription.CreatedAtUtc));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string endpoint, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM push_subscriptions WHERE endpoint = $e";
        cmd.Parameters.AddWithValue("$e", endpoint);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<PushSubscription>> GetByUserAsync(string userId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT endpoint, p256dh, auth, user_id, created_at_utc FROM push_subscriptions WHERE user_id = $u";
        cmd.Parameters.AddWithValue("$u", userId);

        var result = new List<PushSubscription>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new PushSubscription
            {
                Endpoint = r.GetString(0),
                P256dh = r.GetString(1),
                Auth = r.GetString(2),
                UserId = r.GetString(3),
                CreatedAtUtc = SqliteChannelStore.ParseIso(r.GetString(4))
            });
        }
        return result;
    }
}
