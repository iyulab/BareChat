namespace BareChat.Endpoints;

/// <summary>
/// Detects a safe raster image type from magic bytes. The client-supplied Content-Type is never trusted:
/// allowing it would let an attacker upload <c>text/html</c> or <c>image/svg+xml</c> and have it served
/// same-origin as active content (stored XSS). Only PNG/JPEG/GIF/WebP are allowed; SVG/HTML/XML are rejected.
/// </summary>
public static class ImageContentTypes
{
    /// <summary>Returns the sniffed safe image MIME type, or null if the bytes are not an allowed image.</summary>
    public static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 6 &&
            bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' &&
            bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a')
            return "image/gif";

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return "image/webp";

        return null;
    }
}
