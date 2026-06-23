using BareChat.Core;
using BareChat.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BareChat.Tests;

public class AddBareChatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barechat-tests", Guid.NewGuid().ToString("N"));

    private ServiceProvider BuildProvider(Action<BareChatOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new StubEnvironment { ContentRootPath = _dir });
        services.AddBareChat(o =>
        {
            o.DataPath = Path.Combine(_dir, "data");
            configure?.Invoke(o);
        });
        return services.BuildServiceProvider();
    }

    private static async Task StartHostedServicesAsync(ServiceProvider sp)
    {
        foreach (var hosted in sp.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Seeds_default_channel_and_creates_db_file()
    {
        await using var sp = BuildProvider();
        await StartHostedServicesAsync(sp);

        var channels = sp.GetRequiredService<IChannelStore>();
        var def = await channels.GetChannelAsync("general");

        Assert.NotNull(def);
        Assert.True(def!.IsDefault);
        Assert.True(File.Exists(Path.Combine(_dir, "data", "chat.db")));
    }

    [Fact]
    public async Task Default_channel_id_is_configurable()
    {
        await using var sp = BuildProvider(o =>
        {
            o.DefaultChannelId = "lobby";
            o.DefaultChannelName = "Lobby";
        });
        await StartHostedServicesAsync(sp);

        var channels = sp.GetRequiredService<IChannelStore>();
        Assert.NotNull(await channels.GetChannelAsync("lobby"));
        Assert.Null(await channels.GetChannelAsync("general"));
    }

    [Fact]
    public async Task All_core_seams_are_registered()
    {
        await using var sp = BuildProvider();

        Assert.NotNull(sp.GetService<IChannelStore>());
        Assert.NotNull(sp.GetService<IChatStorageProvider>());
        Assert.NotNull(sp.GetService<IBlobStore>());
        Assert.NotNull(sp.GetService<IPresenceTracker>());
        Assert.NotNull(sp.GetService<IChatAuthorizationProvider>());
        Assert.NotNull(sp.GetService<IChatAuthProvider>());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "BareChat.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
