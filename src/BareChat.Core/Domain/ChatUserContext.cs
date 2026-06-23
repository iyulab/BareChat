namespace BareChat.Core.Domain;

/// <summary>
/// The resolved identity of a chat participant, projected from the host's authenticated user.
/// Infrastructure-free so any host (not only ASP.NET Core) can produce one.
/// </summary>
public record ChatUserContext
{
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public bool IsAuthenticated { get; init; }

    /// <summary>An unauthenticated/anonymous context.</summary>
    public static ChatUserContext Anonymous { get; } = new();
}
