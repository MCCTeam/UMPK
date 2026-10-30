using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class AdventurePredicateControlEscapeHashTests
{
    [Fact]
    public void TableHashUsesSourceDerivedUppercaseUnnamedControlEscapes()
    {
        // Source-derived expectation, not an executed original-client witness: SnbtGrammar HEX_ESCAPE uses HexFormat.of().withUpperCase(); StringTag prefixes unnamed controls with lowercase x.
        const string canonicalSnbt = "{probe:\"\\x0B\\x1F\"}";
        const int sourceDerivedHash = -380370003;
        var nbt = new NbtCompound { ["probe"] = new NbtString("\u000B\u001F") };
        var predicate = new BlockPredicateEntry(null, null, nbt) { ComponentMatchers = new([], []) };
        var component = new AdventureModePredicateComponent([predicate]);
        var context = new PacketCodecContext(JavaGameData.Registries(770), IConnectionCodecState.Empty);
        var ops = new HashOps();
        Assert.Equal(sourceDerivedHash, ops.Map([(ops.String("nbt"), ops.String(canonicalSnbt))]));

        int actual = ItemComponentTable.V1_21_5().ByKey(DataComponents.CanBreak).Hash(ops, component, context);

        Assert.Equal(sourceDerivedHash, actual);
    }
}
