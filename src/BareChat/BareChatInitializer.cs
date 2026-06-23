using BareChat.Core;
using BareChat.Core.Domain;
using BareChat.Storage.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BareChat;

/// <summary>
/// Startup work: ensures the SQLite schema exists and seeds the default channel
/// (undeletable, auto-joined). Runs before the app serves requests.
/// </summary>
public sealed class BareChatInitializer : IHostedService
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IChannelStore _channels;
    private readonly BareChatOptions _options;

    public BareChatInitializer(SqliteConnectionFactory factory, IChannelStore channels, IOptions<BareChatOptions> options)
    {
        _factory = factory;
        _channels = channels;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _factory.Initialize();

        if (await _channels.GetChannelAsync(_options.DefaultChannelId, cancellationToken).ConfigureAwait(false) is null)
        {
            try
            {
                await _channels.CreateChannelAsync(new Channel
                {
                    ChannelId = _options.DefaultChannelId,
                    Name = _options.DefaultChannelName,
                    CreatedBy = "system",
                    IsDefault = true
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Concurrent startup already seeded it — fine.
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
