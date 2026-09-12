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
        // abilities

        /// <summary>Player abilities (<c>minecraft:player_abilities</c> 770/776, <c>minecraft:abilities</c> 47).</summary>
        public static readonly PacketType<ClientboundPlayerAbilitiesPacket> PlayerAbilities =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("player_abilities"));
    }

    public static partial class Serverbound
    {
        /// <summary>Player abilities (<c>minecraft:player_abilities</c> 770/776, <c>minecraft:abilities</c> 47).</summary>
        public static readonly PacketType<ServerboundPlayerAbilitiesPacket> PlayerAbilities =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("player_abilities"));
    }
}

/// <summary>Player abilities: a flags byte plus flying and walking speeds.</summary>
public sealed record ClientboundPlayerAbilitiesPacket(byte Flags, float FlyingSpeed, float WalkingSpeed) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.PlayerAbilities;
}

/// <summary>Serverbound player abilities (770/776): a flags byte only (flying bit).</summary>
public sealed record ServerboundPlayerAbilitiesPacket(byte Flags) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.PlayerAbilities;
}
