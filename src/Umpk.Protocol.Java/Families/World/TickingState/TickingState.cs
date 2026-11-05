namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Declared world tick rate and freeze flag, from 1.20.3.</summary>
        public static readonly PacketType<ClientboundTickingStatePacket> TickingState =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("ticking_state"));
    }
}

/// <summary>The server's declared world tick rate and freeze flag. The native tick manager applies the minimum rate when handling the packet.</summary>
public sealed record ClientboundTickingStatePacket(float TickRate, bool IsFrozen) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.TickingState;
}
