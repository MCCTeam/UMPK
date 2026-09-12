using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Umpk.Protocol.Java.Crypto;

/// <summary>The vanilla Minecraft "server id hash": a SHA-1 digest over the (empty) server id, the shared secret, and the server RSA public key, rendered as Java's <c>BigInteger.toString(16)</c> of the two's complement digest (a signed hex string, possibly with a leading minus). Both the online-mode client (before <c>hasJoined</c>) and the server (during <c>handleKey</c>) compute this identically; it is the join token the session service checks.</summary>
public static class MinecraftServerId
{
    /// <summary>Computes the server-id hash from the (usually empty) server id, the AES shared secret, and the DER-encoded server RSA public key.</summary>
    public static string Compute(string serverId, ReadOnlySpan<byte> sharedSecret, ReadOnlySpan<byte> publicKey)
    {
        ArgumentNullException.ThrowIfNull(serverId);

        byte[] serverIdBytes = Encoding.GetEncoding("iso-8859-1").GetBytes(serverId);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha1.AppendData(serverIdBytes);
        sha1.AppendData(sharedSecret);
        sha1.AppendData(publicKey);
        byte[] digest = sha1.GetHashAndReset();

        // Java's BigInteger treats the digest as a big-endian two's-complement integer.
        var value = new BigInteger(digest, isUnsigned: false, isBigEndian: true);
        return NormalizeHex(value);
    }

    private static string NormalizeHex(BigInteger value)
    {
        if (value.Sign == 0)
            return "0";

        bool negative = value.Sign < 0;
        BigInteger magnitude = negative ? -value : value;

        // BigInteger.ToString("x") zero-pads to a byte boundary and can emit a leading sign nibble;
        // The protocol format renders the bare magnitude with a leading minus and no leading zeros.
        string hex = magnitude.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');
        if (hex.Length == 0)
            hex = "0";

        return negative ? "-" + hex : hex;
    }
}
