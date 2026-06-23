namespace BareChat;

/// <summary>
/// Host-facing configuration. Bound from the <c>BareChat</c> configuration section; every value has a
/// working default so <c>AddBareChat()</c> with no arguments runs (zero-config).
/// </summary>
public sealed class BareChatOptions
{
    /// <summary>Configuration section name bound by <c>AddBareChat</c>.</summary>
    public const string SectionName = "BareChat";

    /// <summary>Path prefix the whole chat (Hub, REST, embedded UI) mounts under.</summary>
    public string RoutePrefix { get; set; } = "/chat";

    /// <summary>
    /// Directory holding <c>chat.db</c> and <c>blobs/</c>. Relative paths resolve against the host content root.
    /// Null/empty → <c>App_Data/barechat</c> under the content root.
    /// </summary>
    public string? DataPath { get; set; }

    /// <summary>Maximum accepted upload size in bytes. Default 3 MB.</summary>
    public long MaxImageSizeInBytes { get; set; } = 3 * 1024 * 1024;

    /// <summary>SignalR keep-alive interval.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Slug of the default channel (seeded, undeletable, auto-joined).</summary>
    public string DefaultChannelId { get; set; } = "general";

    /// <summary>Display name of the default channel.</summary>
    public string DefaultChannelName { get; set; } = "General";
}
