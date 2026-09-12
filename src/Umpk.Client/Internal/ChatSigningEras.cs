using Umpk.Protocol.Java.Signing;

namespace Umpk.Client.Internal;

/// <summary>Maps the per-version <c>ProtocolFeatures.ChatSigning</c> generation string (<c>none</c>/<c>v1</c>/<c>v2</c>/<c>v3</c>) onto the signing payload era the session builds. Keeping the mapping in one place lets the client derive the era off the negotiated version instead of comparing protocol numbers.</summary>
internal static class ChatSigningEras
{
    /// <summary>Resolves the signature era for a version's <paramref name="chatSigningFeature"/>. Returns false for <c>none</c> (or any unknown value), meaning the version has no chat signing and no provider is wired.</summary>
    public static bool TryFromFeature(string chatSigningFeature, out ChatSignatureEra era)
    {
        switch (chatSigningFeature)
        {
            case "v1":
                era = ChatSignatureEra.V1_19;
                return true;
            case "v2":
                era = ChatSignatureEra.V1_19_1;
                return true;
            case "v3":
                era = ChatSignatureEra.V1_19_3;
                return true;
            default:
                era = default;
                return false;
        }
    }
}
