using BareChat.Authorization;
using BareChat.Core;
using BareChat.Messaging;
using BareChat.Presence;
using BareChat.Storage;
using BareChat.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BareChat.Extensions;

/// <summary>DI registration for BareChat.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers BareChat with zero-config defaults: SQLite file storage + filesystem blobs under
    /// <c>DataPath</c>, in-memory presence, allow-all authorization, host-user auth, SignalR.
    /// Options bind from the <c>BareChat</c> configuration section; <paramref name="configure"/> overrides.
    /// </summary>
    public static IServiceCollection AddBareChat(this IServiceCollection services, Action<BareChatOptions>? configure = null)
    {
        services.AddOptions<BareChatOptions>().BindConfiguration(BareChatOptions.SectionName);
        if (configure is not null)
            services.Configure(configure);

        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var options = sp.GetRequiredService<IOptions<BareChatOptions>>().Value;
            return DataPaths.Resolve(options.DataPath, env.ContentRootPath);
        });

        services.AddSingleton(sp => new SqliteConnectionFactory(sp.GetRequiredService<DataPaths>().DatabaseFile));

        services.AddSingleton<IChannelStore>(sp => new SqliteChannelStore(sp.GetRequiredService<SqliteConnectionFactory>()));
        services.AddSingleton<IChatStorageProvider>(sp => new SqliteChatStorageProvider(sp.GetRequiredService<SqliteConnectionFactory>()));
        services.AddSingleton<IBlobStore>(sp => new FileSystemBlobStore(sp.GetRequiredService<DataPaths>().BlobsDirectory));

        services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
        services.AddSingleton<IChatAuthorizationProvider, AllowAllAuthorizationProvider>();
        services.AddSingleton<IChatAuthProvider, HttpUserChatAuthProvider>();

        services.AddSingleton<INotificationChannel, InAppChannel>();
        services.AddSingleton<IMessagePublisher, MessagePublisher>();

        services.AddSignalR();
        services.AddHostedService<BareChatInitializer>();

        return services;
    }
}
