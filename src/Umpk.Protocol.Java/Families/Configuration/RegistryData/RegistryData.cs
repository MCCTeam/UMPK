using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Registry data (<c>minecraft:registry_data</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigRegistryDataPacket> RegistryData =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("registry_data"));

        /// <summary>Registry data as a single unnamed-root NBT blob (<c>minecraft:registry_data</c>), clientbound; the 1.20.2/1.20.3 (protocols 764/765) form before 1.20.5 switched to the per-registry packed-entry <see cref="RegistryData"/>.</summary>
        public static readonly PacketType<ClientboundConfigRegistryBlobPacket> RegistryBlob =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("registry_data"));
    }
}

/// <summary>Configuration registry-data: a registry key and its packed entries (RegistryTracker input).</summary>
public sealed record ClientboundConfigRegistryDataPacket(Identifier Registry, IReadOnlyList<PackedRegistryEntry> Entries) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.RegistryData;
}

/// <summary>The 764/765 registry sync payload: one unnamed-root NBT compound holding every synchronized registry (dimension types, biomes, chat types, damage types,...), exactly as <c>RegistrySynchronization.NETWORK_CODEC</c> serializes the registry access.</summary>
/// <param name="Registries">The full registry-access compound.</param>
public sealed record ClientboundConfigRegistryBlobPacket(NbtTag Registries) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.RegistryBlob;
}
