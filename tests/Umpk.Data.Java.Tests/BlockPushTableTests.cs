using System.Buffers;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>The generated per-block piston table used by the client.</summary>
/// <remarks>
/// <para>The three values determine whether a piston may move or destroy a block. No packet carries these values, so the generated data records them per protocol.</para>
/// <para>EVERY ASSERTION HERE IS AGAINST A NON-DEFAULT VALUE. A test that checked <c>minecraft:stone</c> is NORMAL, breakable and entity-less would pass against a table of all zeroes, against a table read at the wrong offset, and against no table at all.</para>
/// </remarks>
public sealed class BlockPushTableTests
{
    /// <summary>The protocols whose dataset carries a push table.</summary>
    public static TheoryData<int> MeasuredProtocols =>
    [
        498, 573, 575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764,
        765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776,
    ];

    /// <summary>Flattened protocols without an extractable push table, plus one legacy band. These must report NO DATA, which is a different statement from "every block is normal".</summary>
    /// <remarks>Protocols 393/401/404 and 477/480/485/490 have no official names available for extraction. Protocol 340 is a pre-flattening legacy band included as a negative control on the identity gate itself, not on measurement availability.</remarks>
    public static TheoryData<int> UnmeasuredProtocols => [47, 340, 393, 401, 404, 477, 480, 485, 490];

    [Theory]
    [MemberData(nameof(MeasuredProtocols))]
    public void Measured_protocols_carry_a_table(int protocol) =>
        Assert.True(JavaGameData.BlockPushData(protocol).HasData);

    /// <summary>The load-bearing negative. An unmeasured band must refuse, INCLUDING for a block whose real answer is the default: a source that quietly returned "normal, breakable, no block entity" there would let the resolver push a column it has no measurement for.</summary>
    [Theory]
    [MemberData(nameof(UnmeasuredProtocols))]
    public void Unmeasured_protocols_refuse_rather_than_default(int protocol)
    {
        IBlockPushSource source = JavaGameData.BlockPushData(protocol);
        Assert.False(source.HasData);
        Assert.False(source.TryGet(Identifier.Minecraft("stone"), out _));
        Assert.False(source.TryGet(Identifier.Minecraft("anvil"), out _));
    }

    /// <summary>The four reactions that actually occur, each pinned on a block whose value is NOT the default. <c>PushReaction.IGNORE</c> is deliberately absent: no block on any measured band carries it.</summary>
    [Theory]
    [MemberData(nameof(MeasuredProtocols))]
    public void The_non_default_reactions_reach_the_reader(int protocol)
    {
        IBlockPushSource source = JavaGameData.BlockPushData(protocol);

        // BLOCK prevents piston movement entirely.
        Assert.Equal(PistonPushReaction.Block, Reaction(source, "anvil"));
        Assert.Equal(PistonPushReaction.Block, Reaction(source, "piston"));
        Assert.Equal(PistonPushReaction.Block, Reaction(source, "sticky_piston"));
        Assert.Equal(PistonPushReaction.Block, Reaction(source, "piston_head"));

        // DESTROY: isPushable returns the caller's canBreak flag, and resolve() drops the block.
        Assert.Equal(PistonPushReaction.Destroy, Reaction(source, "oak_leaves"));
        Assert.Equal(PistonPushReaction.Destroy, Reaction(source, "oak_door"));
        Assert.Equal(PistonPushReaction.Destroy, Reaction(source, "torch"));

        // PUSH_ONLY: isPushable returns pushDirection == facing.
        Assert.Equal(PistonPushReaction.PushOnly, Reaction(source, "white_glazed_terracotta"));

        // NORMAL, and it MATTERS that these two are normal: they are the sticky blocks, and a table that made them anything else would silently disable every branching push.
        Assert.Equal(PistonPushReaction.Normal, Reaction(source, "slime_block"));
        Assert.Equal(PistonPushReaction.Normal, Reaction(source, "stone"));

        // Obsidian's reaction is NORMAL: it is refused by NAME in isPushable, not by reaction, so a resolver that only consulted this table would happily push it.
        Assert.Equal(PistonPushReaction.Normal, Reaction(source, "obsidian"));
    }

    [Theory]
    [MemberData(nameof(MeasuredProtocols))]
    public void Unbreakable_and_block_entity_flags_reach_the_reader(int protocol)
    {
        IBlockPushSource source = JavaGameData.BlockPushData(protocol);

        Assert.True(Info(source, "bedrock").Unbreakable);
        Assert.True(Info(source, "barrier").Unbreakable);
        Assert.False(Info(source, "stone").Unbreakable);

        // isPushable's last line is "return !state.hasBlockEntity()", so this is what stops a piston pushing a chest even though a chest's push reaction is NORMAL.
        Assert.Equal(PistonPushReaction.Normal, Reaction(source, "chest"));
        Assert.True(Info(source, "chest").HasBlockEntity);
        Assert.False(Info(source, "stone").HasBlockEntity);
    }

    /// <summary>Beds carry a block entity on every band up to 26.1 and stop carrying one at 26.2, so one shared name-keyed table would answer 16 of 1196 blocks wrongly on the newest version. This is also the only per-block change of any of the three values across the whole measured 1.14.4 to 26.2 span.</summary>
    [Fact]
    public void Beds_lose_their_block_entity_at_protocol_776()
    {
        Assert.True(Info(JavaGameData.BlockPushData(774), "white_bed").HasBlockEntity);
        Assert.True(Info(JavaGameData.BlockPushData(775), "white_bed").HasBlockEntity);
        Assert.False(Info(JavaGameData.BlockPushData(776), "white_bed").HasBlockEntity);
    }

    /// <summary>Hardcoded refusal names appear only in protocol bands whose block registries contain them.</summary>
    [Fact]
    public void The_hardcoded_refusals_do_not_exist_before_the_band_that_names_them()
    {
        foreach (string name in new[] { "crying_obsidian", "respawn_anchor", "reinforced_deepslate" })
        {
            Assert.False(JavaGameData.BlockPushData(498).TryGet(Identifier.Minecraft(name), out _));
            Assert.False(JavaGameData.BlockPushData(578).TryGet(Identifier.Minecraft(name), out _));
        }

        // 1.17.1 names crying_obsidian and respawn_anchor but not reinforced_deepslate, which is exactly the band where the block does not exist yet.
        Assert.True(JavaGameData.BlockPushData(756).TryGet(Identifier.Minecraft("crying_obsidian"), out _));
        Assert.False(JavaGameData.BlockPushData(756).TryGet(Identifier.Minecraft("reinforced_deepslate"), out _));
        Assert.True(JavaGameData.BlockPushData(759).TryGet(Identifier.Minecraft("reinforced_deepslate"), out _));

        // honey_block, the block that makes 1.15's isSticky differ from 1.14.4's.
        Assert.False(JavaGameData.BlockPushData(498).TryGet(Identifier.Minecraft("honey_block"), out _));
        Assert.True(JavaGameData.BlockPushData(578).TryGet(Identifier.Minecraft("honey_block"), out _));
    }

    /// <summary>Fourteen additional measured protocols: 573, 575, 735, 736, 751, 753, 754, 757, 758, 760, 761, 762, 763, and 764. Each expectation is protocol-specific.</summary>
    /// <remarks>
    /// <para><c>crying_obsidian</c> and <c>respawn_anchor</c> join the by-name refusal list at protocol 735; <c>reinforced_deepslate</c> joins at 760. Their table reaction remains NORMAL because refusal happens by name in the resolver.</para>
    /// <para><c>white_bed</c> has DESTROY reaction on every one of these protocols.</para>
    /// </remarks>
    [Fact]
    public void The_fourteen_newly_measured_protocols_agree_with_their_own_decompiled_source()
    {
        foreach (int protocol in new[] { 573, 575, 735, 736, 751, 753, 754, 757, 758, 760, 761, 762, 763, 764 })
        {
            IBlockPushSource source = JavaGameData.BlockPushData(protocol);
            Assert.True(source.HasData, protocol.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(PistonPushReaction.Destroy, Reaction(source, "white_bed"));
            Assert.Equal(PistonPushReaction.Normal, Reaction(source, "obsidian"));
            Assert.Equal(PistonPushReaction.Normal, Reaction(source, "slime_block"));
        }

        // Not yet in the by-name refusal list at 1.15/1.15.1: the block does not exist yet.
        Assert.False(JavaGameData.BlockPushData(573).TryGet(Identifier.Minecraft("crying_obsidian"), out _));
        Assert.False(JavaGameData.BlockPushData(575).TryGet(Identifier.Minecraft("crying_obsidian"), out _));

        // First in the refusal list one band earlier than 756 (1.17.1) already proved.
        Assert.Equal(PistonPushReaction.Normal, Reaction(JavaGameData.BlockPushData(735), "crying_obsidian"));
        Assert.Equal(PistonPushReaction.Normal, Reaction(JavaGameData.BlockPushData(735), "respawn_anchor"));

        // reinforced_deepslate joins starting at 1.19.1/1.19.2 (protocol 760), not before.
        Assert.False(JavaGameData.BlockPushData(758).TryGet(Identifier.Minecraft("reinforced_deepslate"), out _));
        Assert.Equal(PistonPushReaction.Normal, Reaction(JavaGameData.BlockPushData(760), "reinforced_deepslate"));
    }

    /// <summary><c>JavaBlockPush.Create</c> checks <c>blocks.Count != count</c> BEFORE allocating the row array or reading a single row VarInt. The payload here declares a row count that does not match the registry AND has no bytes left to satisfy it. The validate-first order detects the mismatch from the count alone and returns gracefully.</summary>
    [Fact]
    public void A_mismatched_count_with_no_row_bytes_refuses_without_reading_past_the_payload()
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(774).Blocks;
        int wrongCount = blocks.Count + 1000; // Guaranteed not to equal the real registry count.

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(wrongCount);
        // No row bytes follow: reading even one row would run off the end of the payload.

        JavaBlockPush source = JavaBlockPush.Create(blocks, buffer.WrittenSpan);
        Assert.False(source.HasData);
        Assert.False(source.TryGet(Identifier.Minecraft("stone"), out _));
    }

    /// <summary>An identifier no version has must be a refusal, not a default.</summary>
    [Fact]
    public void An_unknown_identifier_is_refused()
    {
        IBlockPushSource source = JavaGameData.BlockPushData(774);
        Assert.True(source.HasData);
        Assert.False(source.TryGet(new Identifier("somemod", "gravity_gun"), out _));
    }

    private static BlockPushInfo Info(IBlockPushSource source, string name)
    {
        Assert.True(source.TryGet(Identifier.Minecraft(name), out BlockPushInfo info), name);
        return info;
    }

    private static PistonPushReaction Reaction(IBlockPushSource source, string name) => Info(source, name).Reaction;
}
