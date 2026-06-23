using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BareChat.Core.Domain;
using Microsoft.AspNetCore.SignalR.Client;

namespace BareChat.Client;

/// <summary>
/// Thin .NET client for a BareChat host: real-time send/receive over the SignalR hub plus REST publish for
/// programmatic System messages (event feed). Lets a backend process (device service, build agent, …)
/// participate in channels without a browser.
/// </summary>
public sealed class BareChatClient : IAsyncDisposable
{
    // REST DTO is camelCase with ContentType as a string ("System") → web defaults + string enum converter.
    private static readonly JsonSerializerOptions RestJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HubConnection _hub;
    private readonly HttpClient _http;
    private readonly string _prefix;
    private readonly Func<string?>? _token;

    /// <summary>Raised for every message delivered to a subscribed channel (live <c>ReceiveMessage</c>).</summary>
    public event Action<ChatMessage>? MessageReceived;

    public BareChatClient(BareChatClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.BaseAddress);

        _prefix = NormalizePrefix(options.RoutePrefix);
        _token = options.AccessTokenProvider;

        var hubUrl = options.BaseAddress.ToString().TrimEnd('/') + _prefix + "/hub";
        var builder = new HubConnectionBuilder().WithUrl(hubUrl, connection =>
        {
            if (_token is not null)
                connection.AccessTokenProvider = () => Task.FromResult(_token());
            options.ConfigureConnection?.Invoke(connection);
        });
        if (options.AutomaticReconnect)
            builder.WithAutomaticReconnect();

        _hub = builder.Build();
        _hub.On<ChatMessage>("ReceiveMessage", message => MessageReceived?.Invoke(message));

        _http = new HttpClient(options.RestMessageHandler ?? new HttpClientHandler(), disposeHandler: options.RestMessageHandler is null)
        {
            BaseAddress = options.BaseAddress
        };
    }

    /// <summary>Current hub connection state.</summary>
    public HubConnectionState State => _hub.State;

    /// <summary>Opens the hub connection. The host auto-joins the default channel on connect.</summary>
    public Task ConnectAsync(CancellationToken ct = default) => _hub.StartAsync(ct);

    /// <summary>Joins (subscribes to) a channel.</summary>
    public Task SubscribeAsync(string channelId, CancellationToken ct = default) =>
        _hub.InvokeAsync("JoinChannel", channelId, ct);

    /// <summary>Leaves a channel.</summary>
    public Task UnsubscribeAsync(string channelId, CancellationToken ct = default) =>
        _hub.InvokeAsync("LeaveChannel", channelId, ct);

    /// <summary>Sends a text message to a channel over the live hub.</summary>
    public Task SendTextAsync(string channelId, string text, CancellationToken ct = default) =>
        _hub.InvokeAsync("SendMessage", channelId, text, ct);

    /// <summary>Sends an image message (URL of a previously uploaded blob) to a channel.</summary>
    public Task SendImageAsync(string channelId, string url, CancellationToken ct = default) =>
        _hub.InvokeAsync("SendImage", channelId, url, ct);

    /// <summary>
    /// Publishes a message via REST (<c>POST {prefix}/messages</c>). Works without a live socket — the intended
    /// path for background event feeds (System messages by default). Returns the persisted message.
    /// </summary>
    public async Task<ChatMessage?> PublishAsync(
        string channelId, string payload, MessageType contentType = MessageType.System, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_prefix}/messages")
        {
            Content = JsonContent.Create(new { channelId, payload, contentType = contentType.ToString() })
        };
        var token = _token?.Invoke();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new("Bearer", token);

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatMessage>(RestJson, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync().ConfigureAwait(false);
        _http.Dispose();
    }

    private static string NormalizePrefix(string routePrefix)
    {
        if (string.IsNullOrWhiteSpace(routePrefix)) return string.Empty;
        var p = "/" + routePrefix.Trim().Trim('/');
        return p == "/" ? string.Empty : p;
    }
}
