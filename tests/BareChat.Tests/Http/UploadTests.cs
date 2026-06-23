using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

public class UploadTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public UploadTests(ChatApp app) => _app = app;

    // 1x1 PNG
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent FileContent(byte[] bytes, string name, string contentType)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", name);
        return content;
    }

    [Fact]
    public async Task Upload_then_fetch_blob_round_trips()
    {
        var client = _app.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/chat/api/upload") { Content = FileContent(Png, "pixel.png", "image/png") };
        req.Headers.Add("Cookie", "bc_user=alice");
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var url = dto.GetProperty("url").GetString()!;
        Assert.StartsWith("/chat/api/blobs/", url);
        Assert.Equal("image/png", dto.GetProperty("contentType").GetString());

        var fetched = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await fetched.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_requires_authentication()
    {
        var client = _app.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/chat/api/upload") { Content = FileContent(Png, "pixel.png", "image/png") };
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Non_image_payload_is_rejected_even_with_image_content_type()
    {
        var client = _app.CreateClient();
        var html = System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        // Attacker lies about the type — server must sniff and reject.
        var req = new HttpRequestMessage(HttpMethod.Post, "/chat/api/upload") { Content = FileContent(html, "evil.png", "image/png") };
        req.Headers.Add("Cookie", "bc_user=mallory");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Spoofed_content_type_is_coerced_to_sniffed_type()
    {
        var client = _app.CreateClient();
        // Real PNG bytes but claims text/html — stored/served type must be the sniffed image/png.
        var req = new HttpRequestMessage(HttpMethod.Post, "/chat/api/upload") { Content = FileContent(Png, "x.html", "text/html") };
        req.Headers.Add("Cookie", "bc_user=alice");
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("image/png", dto.GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task Blob_serving_sets_hardening_headers()
    {
        var client = _app.CreateClient();
        var up = new HttpRequestMessage(HttpMethod.Post, "/chat/api/upload") { Content = FileContent(Png, "pixel.png", "image/png") };
        up.Headers.Add("Cookie", "bc_user=alice");
        var info = await (await client.SendAsync(up)).Content.ReadFromJsonAsync<JsonElement>();

        var resp = await client.GetAsync(info.GetProperty("url").GetString());
        resp.EnsureSuccessStatusCode();
        Assert.Equal("nosniff", string.Join("", resp.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("sandbox", string.Join("", resp.Headers.GetValues("Content-Security-Policy")));
    }
}
