using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Update enabled features (<c>minecraft:update_enabled_features</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigUpdateEnabledFeaturesPacket> UpdateEnabledFeatures =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("update_enabled_features"));
    }
}

/// <summary>Configuration update-enabled-features: the set of enabled feature-flag ids.</summary>
public sealed record ClientboundConfigUpdateEnabledFeaturesPacket(IReadOnlyList<Identifier> Features) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.UpdateEnabledFeatures;
}
