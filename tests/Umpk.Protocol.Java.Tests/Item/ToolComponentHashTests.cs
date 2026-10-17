using Umpk.Data.Java;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Literal results produced by vanilla 1.21.5 Tool.CODEC.encodeStart with RegistryOps over HashOps.CRC32C_INSTANCE. The probe used the official jar, its mappings and its block tags; raw inputs and hashes are retained in /tmp/umpk-block-mining/1.21.5/tool-hash-raw.json.</summary>
public sealed class ToolComponentHashTests
{
    [Theory]
    [InlineData(0, 937295970)]
    [InlineData(1, -1745692832)]
    [InlineData(2, 1360756880)]
    [InlineData(3, 1503381454)]
    [InlineData(4, 1522436745)]
    public void HashMatchesTheNativeToolCodec(int vector, int expected)
    {
        var registries = JavaGameData.Registries(770);
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        var rules = new List<NbtCompound>();
        if (vector >= 2)
        {
            var rule = new NbtCompound();
            if (vector == 4)
            {
                var tag = new NbtList(NbtTagType.String);
                tag.Add(new NbtString("minecraft:mineable/pickaxe"));
                rule.Put("blocks", tag);
            }
            else
            {
                var blocks = new NbtList(NbtTagType.Int);
                Assert.True(registries.Blocks.TryGetNetworkId(Identifier.Minecraft("stone"), out int stone));
                blocks.Add(new NbtInt(stone));
                if (vector == 3)
                {
                    Assert.True(registries.Blocks.TryGetNetworkId(Identifier.Minecraft("dirt"), out int dirt));
                    blocks.Add(new NbtInt(dirt));
                }
                rule.Put("blocks", blocks);
            }
            rule.PutFloat("speed", vector == 4 ? 7.5F : 2.0F);
            rule.PutBool("correct_for_drops", vector != 3);
            rules.Add(rule);
        }
        var tool = new ToolComponent(vector == 4 ? 3.0F : 1.0F, vector == 4 ? 2 : 1, rules)
        {
            CanDestroyBlocksInCreative = vector is not (1 or 4),
        };

        int actual = ItemComponentCodecs.Tool.Hash(new HashOps(), tool, context);

        Assert.Equal(expected, actual);
    }
}
