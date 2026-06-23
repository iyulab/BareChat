using BareChat.Core;
using BareChat.Storage;
using BareChat.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace BareChat.Tests.Conformance;

/// <summary>Owns a throwaway SQLite database file for one test instance (xUnit news up one instance per test).</summary>
public sealed class SqliteTestDb : IDisposable
{
    public string Dir { get; }
    public SqliteConnectionFactory Factory { get; }

    public SqliteTestDb()
    {
        Dir = Path.Combine(Path.GetTempPath(), "barechat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Factory = new SqliteConnectionFactory(Path.Combine(Dir, "chat.db"));
        Factory.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, recursive: true); } catch { /* best-effort */ }
    }
}

public class SqliteChannelStoreTests : ChannelStoreConformanceTests, IDisposable
{
    private readonly SqliteTestDb _db = new();
    protected override IChannelStore CreateStore() => new SqliteChannelStore(_db.Factory);
    public void Dispose() => _db.Dispose();
}

public class SqliteChatStorageTests : ChatStorageConformanceTests, IDisposable
{
    private readonly SqliteTestDb _db = new();
    protected override IChatStorageProvider CreateStore() => new SqliteChatStorageProvider(_db.Factory);
    public void Dispose() => _db.Dispose();
}

public class FileSystemBlobStoreTests : BlobStoreConformanceTests, IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barechat-tests", Guid.NewGuid().ToString("N"));
    protected override IBlobStore CreateStore() => new FileSystemBlobStore(_dir);
    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }
}
