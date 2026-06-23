using BareChat.Extensions;
using BareChat.Sample;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Sample-only dev authentication (see DevAuth.cs). Real hosts plug in their own scheme.
builder.Services
    .AddAuthentication(DevAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddBareChat(options =>
{
    // DataPath defaults to App_Data/barechat under the content root (zero-config).
    options.RoutePrefix = "/chat";
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseBareChat();   // after auth so the host user context is inherited

// Dev login: pick an identity, then go to the chat.
app.MapGet("/dev-login", (HttpContext ctx, string user) =>
{
    ctx.Response.Cookies.Append(DevAuthHandler.CookieName, user);
    return Results.Redirect("/chat");
});

app.MapGet("/", () => Results.Redirect("/dev-login?user=guest"));

app.Run();

// Exposed so WebApplicationFactory<Program> can boot this host in integration tests.
public partial class Program { }
