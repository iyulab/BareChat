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

        // The app shell: index.html with route prefix + branding injected. Routing matches both the bare
        // prefix and its trailing-slash form ({prefix}/ = the SW-controlled canonical PWA URL).
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

        // Service Worker (PWA profile). Served at {prefix}/sw.js → default scope {prefix}/ confines it
        // to the add-on. BASE is templated so precache URLs match the mount prefix.
        app.MapGet($"{prefix}/sw.js", () =>
        {
            var js = ReadText("sw.js")
                .Replace("{{BASE}}", prefix)
                .Replace("{{VERSION}}", AssetVersion());
            return Results.Content(js, "text/javascript; charset=utf-8");
        });
    }

    private static IResult Asset(string name, string contentType)
    {
        var bytes = ReadBytes(name);
        return bytes is null ? Results.NotFound() : Results.Bytes(bytes, contentType);
    }

    private static string? _assetVersion;

    /// <summary>
    /// Short content hash of the embedded shell assets. Injected into the SW so a rebuild that changes any
    /// asset produces a new cache name (auto cache-bust) — no manual version bump.
    /// </summary>
    private static string AssetVersion()
    {
        if (_assetVersion is not null) return _assetVersion;
        using var ms = new MemoryStream();
        foreach (var name in new[] { "index.html", "app.js", "app.css", "signalr.min.js", "sw.js" })
        {
            var bytes = ReadBytes(name);
            if (bytes is not null) ms.Write(bytes);
        }
        var hash = System.Security.Cryptography.SHA256.HashData(ms.ToArray());
        _assetVersion = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        return _assetVersion;
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
