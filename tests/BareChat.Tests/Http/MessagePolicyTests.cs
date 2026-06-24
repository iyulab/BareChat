using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace BareChat.Tests.Http;

/// <summary>Host message-governance policy: editing/deletion can be disabled (Slack-style retention).</summary>
public class MessagePolicyTests
{
    private static HttpRequestMessage Req(HttpMethod method, string url, string user, object? body = null)
    {
        var r = new HttpRequestMessage(method, url);
        r.Headers.Add("Cookie", $"bc_user={user}");
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    /// <summary>Host with editing + deletion both turned off.</summary>
    private sealed class LockedDownApp : WebApplicationFactory<Program>
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "barechat-tests", Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("BareChat:DataPath", Path.Combine(_dir, "data"));
            builder.UseSetting("BareChat:Messages:AllowEditing", "false");
            builder.UseSetting("BareChat:Messages:AllowDeletion", "false");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }
        }
    }

    private static async Task<string> Publish(HttpClient client, string user)
    {
        var resp = await client.SendAsync(Req(HttpMethod.Post, "/chat/messages", user,
            new { channelId = "general", payload = "hi", contentType = "Text" }));
        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return dto.GetProperty("messageId").GetString()!;
    }

    [Fact]
    public async Task Default_capabilities_allow_edit_and_delete()
    {
        using var app = new ChatApp();
        var client = app.CreateClient();
        var resp = await client.SendAsync(Req(HttpMethod.Get, "/chat/api/capabilities", "alice"));
        resp.EnsureSuccessStatusCode();
        var caps = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(caps.GetProperty("canEditMessages").GetBoolean());
        Assert.True(caps.GetProperty("canDeleteMessages").GetBoolean());
    }

    [Fact]
    public async Task Capabilities_reflect_disabled_policy()
    {
        using var app = new LockedDownApp();
        var client = app.CreateClient();
        var caps = await (await client.SendAsync(Req(HttpMethod.Get, "/chat/api/capabilities", "alice")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(caps.GetProperty("canEditMessages").GetBoolean());
        Assert.False(caps.GetProperty("canDeleteMessages").GetBoolean());
    }

    [Fact]
    public async Task Editing_is_rejected_when_disabled()
    {
        using var app = new LockedDownApp();
        var client = app.CreateClient();
        var id = await Publish(client, "alice");

        var resp = await client.SendAsync(Req(HttpMethod.Put, $"/chat/api/messages/{id}", "alice", new { payload = "changed" }));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Deletion_is_rejected_when_disabled()
    {
        using var app = new LockedDownApp();
        var client = app.CreateClient();
        var id = await Publish(client, "alice");

        var resp = await client.SendAsync(Req(HttpMethod.Delete, $"/chat/api/messages/{id}", "alice"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
