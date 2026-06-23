namespace BareChat.Core.Domain;

/// <summary>Metadata for a stored binary (e.g. an uploaded image). The bytes live in an <c>IBlobStore</c>; only this metadata is kept in the DB.</summary>
public record BlobInfo
{
    public string BlobId { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long Size { get; init; }
    public string? FileName { get; init; }
}
