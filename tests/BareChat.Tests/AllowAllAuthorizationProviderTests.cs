using BareChat.Authorization;
using BareChat.Core.Domain;

namespace BareChat.Tests;

public class AllowAllAuthorizationProviderTests
{
    [Fact]
    public async Task Authenticated_user_can_read_and_write_any_channel()
    {
        var authz = new AllowAllAuthorizationProvider();
        var user = new ChatUserContext { UserId = "alice", IsAuthenticated = true };

        Assert.True(await authz.CanReadAsync(user, "anything"));
        Assert.True(await authz.CanWriteAsync(user, "anything"));
    }

    [Fact]
    public async Task Anonymous_user_is_denied()
    {
        var authz = new AllowAllAuthorizationProvider();

        Assert.False(await authz.CanReadAsync(ChatUserContext.Anonymous, "general"));
        Assert.False(await authz.CanWriteAsync(ChatUserContext.Anonymous, "general"));
    }
}
