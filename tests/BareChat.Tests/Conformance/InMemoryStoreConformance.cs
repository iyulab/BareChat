using BareChat.Core;
using BareChat.Storage;

namespace BareChat.Tests.Conformance;

public class InMemoryChannelStoreConformance : ChannelStoreConformanceTests
{
    protected override IChannelStore CreateStore() => new InMemoryChannelStore();
}

public class InMemoryChatStorageConformance : ChatStorageConformanceTests
{
    protected override IChatStorageProvider CreateStore() => new InMemoryChatStorageProvider();
}

public class InMemoryBlobStoreConformance : BlobStoreConformanceTests
{
    protected override IBlobStore CreateStore() => new InMemoryBlobStore();
}

public class InMemoryPushSubscriptionStoreConformance : PushSubscriptionStoreConformanceTests
{
    protected override IPushSubscriptionStore CreateStore() => new InMemoryPushSubscriptionStore();
}
