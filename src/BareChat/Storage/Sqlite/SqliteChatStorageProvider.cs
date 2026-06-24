using System.Text.Json;
using BareChat.Core;
using BareChat.Core.Domain;
using Microsoft.Data.Sqlite;

namespace BareChat.Storage.Sqlite;

/// <summary>SQLite-backed <see cref="IChatStorageProvider"/>. Shares <c>chat.db</c> via <see cref="SqliteConnectionFactory"/>.</summary>
public sealed class SqliteChatStorageProvider : IChatStorageProvider
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteChatStorageProvider(SqliteConnectionFactory factory) => _factory = factory;

    public async Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO messages
              (message_id, channel_id, sender_id, sender_name, sender_avatar_url,
               content_type, payload, created_at_utc, is_deleted, edited_at_utc, metadata)
            VALUES
              ($id, $cid, $sid, $sname, $savatar, $ctype, $payload, $created, $deleted, $edited, $meta)
            """;
        cmd.Parameters.AddWithValue("$id", message.MessageId.ToString());
        cmd.Parameters.AddWithValue("$cid", message.ChannelId);
        cmd.Parameters.AddWithValue("$sid", message.SenderId);
        cmd.Parameters.AddWithValue("$sname", message.SenderName);
        cmd.Parameters.AddWithValue("$savatar", message.SenderAvatarUrl);
        cmd.Parameters.AddWithValue("$ctype", (int)message.ContentType);
        cmd.Parameters.AddWithValue("$payload", message.Payload);
        cmd.Parameters.AddWithValue("$created", SqliteChannelStore.Iso(message.CreatedAtUtc));
        cmd.Parameters.AddWithValue("$deleted", message.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$edited", (object?)(message.EditedAtUtc is { } e ? SqliteChannelStore.Iso(e) : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$meta", JsonSerializer.Serialize(message.Metadata));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return message;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        string channelId, int limit = 50, DateTime? beforeUtc = null, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        // newest `limit` (optionally older than $before), returned oldest-first for display
        cmd.CommandText = """
            SELECT message_id, channel_id, sender_id, sender_name, sender_avatar_url,
                   content_type, payload, created_at_utc, is_deleted, edited_at_utc, metadata
            FROM (
                SELECT * FROM messages
                WHERE channel_id = $cid
                  AND ($before IS NULL OR created_at_utc < $before)
                ORDER BY created_at_utc DESC
                LIMIT $limit
            )
            ORDER BY created_at_utc ASC
            """;
        cmd.Parameters.AddWithValue("$cid", channelId);
        cmd.Parameters.AddWithValue("$before", (object?)(beforeUtc is { } b ? SqliteChannelStore.Iso(b) : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", limit);
        return await ReadMessagesAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<ChatMessage?> GetMessageAsync(Guid messageId, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT message_id, channel_id, sender_id, sender_name, sender_avatar_url,
                   content_type, payload, created_at_utc, is_deleted, edited_at_utc, metadata
            FROM messages WHERE message_id = $id
            """;
        cmd.Parameters.AddWithValue("$id", messageId.ToString());
        var list = await ReadMessagesAsync(cmd, ct).ConfigureAwait(false);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<ChatMessage?> UpdateMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        // Only mutable fields change; sender/channel/created stay put.
        cmd.CommandText = """
            UPDATE messages
            SET payload = $payload, is_deleted = $deleted, edited_at_utc = $edited
            WHERE message_id = $id
            """;
        cmd.Parameters.AddWithValue("$payload", message.Payload);
        cmd.Parameters.AddWithValue("$deleted", message.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$edited", (object?)(message.EditedAtUtc is { } e ? SqliteChannelStore.Iso(e) : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", message.MessageId.ToString());
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return rows == 0 ? null : message;
    }

    public async Task<int> CountMessagesSinceAsync(
        string channelId, DateTime afterUtc, string? excludeSenderId = null, CancellationToken ct = default)
    {
        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM messages
            WHERE channel_id = $cid
              AND created_at_utc > $after
              AND is_deleted = 0
              AND ($exclude IS NULL OR sender_id <> $exclude)
            """;
        cmd.Parameters.AddWithValue("$cid", channelId);
        cmd.Parameters.AddWithValue("$after", SqliteChannelStore.Iso(afterUtc));
        cmd.Parameters.AddWithValue("$exclude", (object?)excludeSenderId ?? DBNull.Value);
        var count = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One-query batch count: the per-channel baselines are supplied as an inline <c>VALUES</c> table and
    /// left-joined to messages, so a user's whole channel list resolves in a single round-trip (no N+1).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> CountMessagesSinceAsync(
        IReadOnlyDictionary<string, DateTime> baselinesByChannel,
        string? excludeSenderId = null,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, int>(baselinesByChannel.Count);
        if (baselinesByChannel.Count == 0) return result;
        foreach (var channelId in baselinesByChannel.Keys) result[channelId] = 0;   // ensure every channel is present

        await using var c = _factory.Open();
        await using var cmd = c.CreateCommand();

        // Build "(SELECT $c0 AS channel_id, $s0 AS since UNION ALL SELECT $c1, $s1 ...)" as the baseline table.
        var rows = new List<string>(baselinesByChannel.Count);
        var i = 0;
        foreach (var (channelId, afterUtc) in baselinesByChannel)
        {
            rows.Add($"SELECT $c{i} AS channel_id, $s{i} AS since");
            cmd.Parameters.AddWithValue($"$c{i}", channelId);
            cmd.Parameters.AddWithValue($"$s{i}", SqliteChannelStore.Iso(afterUtc));
            i++;
        }
        cmd.CommandText = $"""
            SELECT b.channel_id, COUNT(m.message_id)
            FROM ({string.Join(" UNION ALL ", rows)}) AS b
            LEFT JOIN messages m
              ON m.channel_id = b.channel_id
              AND m.created_at_utc > b.since
              AND m.is_deleted = 0
              AND ($exclude IS NULL OR m.sender_id <> $exclude)
            GROUP BY b.channel_id
            """;
        cmd.Parameters.AddWithValue("$exclude", (object?)excludeSenderId ?? DBNull.Value);

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            result[r.GetString(0)] = Convert.ToInt32(r.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }

    private static async Task<IReadOnlyList<ChatMessage>> ReadMessagesAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var result = new List<ChatMessage>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new ChatMessage
            {
                MessageId = Guid.Parse(r.GetString(0)),
                ChannelId = r.GetString(1),
                SenderId = r.GetString(2),
                SenderName = r.GetString(3),
                SenderAvatarUrl = r.GetString(4),
                ContentType = (MessageType)r.GetInt64(5),
                Payload = r.GetString(6),
                CreatedAtUtc = SqliteChannelStore.ParseIso(r.GetString(7)),
                IsDeleted = r.GetInt64(8) != 0,
                EditedAtUtc = r.IsDBNull(9) ? null : SqliteChannelStore.ParseIso(r.GetString(9)),
                Metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(10)) ?? new()
            });
        }
        return result;
    }
}
