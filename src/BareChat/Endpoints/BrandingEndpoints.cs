using System.Reflection;
using System.Text.Json;
using BareChat.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BareChat.Endpoints;

/// <summary>
/// Host-owned branding: a dynamic PWA manifest (name/colors from options) and icon serving that prefers
/// host files in <c>{DataPath}/branding</c> and falls back to an embedded default. No rebuild needed to rebrand.
/// </summary>
public static class BrandingEndpoints
{
    private static readonly Assembly Asm = typeof(BrandingEndpoints).Assembly;
    private static readonly string[] HostIcons = ["icon-192.png", "icon-512.png", "icon.svg", "favicon.ico"];

    public static void MapBranding(this IEndpointRouteBuilder app, string prefix)
    {
        // PWA manifest (install name + icons). Independent of the Service Worker (P2).
        app.MapGet($"{prefix}/manifest.webmanifest", (IOptions<BareChatOptions> opt, DataPaths paths) =>
        {
            var b = opt.Value.Branding;
            var icons = new List<object>
            {
                new { src = $"{prefix}/branding/icon.svg", sizes = "any", type = "image/svg+xml", purpose = "any" }
            };
            AddIfPresent(icons, paths, prefix, "icon-192.png", "192x192");
            AddIfPresent(icons, paths, prefix, "icon-512.png", "512x512");

            var manifest = new
            {
                name = b.AppName,
                short_name = b.EffectiveShortName,
                // Installed app boots into the PWA shell so it registers the SW and asks for notifications.
                start_url = (prefix.Length == 0 ? "/" : prefix + "/") + "?shell=pwa",
                scope = prefix.Length == 0 ? "/" : prefix + "/",
                display = "standalone",
                theme_color = b.ThemeColor,
                background_color = b.BackgroundColor,
                icons
            };
            return Results.Content(JsonSerializer.Serialize(manifest), "application/manifest+json");
        });

        // Branding asset: host override in {DataPath}/branding wins; otherwise embedded default (icon.svg only).
        app.MapGet($"{prefix}/branding/{{file}}", (string file, HttpContext http, DataPaths paths) =>
        {
            if (Array.IndexOf(HostIcons, file) < 0)
                return Results.NotFound();

            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var contentType = ContentTypeFor(file);

            var hostPath = Path.Combine(paths.Root, "branding", file);
            if (File.Exists(hostPath))
                return Results.File(File.OpenRead(hostPath), contentType);

            // Embedded default only exists for icon.svg.
            if (file == "icon.svg")
            {
                var stream = Asm.GetManifestResourceStream("BareChat.Assets.icon.svg");
                if (stream is not null) return Results.File(stream, contentType);
            }
            return Results.NotFound();
        });
    }

    private static void AddIfPresent(List<object> icons, DataPaths paths, string prefix, string file, string sizes)
    {
        if (File.Exists(Path.Combine(paths.Root, "branding", file)))
            icons.Add(new { src = $"{prefix}/branding/{file}", sizes, type = "image/png", purpose = "any maskable" });
    }

    private static string ContentTypeFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };
}
