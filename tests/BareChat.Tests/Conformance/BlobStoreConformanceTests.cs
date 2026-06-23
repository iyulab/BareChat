using System.Text;
using BareChat.Core;

namespace BareChat.Tests.Conformance;

/// <summary>Behavioral contract every <see cref="IBlobStore"/> must satisfy.</summary>
public abstract class BlobStoreConformanceTests
{
    protected abstract IBlobStore CreateStore();

    [Fact]
    public async Task Save_then_read_round_trips_bytes_and_metadata()
    {
        var store = CreateStore();
        var bytes = Encoding.UTF8.GetBytes("hello blob");
        using var input = new MemoryStream(bytes);

        var info = await store.SaveAsync(input, "text/plain", "note.txt");

        Assert.Equal("text/plain", info.ContentType);
        Assert.Equal(bytes.LongLength, info.Size);
        Assert.Equal("note.txt", info.FileName);
        Assert.False(string.IsNullOrEmpty(info.BlobId));

        await using var read = await store.OpenReadAsync(info.BlobId);
        Assert.NotNull(read);
        using var sr = new StreamReader(read!);
        Assert.Equal("hello blob", await sr.ReadToEndAsync());

        var fetched = await store.GetInfoAsync(info.BlobId);
        Assert.Equal(info.BlobId, fetched!.BlobId);
    }

    [Fact]
    public async Task Unknown_blob_returns_null()
    {
        var store = CreateStore();
        Assert.Null(await store.OpenReadAsync("missing"));
        Assert.Null(await store.GetInfoAsync("missing"));
    }
}
