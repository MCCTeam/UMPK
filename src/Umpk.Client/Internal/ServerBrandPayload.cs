using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Client.Internal;

/// <summary>
/// Reads the server brand out of a plugin-channel message. The brand travels on a custom payload in one of three era shapes, all with the same body (a single length-prefixed UTF-8 string) but different channels and different phases:
/// <list type="bullet">
/// <item>1.8-1.12.2: play phase, channel <c>MC|Brand</c>. This is a raw legacy channel name, not a namespaced
/// identifier, so it cannot route through the identifier-keyed plugin-channel table.</item>
/// <item>1.13-1.20.1: play phase, channel <c>minecraft:brand</c>.</item>
/// <item>1.20.2+: configuration phase only, channel <c>minecraft:brand</c>;
/// the send is gone from <c>PlayerList</c> on these versions, so a play-only reader never sees a brand on any modern server.</item>
/// </list>
/// </summary>
internal static class ServerBrandPayload
{
    /// <summary>The 1.13+ channel.</summary>
    public static readonly Identifier Channel = Identifier.Minecraft("brand");

    /// <summary>The 1.8-1.12.2 channel, which is not a valid namespaced identifier.</summary>
    private const string LegacyChannel = "MC|Brand";

    /// <summary>Reads a play-phase <c>custom_payload</c> frame body (channel string followed by the payload) and returns the brand when the channel is a brand channel of either era.</summary>
    public static bool TryReadFromPlayFrame(ReadOnlySpan<byte> frameBody, out string brand)
    {
        brand = string.Empty;
        try
        {
            var reader = new PacketReader(frameBody);
            string channel = reader.ReadString();
            if (!IsBrandChannel(channel))
                return false;

            brand = reader.ReadString();
            return true;
        }
        catch (Exception ex) when (ex is ProtocolViolationException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>Reads the brand out of a decoded custom-payload body (the channel is matched by the caller).</summary>
    public static bool TryReadBody(ReadOnlySpan<byte> body, out string brand)
    {
        brand = string.Empty;
        try
        {
            var reader = new PacketReader(body);
            brand = reader.ReadString();
            return true;
        }
        catch (Exception ex) when (ex is ProtocolViolationException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>True for either the modern namespaced channel or the 1.8-1.12.2 legacy name.</summary>
    private static bool IsBrandChannel(string channel) =>
        string.Equals(channel, LegacyChannel, StringComparison.Ordinal)
        || (Identifier.TryParse(channel, out Identifier id) && id == Channel);
}
