using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

/// <summary>A player profile property (name, value, optional Mojang signature), part of the modern game profile carried in login-finished.</summary>
public sealed record GameProfileProperty(string Name, string Value, string? Signature);

/// <summary>A player's profile public key as it appears on the wire: the key validity expiry (epoch milliseconds), the DER <c>SubjectPublicKeyInfo</c> key bytes, and Mojang's signature over the key. This is the "ProfilePublicKey.Data" carried by the 1.19/1.19.1 login-start "has sig data" field and by the 1.19.3+ <c>chat_session_update</c> packet. The signature is the v1 signature on 1.19 login and the v2 signature on 1.19.1 login and on every <c>chat_session_update</c>; the caller decides which signature bytes to supply, so the codec stays era-agnostic.</summary>
/// <param name="ExpiresAtMillis">The key expiry as Unix epoch milliseconds.</param>
/// <param name="KeyDer">The DER <c>SubjectPublicKeyInfo</c> RSA public key bytes.</param>
/// <param name="KeySignature">Mojang's signature over the key (v1 or v2, chosen by the caller).</param>
public sealed record ProfilePublicKeyData(long ExpiresAtMillis, byte[] KeyDer, byte[] KeySignature);

/// <summary>The signed-challenge payload a 1.19/1.19.1 (protocols 759/760) encryption response carries in place of a plain verify token, as it appears on the wire: a client-chosen salt and the SHA256withRSA signature (produced with the profile PRIVATE key) over the server's verify-token bytes followed by the salt's 8-byte big-endian encoding. Verified server-side against the profile PUBLIC key already presented at login-start.</summary>
/// <param name="Salt">The client-chosen salt, signed alongside the verify token.</param>
/// <param name="Signature">The SHA256withRSA signature over verify-token-bytes || salt-bytes(big-endian).</param>
public sealed record SignedChallengeData(long Salt, byte[] Signature);
