using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Clientbound
    {
        /// <summary>The 1.8 confirm-transaction (<c>minecraft:transaction</c>).</summary>
        public static readonly PacketType<ClientboundTransactionPacket> Transaction =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("transaction"));
    }

    public static partial class Serverbound
    {
        /// <summary>The 1.8 confirm-transaction (<c>minecraft:transaction</c>).</summary>
        public static readonly PacketType<ServerboundTransactionPacket> Transaction =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("transaction"));
    }
}

/// <summary>The 1.8 confirm-transaction packet.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="ActionNumber">The action number to accept/reject.</param>
/// <param name="Accepted">Whether the action was accepted.</param>
public sealed record ClientboundTransactionPacket(int ContainerId, short ActionNumber, bool Accepted) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.Transaction;
}

/// <summary>The 1.8 serverbound confirm-transaction.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="ActionNumber">The action number being confirmed.</param>
/// <param name="Accepted">Whether the client accepted.</param>
public sealed record ServerboundTransactionPacket(int ContainerId, short ActionNumber, bool Accepted) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.Transaction;
}
