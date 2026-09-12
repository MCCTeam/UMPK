using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Update tags (<c>minecraft:update_tags</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigUpdateTagsPacket> UpdateTags =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("update_tags"));
    }
}

/// <summary>Configuration update-tags: per-registry tag payloads.</summary>
public sealed record ClientboundConfigUpdateTagsPacket(IReadOnlyList<TagRegistry> Registries) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.UpdateTags;
}

/// <summary>A single registry's tag payload as carried in <c>update_tags</c>: a map from tag id to the list of numeric registry ids the tag contains.</summary>
public sealed record TagEntry(Identifier TagId, IReadOnlyList<int> Ids);

/// <summary>Update-tags: one registry key and its tag entries.</summary>
public sealed record TagRegistry(Identifier Registry, IReadOnlyList<TagEntry> Tags);
