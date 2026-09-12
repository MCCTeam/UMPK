using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginPackets
{
    public static partial class Clientbound
    {
        /// <summary>Login success / game profile (<c>minecraft:login_finished</c>).</summary>
        public static readonly PacketType<ClientboundLoginFinishedPacket> LoginFinished =
            new(ProtocolPhase.Login, PacketFlow.Clientbound, Identifier.Minecraft("login_finished"));
    }
}

/// <summary>Login success / game profile: UUID, username, profile properties, and (26.2 era, V26_2) a trailing session id UUID. On 1.8 the UUID is a dashed string and there are no properties; the era codec reads/writes only what the version has.</summary>
public sealed record ClientboundLoginFinishedPacket(
    Guid Uuid,
    string Username,
    IReadOnlyList<GameProfileProperty> Properties,
    Guid? SessionId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginPackets.Clientbound.LoginFinished;

    /// <summary>The deprecated strict-error-handling boolean carried only by the 1.20.5-1.21.1 wire (protocols 764-767, protocols 766/767; introduced in 1.20.5, removed in 1.21.2). Always false on other eras.</summary>
    public bool StrictErrorHandling { get; init; }
}
