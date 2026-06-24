using BareChat.Authorization;
using BareChat.Core;
using BareChat.Messaging;
using BareChat.Presence;
using BareChat.Storage;
using BareChat.Storage.Sqlite;
using Microsoft.AspNetCore.SignalR;
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
    /// <para><paramref name="configureSignalR"/> exposes the SignalR builder for scale-out wiring — e.g.
    /// <c>signalR =&gt; signalR.AddStackExchangeRedis(connection)</c>. The Redis package is the host's dependency;
    /// BareChat imposes none. For correct multi-node wake-up routing, also replace the default per-node
    /// <see cref="IPresenceTracker"/> with a distributed implementation (see its remarks).</para>
    /// </summary>
    public static IServiceCollection AddBareChat(
        this IServiceCollection services,
        Action<BareChatOptions>? configure = null,
        Action<ISignalRServerBuilder>? configureSignalR = null)
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
        services.AddSingleton<IPushSubscriptionStore>(sp => new SqlitePushSubscriptionStore(sp.GetRequiredService<SqliteConnectionFactory>()));

        services.AddSingleton<IPresenceTracker, InMemoryPresenceTracker>();
        // Visibility-aware default: public channels open to all, private channels gated by membership.
        services.AddSingleton<IChatAuthorizationProvider>(sp =>
            new ChannelMembershipAuthorizationProvider(sp.GetRequiredService<IChannelStore>()));
        services.AddSingleton<IChatAuthProvider, HttpUserChatAuthProvider>();

        services.AddSingleton<INotificationChannel, InAppChannel>();
        // Web Push wake-up channel: always registered, no-ops until a VAPID key pair is configured.
        services.AddSingleton<IWebPushSender, WebPushSender>();
        services.AddSingleton<IWakeUpNotificationChannel, WebPushChannel>();
        services.AddSingleton<IMessagePublisher, MessagePublisher>();
        services.AddSingleton<IMessageUpdateNotifier, MessageUpdateNotifier>();

        var signalR = services.AddSignalR()
            // Serialize enums as strings so live payloads match the REST DTOs (contentType "Text"/"Image"/
            // "System"), which the embedded UI compares by name.
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()));
        configureSignalR?.Invoke(signalR);   // host opts into a backplane (Redis, etc.) here

        services.AddHostedService<BareChatInitializer>();

        return services;
    }
}
