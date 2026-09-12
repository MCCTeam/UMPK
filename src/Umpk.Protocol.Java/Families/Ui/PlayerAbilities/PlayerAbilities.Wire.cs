using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class UiMiscCodecs
{
    /// <summary>Player abilities clientbound (all versions).</summary>
    public static readonly PacketCodec<ClientboundPlayerAbilitiesPacket> PlayerAbilitiesV1_8 =
        PacketCodec<ClientboundPlayerAbilitiesPacket>.Of(
            static (ref PacketWriter w, ClientboundPlayerAbilitiesPacket p, PacketCodecContext _) =>
            {
                w.WriteByte(p.Flags);
                w.WriteFloat(p.FlyingSpeed);
                w.WriteFloat(p.WalkingSpeed);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundPlayerAbilitiesPacket(r.ReadByte(), r.ReadFloat(), r.ReadFloat()));

    /// <summary>Serverbound player abilities (770/776): a flags byte only.</summary>
    public static readonly PacketCodec<ServerboundPlayerAbilitiesPacket> ServerPlayerAbilitiesV1_14 =
        PacketCodec<ServerboundPlayerAbilitiesPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayerAbilitiesPacket p, PacketCodecContext _) => w.WriteByte(p.Flags),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundPlayerAbilitiesPacket(r.ReadByte()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerAbilities(PacketBindings bindings)
    {
        // Flags byte + fly speed + walk speed on every protocol that spells the packet minecraft:player_abilities (107 onward); 47 spells it minecraft:abilities and is bound above.
        bindings.Packet(UiPackets.Clientbound.PlayerAbilities)
            .From(JavaEras.Combat, UiMiscCodecs.PlayerAbilitiesV1_8);

        // 107-404 still send the full 1.8 body (flags byte + fly speed + walk speed); the flags-only form arrives at 1.14. The record for the three-field shape is the one the 1.8 dataset registers as minecraft:abilities.
        bindings.Packet(UiPackets.Serverbound.PlayerAbilities)
            .FromAs(JavaEras.Combat, UiPackets.Serverbound.LegacyAbilities, UiMiscCodecs.ServerLegacyAbilitiesV1_8)
            .From(JavaEras.Palettes, UiMiscCodecs.ServerPlayerAbilitiesV1_14);
    }
}
