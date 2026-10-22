using System.Buffers.Binary;

namespace CrosshairOverlay.Services.Import;

/// <summary>Compact, bounded share format for a 16/24/32-pixel ARGB drawing.</summary>
public static class PixelArtCode
{
    public const string Prefix = "PX1";
    public const int MaxCodeLength = 9000;

    public static bool IsSupportedSize(int size) => size is 16 or 24 or 32;

    public static string Encode(int size, ReadOnlySpan<uint> pixels)
    {
        if (!IsSupportedSize(size) || pixels.Length != size * size)
            throw new ArgumentException("Invalid pixel canvas dimensions.", nameof(pixels));

        // Each run is uint16 count + uint32 ARGB, little-endian. The worst case is 6 KiB.
        var bytes = new byte[pixels.Length * 6];
        var written = 0;
        for (var index = 0; index < pixels.Length;)
        {
            var color = pixels[index];
            var count = 1;
            while (index + count < pixels.Length && pixels[index + count] == color)
                count++;

            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(written, 2), (ushort)count);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(written + 2, 4), color);
            written += 6;
            index += count;
        }

        return $"{Prefix}|{size}|{Convert.ToBase64String(bytes, 0, written)}";
    }

    public static bool TryDecode(string? code, out int size, out uint[] pixels)
    {
        size = 0;
        pixels = [];
        if (string.IsNullOrWhiteSpace(code) || code.Length > MaxCodeLength) return false;

        var parts = code.Trim().Split('|');
        if (parts.Length != 3 || parts[0] != Prefix ||
            !int.TryParse(parts[1], out var parsedSize) || !IsSupportedSize(parsedSize))
            return false;

        byte[] bytes;
        try { bytes = Convert.FromBase64String(parts[2]); }
        catch (FormatException) { return false; }

        if (bytes.Length == 0 || bytes.Length % 6 != 0 || bytes.Length > parsedSize * parsedSize * 6)
            return false;

        var decoded = new uint[parsedSize * parsedSize];
        var index = 0;
        for (var offset = 0; offset < bytes.Length; offset += 6)
        {
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
            if (count == 0 || count > decoded.Length - index) return false;
            var color = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 2, 4));
            decoded.AsSpan(index, count).Fill(color);
            index += count;
        }

        if (index != decoded.Length) return false;
        size = parsedSize;
        pixels = decoded;
        return true;
    }
}
