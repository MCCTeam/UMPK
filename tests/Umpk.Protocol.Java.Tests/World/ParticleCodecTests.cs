using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Tests.Item;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary>Round-trip tests for the particle option codec (modern shape table + 1.8 argument counts).</summary>
public class ParticleCodecTests
{
    private static PacketCodecContext Ctx => ItemTestRegistries.Context;

    private static ItemComponentTable Icons770 => ItemStackCodecs.ComponentsV1_21_5;

    private static ItemComponentTable Icons776 => ItemStackCodecs.ComponentsV26_2;

    [Theory]
    [InlineData(0)]   // no options
    [InlineData(1)]   // block: VarInt block-state id
    [InlineData(13)]  // dust: int color + float scale
    [InlineData(14)]  // dust_color_transition
    [InlineData(20)]  // entity_effect: int color
    [InlineData(37)]  // sculk_charge: float roll
    [InlineData(46)]  // item (bare)
    [InlineData(47)]  // vibration
    [InlineData(48)]  // trail
    public void Modern_V1_21_5_RoundTrips_EachShape(int typeId)
    {
        byte[] payload = BuildModernParticle(typeId, ParticleCodec.ModernV1_21_5);
        var r = new PacketReader(payload);
        ParticleData decoded = ParticleCodec.ReadModern(ref r, ParticleCodec.ModernV1_21_5, Icons770, Ctx);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(typeId, decoded.TypeId);

        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        ParticleCodec.WriteModern(ref w, decoded);
        Assert.Equal(payload, buffer.WrittenSpan.ToArray());
    }

    // Protocol 776 option-carrying particle ids and shapes. Protocol 776 inserts the geyser family near the front, shifting every later id; protocol 775 is pinned separately.
    [Theory]
    [InlineData(23, ParticleOptionShape.IntFloat)]  // effect: SpellParticleOption
    [InlineData(53, ParticleOptionShape.IntFloat)]  // instant_effect: SpellParticleOption
    [InlineData(49, ParticleOptionShape.Int)]       // flash: ColorParticleOption
    [InlineData(15, ParticleOptionShape.Float)]     // dragon_breath: PowerParticleOption
    [InlineData(7, ParticleOptionShape.Int)]        // geyser: GeyserParticleOptions
    [InlineData(10, ParticleOptionShape.Int)]       // geyser_plume: GeyserParticleOptions
    [InlineData(8, ParticleOptionShape.IntFloat)]   // geyser_base: GeyserBaseParticleOptions
    [InlineData(9, ParticleOptionShape.IntFloat)]   // geyser_poof: GeyserBaseParticleOptions
    public void Modern_V26_2_OptionCarryingTypes_RoundTrip(int typeId, ParticleOptionShape expectedShape)
    {
        Assert.True(ParticleCodec.ModernV26_2.TryGetValue(typeId, out ParticleOptionShape actual));
        Assert.Equal(expectedShape, actual);

        byte[] payload = BuildModernParticle(typeId, ParticleCodec.ModernV26_2);
        var r = new PacketReader(payload);
        ParticleData decoded = ParticleCodec.ReadModern(ref r, ParticleCodec.ModernV26_2, Icons776, Ctx);
        Assert.Equal(0, r.Remaining); // options fully consumed: no unread trailing option bytes
        Assert.Equal(typeId, decoded.TypeId);

        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        ParticleCodec.WriteModern(ref w, decoded);
        Assert.Equal(payload, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Modern_UnknownType_HasNoOptions()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(9999); // not in shape table
        var r = new PacketReader(buffer.WrittenSpan);
        ParticleData decoded = ParticleCodec.ReadModern(ref r, ParticleCodec.ModernV1_21_5, Icons770, Ctx);
        Assert.Equal(9999, decoded.TypeId);
        Assert.Empty(decoded.Options);
    }

    [Fact]
    public void Modern_Vibration_EntitySource_RoundTrips()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(47);  // vibration
        w.WriteVarInt(1);   // entity source
        w.WriteVarInt(1234);
        w.WriteFloat(1.5f); // y offset
        w.WriteVarInt(20);  // arrival ticks
        byte[] payload = buffer.WrittenSpan.ToArray();

        var r = new PacketReader(payload);
        ParticleData decoded = ParticleCodec.ReadModern(ref r, ParticleCodec.ModernV1_21_5, Icons770, Ctx);
        Assert.Equal(0, r.Remaining);

        var outBuf = new ArrayBufferWriter<byte>();
        var ow = new PacketWriter(outBuf);
        ParticleCodec.WriteModern(ref ow, decoded);
        Assert.Equal(payload, outBuf.WrittenSpan.ToArray());
    }

    [Fact]
    public void Modern_ItemParticle_CarryingComponents_RoundTrips()
    {
        // An item particle whose stack carries a data component (a damage value). The old raw-bytes fallback threw on any non-empty component patch; the real ItemStack codec now handles it.
        var comps = DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(42));
        ItemStack stack = new(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1, comps);

        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(46); // item particle
        ItemStackCodecs.WriteModernStack(ref w, stack, Ctx, Icons770);
        byte[] payload = buffer.WrittenSpan.ToArray();

        var r = new PacketReader(payload);
        ParticleData decoded = ParticleCodec.ReadModern(ref r, ParticleCodec.ModernV1_21_5, Icons770, Ctx);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(46, decoded.TypeId);

        // Byte-exact re-encode of the captured option bytes.
        var outBuf = new ArrayBufferWriter<byte>();
        var ow = new PacketWriter(outBuf);
        ParticleCodec.WriteModern(ref ow, decoded);
        Assert.Equal(payload, outBuf.WrittenSpan.ToArray());

        // And the captured options decode back to the same stack (count, item, component).
        var optReader = new PacketReader(decoded.Options);
        ItemStack roundTripped = ItemStackCodecs.ReadModernStack(ref optReader, Ctx, Icons770);
        Assert.Equal(stack.Item.NetworkId, roundTripped.Item.NetworkId);
        Assert.True(roundTripped.Components.TryGet(DataComponents.Damage, out DamageComponent? dmg));
        Assert.Equal(42, dmg!.Value);
    }

    [Theory]
    [InlineData(0, 0)]   // no args
    [InlineData(36, 2)]  // iconcrack: 2 args
    [InlineData(37, 1)]  // blockcrack: 1 arg
    [InlineData(38, 1)]  // blockdust: 1 arg
    public void Legacy_V1_8_RoundTrips_ArgumentCounts(int typeId, int args)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteInt(typeId);
        for (int i = 0; i < args; i++)
            w.WriteVarInt(100 + i);

        byte[] payload = buffer.WrittenSpan.ToArray();
        var r = new PacketReader(payload);
        ParticleData decoded = ParticleCodec.ReadLegacy(ref r);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(typeId, decoded.TypeId);

        var outBuf = new ArrayBufferWriter<byte>();
        var ow = new PacketWriter(outBuf);
        ParticleCodec.WriteLegacy(ref ow, decoded);
        Assert.Equal(payload, outBuf.WrittenSpan.ToArray());
    }

    [Fact]
    public void ModernShapeTables_DifferBetweenWireLayouts()
    {
        // dust is id 13 on 1.21.5 and id 21 on 26.2; the tables must disagree on that id.
        Assert.True(ParticleCodec.ModernV1_21_5.ContainsKey(13));
        Assert.True(ParticleCodec.ModernV26_2.ContainsKey(21));
        Assert.False(ParticleCodec.ModernV26_2.ContainsKey(13) && ParticleCodec.ModernV26_2[13] == ParticleOptionShape.DustColor);
    }

    private static byte[] BuildModernParticle(int typeId, IReadOnlyDictionary<int, ParticleOptionShape> shapes)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(typeId);
        ParticleOptionShape shape = shapes.TryGetValue(typeId, out ParticleOptionShape s) ? s : ParticleOptionShape.None;
        switch (shape)
        {
            case ParticleOptionShape.None:
                break;
            case ParticleOptionShape.VarInt:
                w.WriteVarInt(42);
                break;
            case ParticleOptionShape.Int:
                w.WriteInt(0x11223344);
                break;
            case ParticleOptionShape.Float:
                w.WriteFloat(1.25f);
                break;
            case ParticleOptionShape.IntFloat:
                w.WriteInt(0x0055AA33);
                w.WriteFloat(2.5f);
                break;
            case ParticleOptionShape.DustColor:
                w.WriteInt(0xFF00FF);
                w.WriteFloat(1.0f);
                break;
            case ParticleOptionShape.DustTransition:
                w.WriteInt(0xFF0000);
                w.WriteInt(0x0000FF);
                w.WriteFloat(1.0f);
                break;
            case ParticleOptionShape.Trail:
                w.WriteDouble(1.0);
                w.WriteDouble(2.0);
                w.WriteDouble(3.0);
                w.WriteInt(0x123456);
                w.WriteVarInt(40);
                break;
            case ParticleOptionShape.Vibration:
                w.WriteVarInt(0); // block source
                w.WriteLong(1234567L);
                w.WriteVarInt(20);
                break;
            case ParticleOptionShape.Item:
                w.WriteVarInt(0); // empty item (bare)
                break;
            default:
                throw new InvalidOperationException($"Unhandled shape {shape}.");
        }

        return buffer.WrittenSpan.ToArray();
    }
}
