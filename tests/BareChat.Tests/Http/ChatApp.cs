using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace BareChat.Tests.Http;

/// <summary>Boots the sample host with an isolated temp DataPath; cleans up on dispose.</summary>
public sealed class ChatApp : WebApplicationFactory<Program>
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "barechat-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.UseSetting("BareChat:DataPath", Path.Combine(DataDir, "data"));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(DataDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
