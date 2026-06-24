using Microsoft.Data.Sqlite;

namespace BareChat.Storage.Sqlite;

/// <summary>
/// Owns the SQLite connection string for <c>chat.db</c> and creates the schema on demand.
/// One database holds messages, channels and memberships (shared by the SQLite stores).
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string dbFilePath)
    {
        var full = Path.GetFullPath(dbFilePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    /// <summary>Creates tables/indexes if absent and enables WAL. Idempotent.</summary>
    public void Initialize()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS channels (
                channel_id     TEXT PRIMARY KEY,
                name           TEXT NOT NULL,
                created_by     TEXT NOT NULL,
                is_default     INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS memberships (
                channel_id    TEXT NOT NULL,
                user_id       TEXT NOT NULL,
                joined_at_utc TEXT NOT NULL,
                PRIMARY KEY (channel_id, user_id)
            );

            CREATE TABLE IF NOT EXISTS messages (
                message_id        TEXT PRIMARY KEY,
                channel_id        TEXT NOT NULL,
                sender_id         TEXT NOT NULL,
                sender_name       TEXT NOT NULL,
                sender_avatar_url TEXT NOT NULL,
                content_type      INTEGER NOT NULL,
                payload           TEXT NOT NULL,
                created_at_utc    TEXT NOT NULL,
                is_deleted        INTEGER NOT NULL,
                edited_at_utc     TEXT NULL,
                metadata          TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_messages_channel_created
                ON messages (channel_id, created_at_utc);

            CREATE TABLE IF NOT EXISTS push_subscriptions (
                endpoint       TEXT PRIMARY KEY,
                p256dh         TEXT NOT NULL,
                auth           TEXT NOT NULL,
                user_id        TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_push_user ON push_subscriptions (user_id);
            """;
        cmd.ExecuteNonQuery();

        // Migrations (idempotent): SQLite has no "ADD COLUMN IF NOT EXISTS", so add only when absent.
        AddColumnIfMissing(c, "memberships", "last_read_at_utc", "TEXT NULL");
        AddColumnIfMissing(c, "channels", "is_private", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void AddColumnIfMissing(SqliteConnection c, string table, string column, string definition)
    {
        using (var info = c.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table})";
            using var r = info.ExecuteReader();
            while (r.Read())
            {
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return; // already present
            }
        }
        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
}
