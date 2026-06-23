using BareChat.Endpoints;
using BareChat.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BareChat.Extensions;

/// <summary>Middleware/endpoint mapping for BareChat.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Mounts the BareChat Hub, REST API and embedded UI under <c>RoutePrefix</c>.
    /// Place after <c>UseAuthentication()</c>/<c>UseAuthorization()</c> so the host user context is inherited.
    /// </summary>
    public static WebApplication UseBareChat(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<BareChatOptions>>().Value;
        var prefix = NormalizePrefix(options.RoutePrefix);

        app.MapGet($"{prefix}/healthz", () => Results.Ok(new { status = "ok" }));

        app.MapHub<ChatHub>($"{prefix}/hub");
        app.MapChatApi(prefix);
        app.MapPush(prefix);
        app.MapBranding(prefix);
        app.MapEmbeddedUi(prefix);

        return app;
    }

    /// <summary>
    /// Promotes a <c>?access_token=</c> query value to an <c>Authorization: Bearer</c> header for requests to
    /// the SignalR hub path. WebSocket handshakes can't carry an <c>Authorization</c> header, so cross-origin
    /// (WebView2/standalone) clients pass the token in the query string; this lifts it so the host's existing
    /// bearer-based authentication scheme authenticates the connection like any other request.
    /// <para>Scheme-agnostic and dependency-free (no JwtBearer package imposed). Opt-in: place this
    /// <b>before</b> <c>UseAuthentication()</c>. same-origin cookie clients don't need it.</para>
    /// </summary>
    public static IApplicationBuilder UseBareChatAccessToken(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<BareChatOptions>>().Value;
        var hubPath = NormalizePrefix(options.RoutePrefix) + "/hub";

        app.Use(async (context, next) =>
        {
            if (!context.Request.Headers.ContainsKey("Authorization") &&
                context.Request.Path.StartsWithSegments(hubPath, StringComparison.Ordinal))
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token))
                    context.Request.Headers.Authorization = $"Bearer {token}";
            }
            await next(context);
        });

        return app;
    }

    internal static string NormalizePrefix(string routePrefix)
    {
        if (string.IsNullOrWhiteSpace(routePrefix))
            return string.Empty;
        var p = "/" + routePrefix.Trim().Trim('/');
        return p == "/" ? string.Empty : p;
    }
}
