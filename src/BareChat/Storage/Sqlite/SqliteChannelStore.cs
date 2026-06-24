using System.Globalization;
using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.Data.Sqlite;

namespace BareChat.Storage.Sqlite;

/// <summary>SQLite-backed <see cref="IChannelStore"/>. Shares <c>chat.db</c> via <see cref="SqliteConnectionFactory"/>.</summary>
public sealed class SqliteChannelStore : IChannelStore
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteChannelStore(SqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT channel_id, name, created_by, is_default, created_at_utc, is_private FROM channels ORDER BY created_at_utc";
        return await ReadChannelsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<Channel?> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT channel_id, name, created_by, is_default, created_at_utc, is_private FROM channels WHERE channel_id = $id";
        cmd.Parameters.AddWithValue("$id", channelId);
        var list = await ReadChannelsAsync(cmd, ct).ConfigureAwait(false);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<Channel> CreateChannelAsync(Channel channel, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO channels (channel_id, name, created_by, is_default, created_at_utc, is_private)
            VALUES ($id, $name, $by, $def, $at, $priv)
            """;
        cmd.Parameters.AddWithValue("$id", channel.ChannelId);
        cmd.Parameters.AddWithValue("$name", channel.Name);
        cmd.Parameters.AddWithValue("$by", channel.CreatedBy);
        cmd.Parameters.AddWithValue("$def", channel.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("$at", Iso(channel.CreatedAtUtc));
        cmd.Parameters.AddWithValue("$priv", channel.IsPrivate ? 1 : 0);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (unique slug)
        {
            throw new InvalidOperationException($"Channel '{channel.ChannelId}' already exists.", ex);
        }
        return channel;
    }

    public async Task DeleteChannelAsync(string channelId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM memberships WHERE channel_id = $id; DELETE FROM channels WHERE channel_id = $id;";
        cmd.Parameters.AddWithValue("$id", channelId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Channel>> GetUserChannelsAsync(string userId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT ch.channel_id, ch.name, ch.created_by, ch.is_default, ch.created_at_utc, ch.is_private
            FROM channels ch
            INNER JOIN memberships m ON m.channel_id = ch.channel_id
            WHERE m.user_id = $uid
            ORDER BY ch.created_at_utc
            """;
        cmd.Parameters.AddWithValue("$uid", userId);
        return await ReadChannelsAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetMembersAsync(string channelId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT user_id FROM memberships WHERE channel_id = $id";
        cmd.Parameters.AddWithValue("$id", channelId);
        var result = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result.Add(r.GetString(0));
        return result;
    }

    public async Task JoinAsync(string channelId, string userId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM channels WHERE channel_id = $id";
            check.Parameters.AddWithValue("$id", channelId);
            if (await check.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                throw new InvalidOperationException($"Channel '{channelId}' does not exist.");
        }
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memberships (channel_id, user_id, joined_at_utc)
            VALUES ($id, $uid, $at)
            ON CONFLICT (channel_id, user_id) DO NOTHING
            """;
        cmd.Parameters.AddWithValue("$id", channelId);
        cmd.Parameters.AddWithValue("$uid", userId);
        cmd.Parameters.AddWithValue("$at", Iso(DateTime.UtcNow));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task LeaveAsync(string channelId, string userId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM memberships WHERE channel_id = $id AND user_id = $uid";
        cmd.Parameters.AddWithValue("$id", channelId);
        cmd.Parameters.AddWithValue("$uid", userId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> IsMemberAsync(string channelId, string userId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM memberships WHERE channel_id = $id AND user_id = $uid";
        cmd.Parameters.AddWithValue("$id", channelId);
        cmd.Parameters.AddWithValue("$uid", userId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task SetLastReadAtAsync(string channelId, string userId, DateTime readAtUtc, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        // Updates an existing membership only — non-members have no read state to keep.
        cmd.CommandText = "UPDATE memberships SET last_read_at_utc = $at WHERE channel_id = $id AND user_id = $uid";
        cmd.Parameters.AddWithValue("$at", Iso(readAtUtc));
        cmd.Parameters.AddWithValue("$id", channelId);
        cmd.Parameters.AddWithValue("$uid", userId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, DateTime>> GetLastReadAtAsync(string userId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        // Effective baseline = last_read_at, falling back to join time when never explicitly read.
        cmd.CommandText = "SELECT channel_id, COALESCE(last_read_at_utc, joined_at_utc) FROM memberships WHERE user_id = $uid";
        cmd.Parameters.AddWithValue("$uid", userId);
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result[r.GetString(0)] = ParseIso(r.GetString(1));
        return result;
    }

    private static async Task<IReadOnlyList<Channel>> ReadChannelsAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var result = new List<Channel>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new Channel
            {
                ChannelId = r.GetString(0),
                Name = r.GetString(1),
                CreatedBy = r.GetString(2),
                IsDefault = r.GetInt64(3) != 0,
                CreatedAtUtc = ParseIso(r.GetString(4)),
                IsPrivate = r.GetInt64(5) != 0
            });
        }
        return result;
    }

    internal static string Iso(DateTime utc) =>
        utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    internal static DateTime ParseIso(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
