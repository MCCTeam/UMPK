using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;

namespace Umpk.IntegrationTests;

/// <summary>Drives a hermetic server through login and across the first clientbound Play-frame boundary. <see cref="JavaServerLogin.AcceptAsync"/> changes the connection codec to Play, but deliberately does not synthesize a Play packet. A whole <see cref="Umpk.Client.UmpkClient"/> waits for that first packet before reporting a ready Play session, matching a vanilla server's JoinGame boundary.</summary>
internal static class HermeticPlayServer
{
    public static async Task<ServerLoginResult> AcceptAndAnnouncePlayAsync(
        JavaConnection server,
        JavaVersion version,
        JavaServerLoginOptions options,
        CancellationToken ct)
    {
        ServerLoginResult result = await JavaServerLogin.AcceptAsync(server, version, options, ct);
        await server.SendAsync(
            new ClientboundSetTimePacket(GameTime: 0, DayTime: 0, TickDayTime: false, ClockUpdates: []),
            ct);
        return result;
    }
}
