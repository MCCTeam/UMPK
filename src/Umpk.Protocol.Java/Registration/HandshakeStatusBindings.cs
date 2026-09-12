using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>Handshake and status (server-list ping) packet timelines.</summary>
internal static class HandshakeStatusBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        HandshakeCodecs.DeclareIntention(bindings);
        StatusCodecs.DeclarePingRequestStatus(bindings);
        StatusCodecs.DeclarePongResponseStatus(bindings);
        StatusCodecs.DeclareStatusRequest(bindings);
        StatusCodecs.DeclareStatusResponse(bindings);
    }
}
