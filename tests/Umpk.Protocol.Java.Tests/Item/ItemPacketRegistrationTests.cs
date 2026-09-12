using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Confirms the item/container family packets are wired through <c>PacketRegistrar</c> to real codecs (not left as NotImplemented markers) on every protocol they exist on. The registrar is driven directly with the era codec key each protocol's dataset assigns, exercising the binding and era-codec selection without a dependency on the generated data assembly.</summary>
public class ItemPacketRegistrationTests
{
    private static bool Implemented(ProtocolPhase phase, PacketFlow flow, string id, string key)
    {
        // Resolution is by protocol number, so build the descriptor at a protocol that carries this era key.
        var version = new GameVersion(GameEdition.Java, "test", CodecKeyProtocols.Of(key));
        var builder = new ProtocolDescriptorBuilder(version, new ProtocolFeatures());
        int wireId = 0;
        PacketRegistrar.Register(builder, phase, flow, wireId, id);
        ProtocolDescriptor descriptor = builder.Build();
        PhaseRegistry registry = descriptor.GetRegistry(phase, flow);
        Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec entry), $"{id} not registered ({key})");
        return entry.IsImplemented;
    }

    private static void AssertImplemented(ProtocolPhase phase, PacketFlow flow, string id, string key) =>
        Assert.True(Implemented(phase, flow, id, key), $"{flow} {id} resolved to a marker under key {key}");

    private const ProtocolPhase Play = ProtocolPhase.Play;
    private const PacketFlow Cb = PacketFlow.Clientbound;
    private const PacketFlow Sb = PacketFlow.Serverbound;

    [Fact]
    public void Legacy_1_8_ItemFamily_IsImplemented()
    {
        const string k = "V1_8";
        AssertImplemented(Play, Cb, "minecraft:open_screen", k);
        AssertImplemented(Play, Cb, "minecraft:set_slot", k);
        AssertImplemented(Play, Cb, "minecraft:container_set_content", k);
        AssertImplemented(Play, Cb, "minecraft:transaction", k);
        AssertImplemented(Play, Sb, "minecraft:block_place", k);
        AssertImplemented(Play, Sb, "minecraft:container_close", k);
        AssertImplemented(Play, Sb, "minecraft:container_click", k);
        AssertImplemented(Play, Sb, "minecraft:transaction", k);
        AssertImplemented(Play, Sb, "minecraft:creative_inventory_action", k);
        AssertImplemented(Play, Sb, "minecraft:container_button_click", k);
    }

    public static TheoryData<string> ModernKeys => new() { "V1_21_5", "V26_1" };

    [Theory]
    [MemberData(nameof(ModernKeys))]
    public void Modern_ItemFamily_IsImplemented(string k)
    {
        // Clientbound
        AssertImplemented(Play, Cb, "minecraft:open_screen", k);
        AssertImplemented(Play, Cb, "minecraft:container_close", k);
        AssertImplemented(Play, Cb, "minecraft:container_set_content", k);
        AssertImplemented(Play, Cb, "minecraft:container_set_slot", k);
        AssertImplemented(Play, Cb, "minecraft:container_set_data", k);
        AssertImplemented(Play, Cb, "minecraft:set_cursor_item", k);
        AssertImplemented(Play, Cb, "minecraft:set_player_inventory", k);
        AssertImplemented(Play, Cb, "minecraft:merchant_offers", k);
        AssertImplemented(Play, Cb, "minecraft:place_ghost_recipe", k);
        AssertImplemented(Play, Cb, "minecraft:recipe_book_add", k);
        AssertImplemented(Play, Cb, "minecraft:recipe_book_remove", k);
        AssertImplemented(Play, Cb, "minecraft:recipe_book_settings", k);
        AssertImplemented(Play, Cb, "minecraft:update_recipes", k);

        // Serverbound
        AssertImplemented(Play, Sb, "minecraft:container_close", k);
        AssertImplemented(Play, Sb, "minecraft:container_button_click", k);
        AssertImplemented(Play, Sb, "minecraft:container_click", k);
        AssertImplemented(Play, Sb, "minecraft:set_creative_mode_slot", k);
        AssertImplemented(Play, Sb, "minecraft:container_slot_state_changed", k);
        AssertImplemented(Play, Sb, "minecraft:use_item_on", k);
        AssertImplemented(Play, Sb, "minecraft:use_item", k);
        AssertImplemented(Play, Sb, "minecraft:pick_item_from_block", k);
        AssertImplemented(Play, Sb, "minecraft:pick_item_from_entity", k);
        AssertImplemented(Play, Sb, "minecraft:select_trade", k);
        AssertImplemented(Play, Sb, "minecraft:place_recipe", k);
        AssertImplemented(Play, Sb, "minecraft:recipe_book_change_settings", k);
        AssertImplemented(Play, Sb, "minecraft:recipe_book_seen_recipe", k);
    }
}
