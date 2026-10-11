namespace Umpk.Protocol.Bedrock;

// A single palette entry keeps its wire namespace apart from its value so a session runtime ID can never alias a persistent hash ID.
// Network palettes resolve as persistent hashes while stable table positions never map to session identifiers, so every palette-derived identity stays unknown until an explicit resolution table lands.
internal readonly record struct BedrockBlockStateIdentity(bool IsRuntime, int RawId, bool IsKnownAir, bool IsUnknown)
{
    internal static BedrockBlockStateIdentity FromPalette(bool isRuntime, int rawId) => new(isRuntime, rawId, false, true);
}
