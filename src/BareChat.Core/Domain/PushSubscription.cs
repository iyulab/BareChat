namespace BareChat.Core.Domain;

/// <summary>
/// A browser Web Push subscription (RFC 8030/8291) owned by a user. <see cref="Endpoint"/> is the unique
/// push-service URL and primary key; <see cref="P256dh"/> + <see cref="Auth"/> are the client public keys
/// used to encrypt push payloads. A user may hold several (one per device/browser).
/// </summary>
public sealed class PushSubscription
{
    /// <summary>Push-service endpoint URL. Unique per subscription (primary key).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Client P-256 ECDH public key (base64url), from the subscription's <c>keys.p256dh</c>.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Client auth secret (base64url), from the subscription's <c>keys.auth</c>.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>The subscribing user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>When the subscription was first stored (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
