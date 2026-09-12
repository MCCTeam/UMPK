using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public class RecipeBindingTests
{
    [Theory]
    [InlineData(338)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(754)]
    [InlineData(764)]
    [InlineData(767)]
    [InlineData(770)]
    public void PlaceGhostRecipe_RoundTrips(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:place_ghost_recipe");
        var packet = new ClientboundPlaceGhostRecipePacket(37, [0x0D, .. "minecraft:torch"u8[..13]]);
        byte[] wire = bound.Encode(packet);
        Assert.Equal(37, wire[0]);
        Assert.Equal(packet.RecipeDisplay, wire[1..]);
        var back = Assert.IsType<ClientboundPlaceGhostRecipePacket>(bound.DecodeFrame(wire));
        Assert.Equal(37, back.ContainerId);
        Assert.Equal(packet.RecipeDisplay, back.RecipeDisplay);
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(754)]
    [InlineData(764)]
    [InlineData(767)]
    public void PlaceRecipeByName_RoundTrips(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:place_recipe");
        var packet = new ServerboundPlaceRecipeByNamePacket(37, Identifier.Minecraft("stone_pickaxe"), true);
        byte[] wire = bound.Encode(packet);
        Assert.Equal(37, wire[0]);
        Assert.Equal(1, wire[^1]);
        Assert.Equal(packet, bound.DecodeFrame(wire));
    }

    [Theory]
    [InlineData(338)]
    [InlineData(340)]
    public void PlaceRecipe_NumericIdentifierRemainsUnimplemented(int protocol) => Assert.False(
        BoundCodec.IsImplementedAt(protocol, ProtocolPhase.Play, PacketFlow.Serverbound, "minecraft:place_recipe"));
    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(754)]
    [InlineData(764)]
    [InlineData(767)]
    [InlineData(770)]
    public void UpdateRecipes_RoundTripsPayload(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:update_recipes");
        var packet = new ClientboundUpdateRecipesPacket([0x02, 0x11, 0x22, 0x33, 0x44]);
        byte[] wire = bound.Encode(packet);
        Assert.Equal(packet.Payload, wire);
        Assert.Equal(packet.Payload, Assert.IsType<ClientboundUpdateRecipesPacket>(bound.DecodeFrame(wire)).Payload);
    }

    [Theory]
    [InlineData(751)]
    [InlineData(754)]
    [InlineData(758)]
    [InlineData(763)]
    [InlineData(764)]
    [InlineData(767)]
    public void RecipeBookSeenRecipe_UsesIdentifierBeforeDisplayId(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:recipe_book_seen_recipe");
        var packet = new ServerboundRecipeBookSeenRecipeByNamePacket(Identifier.Minecraft("golden_apple"));
        byte[] wire = bound.Encode(packet);
        Assert.Equal((byte)"minecraft:golden_apple".Length, wire[0]);
        Assert.Equal("minecraft:golden_apple", System.Text.Encoding.UTF8.GetString(wire[1..]));
        Assert.Equal(packet, bound.DecodeFrame(wire));
    }

    [Theory]
    [InlineData(768)]
    [InlineData(770)]
    [InlineData(776)]
    public void RecipeBookSeenRecipe_UsesDisplayId(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:recipe_book_seen_recipe");
        var packet = new ServerboundRecipeBookSeenRecipePacket(300);
        Assert.Equal([0xAC, 0x02], bound.Encode(packet));
        Assert.Equal(packet, bound.DecodeFrame([0xAC, 0x02]));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(477)]
    public void SelectTrade_RoundTrips(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:select_trade");
        var packet = new ServerboundSelectTradePacket(300);
        Assert.Equal([0xAC, 0x02], bound.Encode(packet));
        Assert.Equal(packet, bound.DecodeFrame([0xAC, 0x02]));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(477)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(754)]
    public void EditBook_UsesItemStackRecord(int protocol) =>
        Assert.Equal(typeof(ServerboundLegacyEditBookPacket),
                     BoundCodec.EntryAt(protocol, PacketFlow.Serverbound, "minecraft:edit_book").Type.PayloadType);
    [Theory]
    [InlineData(755)]
    [InlineData(758)]
    [InlineData(770)]
    public void EditBook_RoundTripsPagesAndTitle(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:edit_book");
        var packet = new ServerboundEditBookPacket(4, ["page one", "page two"], "Field Notes");
        var back = Assert.IsType<ServerboundEditBookPacket>(bound.DecodeFrame(bound.Encode(packet)));
        Assert.Equal(4, back.Slot);
        Assert.Equal(["page one", "page two"], back.Pages);
        Assert.Equal("Field Notes", back.Title);
    }
}
