using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        // advancements

        /// <summary>Update advancements (<c>minecraft:update_advancements</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundUpdateAdvancementsPacket> UpdateAdvancements =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("update_advancements"));
    }
}

/// <summary>Update advancements (770/776/777): a reset flag, the added advancements (each with an optional DisplayInfo carrying an ItemStack icon), the removed ids, the per-advancement progress maps, and the show-advancements flag. <c>ClientboundUpdateAdvancementsPacket</c>; the only 26.2 delta is the icon's ItemStack era codec, and 26.3 appends tab-position floats to each added element.</summary>
public sealed record ClientboundUpdateAdvancementsPacket(
    bool Reset,
    IReadOnlyList<AdvancementEntry> Added,
    IReadOnlyList<Identifier> Removed,
    IReadOnlyList<AdvancementProgressEntry> Progress,
    bool ShowAdvancements) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.UpdateAdvancements;
}
