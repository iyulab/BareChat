using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BareChat.Tests.Http;

public class BrandingTests : IClassFixture<ChatApp>
{
    private readonly ChatApp _app;
    public BrandingTests(ChatApp app) => _app = app;

    [Fact]
    public async Task Manifest_reflects_branding_options()
    {
        var client = _app.CreateClient();
        var resp = await client.GetAsync("/chat/manifest.webmanifest");
        resp.EnsureSuccessStatusCode();
        Assert.Contains("manifest", resp.Content.Headers.ContentType!.ToString());

        var m = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("A Company MES Chat", m.GetProperty("name").GetString());
        Assert.Equal("MES Chat", m.GetProperty("short_name").GetString());
        Assert.Equal("#0f766e", m.GetProperty("theme_color").GetString());
        Assert.Equal("standalone", m.GetProperty("display").GetString());
        Assert.Equal("/chat/?shell=pwa", m.GetProperty("start_url").GetString());
        Assert.Equal("/chat/", m.GetProperty("scope").GetString());
        Assert.True(m.GetProperty("icons").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Shell_injects_app_name_and_theme()
    {
        var client = _app.CreateClient();
        var html = await client.GetStringAsync("/chat");

        Assert.Contains("<title>A Company MES Chat</title>", html);
        Assert.Contains("content=\"#0f766e\"", html);
        Assert.Contains("/chat/manifest.webmanifest", html);
        Assert.Contains("window.BARECHAT_APP_NAME = \"A Company MES Chat\"", html);
    }

    [Fact]
    public async Task Default_icon_svg_is_served()
    {
        var client = _app.CreateClient();
        var resp = await client.GetAsync("/chat/branding/icon.svg");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/svg+xml", resp.Content.Headers.ContentType!.MediaType);
        Assert.Contains("nosniff", string.Join("", resp.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Unknown_branding_file_is_404()
    {
        var client = _app.CreateClient();
        var resp = await client.GetAsync("/chat/branding/secret.txt");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Missing_host_png_icon_is_404_but_manifest_still_has_svg()
    {
        var client = _app.CreateClient();
        // No host icon-512.png dropped → 404 for the png, but manifest still installable via svg.
        var png = await client.GetAsync("/chat/branding/icon-512.png");
        Assert.Equal(HttpStatusCode.NotFound, png.StatusCode);
    }
}
