namespace BareChat.Core;

/// <summary>
/// A "wake-up" transport tried for members who have no live connection (WebPush, NativeBridge).
/// Distinct from the always-on live channel (<see cref="INotificationChannel"/> = InAppChannel/SignalR)
/// so the publisher can route online members to live delivery and offline members to every registered
/// wake-up channel. Multiple wake-up channels can coexist; each is best-effort and isolated from the others.
/// </summary>
public interface IWakeUpNotificationChannel : INotificationChannel
{
}
