using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace BareChat.Endpoints;

/// <summary>Serves the embedded mobile UI (index.html, app.js, app.css, signalr.min.js) from the assembly.</summary>
public static class EmbeddedUi
{
    private static readonly Assembly Asm = typeof(EmbeddedUi).Assembly;
    private const string ResourcePrefix = "BareChat.Assets.";

    public static void MapEmbeddedUi(this IEndpointRouteBuilder app, string prefix)
    {
        var shellPath = prefix.Length == 0 ? "/" : prefix;

        // The app shell: index.html with route prefix + branding injected.
        app.MapGet(shellPath, (IOptions<BareChatOptions> opt) =>
        {
            var b = opt.Value.Branding;
            var html = ReadText("index.html")
                .Replace("{{BASE}}", prefix)
                .Replace("{{APP_NAME}}", WebUtility.HtmlEncode(b.AppName))
                .Replace("{{THEME_COLOR}}", WebUtility.HtmlEncode(b.ThemeColor));
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet($"{prefix}/app.js", () => Asset("app.js", "text/javascript; charset=utf-8"));
        app.MapGet($"{prefix}/app.css", () => Asset("app.css", "text/css; charset=utf-8"));
        app.MapGet($"{prefix}/signalr.min.js", () => Asset("signalr.min.js", "text/javascript; charset=utf-8"));
    }

    private static IResult Asset(string name, string contentType)
    {
        var bytes = ReadBytes(name);
        return bytes is null ? Results.NotFound() : Results.Bytes(bytes, contentType);
    }

    private static Stream Open(string name)
        => Asm.GetManifestResourceStream(ResourcePrefix + name)
           ?? throw new InvalidOperationException($"Embedded asset '{name}' not found.");

    private static string ReadText(string name)
    {
        using var reader = new StreamReader(Open(name));
        return reader.ReadToEnd();
    }

    private static byte[]? ReadBytes(string name)
    {
        using var s = Asm.GetManifestResourceStream(ResourcePrefix + name);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
