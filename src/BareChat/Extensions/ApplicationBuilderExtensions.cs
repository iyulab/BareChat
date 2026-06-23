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
        app.MapEmbeddedUi(prefix);

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
