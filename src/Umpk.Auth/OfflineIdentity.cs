using System.Security.Cryptography;
using System.Text;

namespace Umpk.Auth;

/// <summary>Computes vanilla-deterministic offline identities. The UUID matches Java's <c>UUID.nameUUIDFromBytes("OfflinePlayer:&lt;name&gt;".getBytes(UTF_8))</c>: an MD5 (version 3) name-based UUID with the version and variant bits set per RFC 4122. This is the identity vanilla servers assign offline players, so it is stable across reconnects and matches the ecosystem.</summary>
public static class OfflineIdentity
{
    /// <summary>Returns the deterministic offline <see cref="Guid"/> for a player name.</summary>
    public static Guid ComputeUuid(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        byte[] input = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);
        byte[] hash = MD5.HashData(input);

        // Java UUID.nameUUIDFromBytes: version 3, RFC 4122 variant.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        return GuidFromBigEndianBytes(hash);
    }

    /// <summary>Returns the offline <see cref="GameProfile"/> (deterministic UUID plus the name).</summary>
    public static GameProfile ComputeProfile(string username) => new(ComputeUuid(username), username);

    private static Guid GuidFromBigEndianBytes(byte[] bytes)
    {
        // The 16 bytes are a big-endian UUID (Java layout). .NET's Guid(byte[]) reads the first three fields little-endian, so build the Guid from explicit big-endian fields to match Java exactly.
        int a = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
        short b = (short)((bytes[4] << 8) | bytes[5]);
        short c = (short)((bytes[6] << 8) | bytes[7]);
        return new Guid(a, b, c, bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]);
    }
}
