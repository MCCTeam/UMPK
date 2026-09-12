using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Commands;

public sealed class CommandSuggestionWireShapeTests
{
    private static BoundPacketCodec Cb(int protocol) => BoundCodec.At(protocol, PacketFlow.Clientbound,
                                                                      "minecraft:command_suggestions");
    private static BoundPacketCodec Sb(int protocol) => BoundCodec.At(protocol, PacketFlow.Serverbound,
                                                                      "minecraft:command_suggestion");
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    public void CommandSuggestions_DecodesPlainMatchList(int protocol)
    {
        byte[] frame = [0x03, 0x05, .. "/give"u8, 0x09, .. "/gamemode"u8, 0x09, .. "/gamerule"u8];
        var p = Assert.IsType<ClientboundLegacyTabCompletePacket>(Cb(protocol).DecodeFrame(frame));
        Assert.Equal(new[] { "/give", "/gamemode", "/gamerule" }, p.Matches);
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    public void CommandSuggestions_DecodesTooltips(int protocol)
    {
        byte[] frame = [
            0x09, 0x04, 0x06, 0x02, 0x05, .."stone"u8, 0x01, 0x10, .."{\"text\":\"Stone\"}"u8, 0x0a,
            .."stone_slab"u8, 0x00
        ];
        var p = Assert.IsType<ClientboundCommandSuggestionsPacket>(Cb(protocol).DecodeFrame(frame));
        Assert.Equal(9, p.TransactionId);
        Assert.Equal(4, p.RangeStart);
        Assert.Equal(6, p.RangeLength);
        Assert.Equal(2, p.Suggestions.Count);
        Assert.Equal("stone", p.Suggestions[0].Text);
        Assert.Equal("Stone", p.Suggestions[0].Tooltip!.ToPlainText());
        Assert.Null(p.Suggestions[1].Tooltip);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    public void CommandSuggestion_DecodesAssumeCommandAndBlock(int protocol)
    {
        byte[] frame = [0x0c, .. "/give @p sto"u8, 1, 1, 0, 0, 1, 1, 4, 0, 0, 6];
        var p = Assert.IsType<ServerboundLegacyTabCompletePacket>(Sb(protocol).DecodeFrame(frame));
        Assert.Equal("/give @p sto", p.Text);
        Assert.True(p.AssumeCommand);
        Assert.Equal(new Umpk.Geometry.BlockPos(4, 65, 6), p.LookedAtBlock);
        Assert.Equal(frame, Sb(protocol).Encode(p));
    }

    [Theory]
    [InlineData(393)]
    [InlineData(404)]
    public void CommandSuggestion_DecodesTransactionAndCommand(int protocol)
    {
        byte[] frame = [0x0b, 0x0c, .. "/give @p sto"u8];
        var p = Assert.IsType<ServerboundCommandSuggestionPacket>(Sb(protocol).DecodeFrame(frame));
        Assert.Equal(11, p.TransactionId);
        Assert.Equal("/give @p sto", p.Command);
    }
}
