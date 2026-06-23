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

    /// <summary>Host-controlled branding (app name, colors). Icons are dropped into <c>{DataPath}/branding</c>.</summary>
    public BrandingOptions Branding { get; set; } = new();

    /// <summary>Web Push (VAPID) configuration. Push stays off until a key pair is supplied.</summary>
    public PushOptions Push { get; set; } = new();
}

/// <summary>
/// Web Push (VAPID, RFC 8292) settings. Supply a base64url-encoded P-256 key pair to enable background
/// wake-up notifications for the PWA. Generate once and keep the private key secret. Off when unset.
/// </summary>
public sealed class PushOptions
{
    /// <summary>VAPID public key (base64url, uncompressed P-256 point). Shared with clients to subscribe.</summary>
    public string? PublicKey { get; set; }

    /// <summary>VAPID private key (base64url). Secret — signs push requests.</summary>
    public string? PrivateKey { get; set; }

    /// <summary>VAPID subject: a <c>mailto:</c> or <c>https:</c> contact the push service can reach. Required by some services.</summary>
    public string Subject { get; set; } = "mailto:admin@example.com";

    /// <summary>True once a usable key pair is configured.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>
/// Branding the host owns without rebuilding the library. Name/colors are config; icons override via files
/// in <c>{DataPath}/branding</c> (<c>icon-192.png</c>, <c>icon-512.png</c>) and fall back to an embedded default.
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>In-app name and PWA install name. Shown in the UI header and document title.</summary>
    public string AppName { get; set; } = "BareChat";

    /// <summary>PWA short_name. Defaults to <see cref="AppName"/> when unset.</summary>
    public string? ShortName { get; set; }

    /// <summary>Theme/accent color (manifest theme_color, browser UI). CSS color.</summary>
    public string ThemeColor { get; set; } = "#111827";

    /// <summary>Background color used on the PWA splash (manifest background_color). CSS color.</summary>
    public string BackgroundColor { get; set; } = "#0b0f19";

    /// <summary>Effective PWA short name.</summary>
    public string EffectiveShortName => string.IsNullOrWhiteSpace(ShortName) ? AppName : ShortName;
}
