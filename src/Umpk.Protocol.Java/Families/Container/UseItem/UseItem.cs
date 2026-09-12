using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Serverbound
    {
        /// <summary>Use the held item (<c>minecraft:use_item</c>).</summary>
        public static readonly PacketType<ServerboundUseItemPacket> UseItem =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("use_item"));
    }
}

/// <summary>Use the held item (right-click in air). Modern; carries hand, sequence, and look angles.</summary>
/// <param name="Hand">The interaction hand.</param>
/// <param name="Sequence">The interaction sequence.</param>
/// <param name="YRot">The yaw at use time.</param>
/// <param name="XRot">The pitch at use time.</param>
public sealed record ServerboundUseItemPacket(int Hand, int Sequence, float YRot, float XRot) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.UseItem;
}
