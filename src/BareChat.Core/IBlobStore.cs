using BareChat.Core.Domain;

namespace BareChat.Core;

/// <summary>Binary storage for images and other attachments. The DB keeps only <see cref="BlobInfo"/>; never BLOB-in-DB.</summary>
public interface IBlobStore
{
    Task<BlobInfo> SaveAsync(Stream content, string contentType, string? fileName = null, CancellationToken ct = default);

    /// <summary>Opens the blob for reading, or null if it does not exist. Caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(string blobId, CancellationToken ct = default);

    Task<BlobInfo?> GetInfoAsync(string blobId, CancellationToken ct = default);
}
