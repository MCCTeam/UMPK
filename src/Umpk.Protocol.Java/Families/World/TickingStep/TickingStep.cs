namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Replaces the number of granted world steps, from 1.20.3.</summary>
        public static readonly PacketType<ClientboundTickingStepPacket> TickingStep =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("ticking_step"));
    }
}

/// <summary>The native signed step counter. Positive values permit frozen world ticks; zero and negative values grant none.</summary>
public sealed record ClientboundTickingStepPacket(int TickSteps) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.TickingStep;
}
