using Umpk.Game.Inventory;
using Umpk.Game.Registries;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Builds an empty-but-well-formed <see cref="RegistryAccess"/>: every well-known registry present and empty. Used by codecs that never touch registry holders (handshake, status, login, and the keep-alive/chat exemplars) so they can run before real registries are installed. It is not the game's real registry data; the session installs that via <c>SetCodecState</c> at the phase pause.</summary>
internal static class EmptyRegistries
{
    public static RegistryAccess Access { get; } = BuildEmpty();

    private static RegistryAccess BuildEmpty()
    {
        var snapshot = new RegistrySnapshotBuilder()
            .Add(new RegistryBuilder<BlockDefinition>(RegistryIds.Block).Build())
            .Add(new RegistryBuilder<ItemDefinition>(RegistryIds.Item).Build())
            .Add(new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType).Build())
            .Add(new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType).Build())
            .Add(new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome).Build())
            .Add(new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment).Build())
            .Add(new RegistryBuilder<MobEffectDefinition>(RegistryIds.MobEffect).Build())
            .Add(new RegistryBuilder<AttributeDefinition>(RegistryIds.Attribute).Build())
            .Add(new RegistryBuilder<MenuTypeDefinition>(RegistryIds.Menu).Build())
            .Build();
        return RegistryAccess.FromSnapshot(snapshot);
    }
}
