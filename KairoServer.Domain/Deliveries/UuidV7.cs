using System;
using System.Security.Cryptography;

namespace KairoServer.Domain.Deliveries
{
    /// <summary>
    /// Minimal UUID v7 generator (RFC 9562).
    /// Layout: 48-bit big-endian Unix millisecond timestamp, 4-bit version (0111),
    /// 12 bits of random, 2-bit variant (10), 62 bits of random.
    /// Produces time-ordered GUIDs that sort correctly under Postgres' <c>uuid</c>
    /// byte ordering, giving strong index locality for append-heavy tables.
    /// </summary>
    internal static class UuidV7
    {
        public static Guid Create(DateTimeOffset now)
        {
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);

            var unixMs = now.ToUnixTimeMilliseconds();
            if (unixMs < 0) unixMs = 0;

            bytes[0] = (byte)((unixMs >> 40) & 0xFF);
            bytes[1] = (byte)((unixMs >> 32) & 0xFF);
            bytes[2] = (byte)((unixMs >> 24) & 0xFF);
            bytes[3] = (byte)((unixMs >> 16) & 0xFF);
            bytes[4] = (byte)((unixMs >> 8) & 0xFF);
            bytes[5] = (byte)(unixMs & 0xFF);

            // Version 7 in the high nibble of byte 6.
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
            // RFC 4122 variant (10xx xxxx) in byte 8.
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            return new Guid(bytes, bigEndian: true);
        }
    }
}
