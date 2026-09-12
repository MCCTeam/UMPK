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
        /// <summary>Legacy 1.8 player abilities (<c>minecraft:abilities</c>, 47).</summary>
        public static readonly PacketType<ClientboundPlayerAbilitiesPacket> LegacyAbilities =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("abilities"));
    }

    public static partial class Serverbound
    {
        /// <summary>Legacy 1.8 player abilities (<c>minecraft:abilities</c>, 47; carries speed floats).</summary>
        public static readonly PacketType<ServerboundLegacyPlayerAbilitiesPacket> LegacyAbilities =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("abilities"));
    }
}

/// <summary>Legacy 1.8 serverbound player abilities (47): a flags byte plus fly and walk speeds.</summary>
public sealed record ServerboundLegacyPlayerAbilitiesPacket(byte Flags, float FlyingSpeed, float WalkingSpeed) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.LegacyAbilities;
}
