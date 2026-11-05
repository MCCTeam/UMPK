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
    public PacketType Type => IsPlay ? PlayType : LoginFamilyPackets.Config.UpdateEnabledFeatures;

    // The native packet moves from play to configuration in 1.20.2. Existing construction retains its configuration identity.
    internal bool IsPlay { get; init; }

    internal static readonly PacketType<ClientboundConfigUpdateEnabledFeaturesPacket> PlayType =
        new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("update_enabled_features"));
}
