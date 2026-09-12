using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary><c>minecraft:level_particles</c> has two independent era axes: packet field order and the particle registry used to interpret its payload.</summary>
/// <remarks>
/// <para>Axis 1 is the packet frame. The particle type id is written FIRST on every version through 1.20.4 and only moves to the END of the frame at 1.20.5:</para>
/// <list type="bullet">
/// <item>477-498: int id first, float coordinates.</item>
/// <item>573-758: int id first, double coordinates.</item>
/// <item>759-765: the id becomes a VarInt and remains first.</item>
/// <item>766-768: the particle moves to the end; no <c>alwaysShow</c> bool.</item>
/// <item>769+: a second leading bool, <c>alwaysShow</c>.</item>
/// </list>
/// <para>Axis 2, the particle registry ordering and its option payloads, which is why the modern band needs one entry per era rather than two. See <c>ParticleCodec</c> for the tables and their evidence.</para>
/// <para>A round trip is blind to all of it (encode and decode share the wrong codec), and so is the registration fixture (a codec is bound either way). Every assertion here is a frame LENGTH, a byte at a fixed offset, or a cross-era rejection.</para>
/// </remarks>
public class ParticleCodecBindingTests
{
    private const string LevelParticles = "minecraft:level_particles";

    // The header widths each frame family produces for a particle with no options. int id first + bool + 3 floats + 3 floats + float + int  = 4 + 1 + 12 + 12 + 4 + 4 = 37
    private const int TypeIdFirstFloatLength = 37;

    // int id first + bool + 3 doubles + 3 floats + float + int = 4 + 1 + 24 + 12 + 4 + 4 = 49
    private const int TypeIdFirstDoubleLength = 49;

    // VarInt id (one byte here) + bool + 3 doubles + 3 floats + float + int = 1 + 1 + 24 + 12 + 4 + 4 = 46
    private const int VarIntIdFirstLength = 46;

    // bool + 3 doubles + 3 floats + float + int + VarInt id = 1 + 24 + 12 + 4 + 4 + 1 = 46
    private const int ParticleLastLength = 46;

    // As above plus the alwaysShow bool.
    private const int ParticleLastWithAlwaysShowLength = 47;

    private static ClientboundLevelParticlesPacket Packet(int typeId, byte[]? options = null) => new(
        OverrideLimiter: true,
        AlwaysShow: false,
        X: 1.0,
        Y: 2.0,
        Z: 3.0,
        XDist: 0.25f,
        YDist: 0.5f,
        ZDist: 0.75f,
        MaxSpeed: 1.5f,
        Count: 4,
        Particle: new ParticleData(typeId, options ?? []));

    private static byte[] Encode(int protocol, ClientboundLevelParticlesPacket packet) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, LevelParticles).Encode(packet);

    // Where the type id sits, and how wide the coordinates are.

    /// <summary>Each frame family has its own width for the same option-free particle, and the id sits at a different end of the frame. The widths alone separate every boundary except 759-765 versus 766-768, which the byte-position pin below covers.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="expectedLength">The frame width for an option-free particle.</param>
    [Theory]
    [InlineData(477, TypeIdFirstFloatLength)]
    [InlineData(498, TypeIdFirstFloatLength)]
    [InlineData(573, TypeIdFirstDoubleLength)]
    [InlineData(754, TypeIdFirstDoubleLength)]
    [InlineData(758, TypeIdFirstDoubleLength)]
    [InlineData(759, VarIntIdFirstLength)]
    [InlineData(763, VarIntIdFirstLength)]
    [InlineData(764, VarIntIdFirstLength)]
    [InlineData(765, VarIntIdFirstLength)]
    [InlineData(766, ParticleLastLength)]
    [InlineData(767, ParticleLastLength)]
    [InlineData(768, ParticleLastLength)]
    [InlineData(769, ParticleLastWithAlwaysShowLength)]
    [InlineData(770, ParticleLastWithAlwaysShowLength)]
    [InlineData(774, ParticleLastWithAlwaysShowLength)]
    [InlineData(775, ParticleLastWithAlwaysShowLength)]
    [InlineData(776, ParticleLastWithAlwaysShowLength)]
    public void LevelParticles_FrameWidthIsTheProtocolsOwn(int protocol, int expectedLength) =>
        Assert.Equal(expectedLength, Encode(protocol, Packet(3)).Length);

    /// <summary>Protocols 765 and 766 produce equal-width frames for an option-free particle, but 765 writes the type id first and 766 writes it last.</summary>
    [Fact]
    public void LevelParticles_TypeIdMovesToTheEndAt766()
    {
        byte[] before = Encode(765, Packet(3));
        byte[] after = Encode(766, Packet(3));

        Assert.Equal(before.Length, after.Length);
        Assert.Equal(3, before[0]);
        Assert.Equal(3, after[^1]);
        Assert.NotEqual(3, after[0]);
    }

    /// <summary>The <c>alwaysShow</c> bool is exactly one byte and it arrives at 769, so a 768 frame decoded under 769 shifts every coordinate by a byte. Asserted as a width delta so a future change to the coordinate types cannot mask it.</summary>
    [Fact]
    public void LevelParticles_AlwaysShowBoolArrivesAt769() =>
        Assert.Equal(Encode(768, Packet(3)).Length + 1, Encode(769, Packet(3)).Length);

    /// <summary>The coordinates are floats on 1.14-1.14.4 and doubles from 1.15, a twelve-byte difference on the same otherwise-identical frame.</summary>
    [Fact]
    public void LevelParticles_CoordinatesWidenAt573() =>
        Assert.Equal(Encode(498, Packet(3)).Length + 12, Encode(573, Packet(3)).Length);

    // Cross-era rejection across every frame boundary, in both directions.

    /// <summary>A frame built on the NARROW side of a boundary must not decode on the wide side, and a frame with a fixed-width tail must not decode one byte short. Frame-exactness is what catches these: the reader either overruns or leaves a trailing byte.</summary>
    /// <param name="from">The protocol that builds the frame.</param>
    /// <param name="to">The protocol that must refuse it.</param>
    [Theory]
    [InlineData(498, 573)]
    [InlineData(759, 758)]
    [InlineData(768, 769)]
    [InlineData(769, 768)]
    public void LevelParticlesFrame_DoesNotCrossAFrameBoundary(int from, int to)
    {
        byte[] frame = Encode(from, Packet(3));

        Assert.ThrowsAny<Exception>(() => BoundCodec.At(to, PacketFlow.Clientbound, LevelParticles).DecodeFrame(frame));
    }

    /// <summary>The other three boundaries cannot be caught by a rejection at all, and saying so is the point. Below 1.20.5 the options are captured as the frame REMAINDER (UMPK models no pre-1.20.5 particle option table), so a wide frame read by a narrow codec simply absorbs the extra bytes into the option blob and stays frame-exact; and 765/766 produce equal-width frames. In all three cases the cross decode succeeds and reports a wrong value, which a round-trip or rejection test misses.</summary>
    /// <param name="from">The protocol that builds the frame.</param>
    /// <param name="to">The protocol that decodes it.</param>
    /// <param name="typeIdSurvives">Whether the wrong reader still recovers the particle type id.</param>
    [Theory]
    [InlineData(573, 498, true)]    // the int id still lines up; the coordinates do not
    [InlineData(758, 759, false)]   // the VarInt reader takes the int id's leading zero byte
    [InlineData(765, 766, false)]   // the id moved to the other end of the frame
    public void LevelParticlesFrame_CrossDecodeIsSilentlyWrong(int from, int to, bool typeIdSurvives)
    {
        byte[] frame = Encode(from, Packet(3));

        var decoded = (ClientboundLevelParticlesPacket)BoundCodec
            .At(to, PacketFlow.Clientbound, LevelParticles).DecodeFrame(frame);

        if (typeIdSurvives)
        {
            Assert.Equal(3, decoded.Particle.TypeId);
            Assert.NotEqual(1.0, decoded.X);
        }
        else
            Assert.NotEqual(3, decoded.Particle.TypeId);

    }

    // The particle registry and its option payloads, per era.

    /// <summary><c>minecraft:dust</c> carried a <c>Vector3f</c> colour plus a float scale on 766/767 (16 option bytes) and an int colour plus a float scale from 1.21.2 (8 bytes). The wire id is 13 on both eras, so this is a pure payload difference and only a LENGTH assertion can see it.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="optionBytes">The dust option payload width.</param>
    [Theory]
    [InlineData(766, 16)]
    [InlineData(767, 16)]
    [InlineData(768, 8)]
    public void Dust_PayloadWidthIsTheProtocolsOwn(int protocol, int optionBytes)
    {
        // 16 zero bytes is a valid payload under both readings, so the reader consumes whichever width its era declares and the frame length reports which one that was.
        byte[] frame = Encode(protocol, Packet(13, new byte[16]));

        // The frame must re-encode at header + exactly the era's option width.
        var decoded = (ClientboundLevelParticlesPacket)BoundCodec
            .At(protocol, PacketFlow.Clientbound, LevelParticles).DecodeFrame(frame[..(ParticleLastLength + optionBytes)]);
        Assert.Equal(optionBytes, decoded.Particle.Options.Length);
    }

    /// <summary><c>minecraft:trail</c> arrived at 1.21.2 as <c>TargetColorParticleOption</c> (a Vec3 target plus an int colour, 28 bytes) and gained a duration VarInt at 1.21.4 (<c>TrailParticleOption</c>).</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="wireId">The era's trail particle id.</param>
    /// <param name="optionBytes">The trail option payload width.</param>
    [Theory]
    [InlineData(768, 46, 28)]
    [InlineData(769, 47, 29)]
    [InlineData(770, 48, 29)]
    public void Trail_PayloadWidthIsTheProtocolsOwn(int protocol, int wireId, int optionBytes)
    {
        int headerLength = protocol == 768 ? ParticleLastLength : ParticleLastWithAlwaysShowLength;
        byte[] frame = Encode(protocol, Packet(wireId, new byte[optionBytes]));

        var decoded = (ClientboundLevelParticlesPacket)BoundCodec
            .At(protocol, PacketFlow.Clientbound, LevelParticles).DecodeFrame(frame[..(headerLength + optionBytes)]);
        Assert.Equal(optionBytes, decoded.Particle.Options.Length);
    }

    /// <summary>Protocol 775 must not use the 776 particle table, whose ids are shifted by the geyser family 26.2 inserted near the front. <c>minecraft:item</c> is id 47 on 775 and 54 on 776, and both eras type it, so a 775 frame carrying an item particle decoded under 776 dispatches on <c>vibration</c> instead.</summary>
    [Fact]
    public void ItemParticle_IdIsTheProtocolsOwnOn775And776()
    {
        Assert.Equal(ParticleOptionShape.ItemTemplate, ParticleCodec.ModernV26_1[47]);
        Assert.Equal(ParticleOptionShape.ItemTemplate, ParticleCodec.ModernV26_2[54]);

        // Under the OTHER era's table id 47 and id 54 name different types entirely.
        Assert.Equal(ParticleOptionShape.Vibration, ParticleCodec.ModernV26_1[48]);
        Assert.False(ParticleCodec.ModernV26_2.ContainsKey(47));
    }

    /// <summary>The 26.1 item particle is an <c>ItemStackTemplate</c>, not the count-first stack 1.21.11 used. The 774 and 775 tables therefore have to disagree on the shape as well as the id.</summary>
    [Fact]
    public void ItemParticle_BecomesATemplateAt775()
    {
        Assert.Equal(ParticleOptionShape.Item, ParticleCodec.ModernV1_21_9[47]);
        Assert.Equal(ParticleOptionShape.ItemTemplate, ParticleCodec.ModernV26_1[47]);
    }

    /// <summary><c>effect</c>, <c>instant_effect</c>, <c>dragon_breath</c> and <c>flash</c> gained payloads at 1.21.9, not at 26.2. Leaving them untyped on 773/774 consumed zero option bytes and desynchronized the frame.</summary>
    [Fact]
    public void SpellAndPowerPayloads_ArriveAt773()
    {
        Assert.False(ParticleCodec.ModernV1_21_5.ContainsKey(15));   // 770: effect carries nothing
        Assert.Equal(ParticleOptionShape.IntFloat, ParticleCodec.ModernV1_21_9[16]);   // 773: effect
        Assert.Equal(ParticleOptionShape.IntFloat, ParticleCodec.ModernV1_21_9[46]);   // 773: instant_effect
        Assert.Equal(ParticleOptionShape.Float, ParticleCodec.ModernV1_21_9[8]);       // 773: dragon_breath
        Assert.Equal(ParticleOptionShape.Int, ParticleCodec.ModernV1_21_9[42]);        // 773: flash
    }

    /// <summary>The 770 and 773 tables share almost every id but differ by one from <c>dust</c> onward, because 1.21.9 inserted a type ahead of them. A 773 frame carrying dust decoded under 770 therefore dispatches on <c>dust_color_transition</c> and consumes four extra bytes.</summary>
    [Fact]
    public void DustId_ShiftsBetween770And773()
    {
        Assert.Equal(ParticleOptionShape.DustColor, ParticleCodec.ModernV1_21_5[13]);
        Assert.Equal(ParticleOptionShape.DustTransition, ParticleCodec.ModernV1_21_5[14]);
        Assert.Equal(ParticleOptionShape.DustColor, ParticleCodec.ModernV1_21_9[14]);
        Assert.Equal(ParticleOptionShape.DustTransition, ParticleCodec.ModernV1_21_9[15]);
    }

    // The third table an item particle depends on: the item-component era.

    /// <summary>
    /// 766 and 767 share a frame AND a particle_type ordering, so nothing above this point separates them. They do not share the item-component table, and the <c>item</c> particle option carries a whole count-first component stack on protocols 766-774.
    /// <para>1.21 inserted <c>minecraft:jukebox_playable</c> at component wire id 42, so 767 carries 57 component ids against 766's 56 and every id from 42 up shifts by one. Their particle-type registries remain identical at 109 entries in the same order. The probe below uses <c>minecraft:note_block_sound</c>, which sits at 47 on 766 and 48 on 767; on the other era's table those ids name <c>profile</c> and <c>banner_patterns</c>, neither of which can read an identifier string.</para>
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="componentWireId">That protocol's wire id for minecraft:note_block_sound.</param>
    [Theory]
    [InlineData(766, 47)]
    [InlineData(767, 48)]
    public void ItemParticle_UsesTheProtocolsOwnComponentTable(int protocol, int componentWireId)
    {
        byte[] frame = ItemParticleFrame(componentWireId);

        // Frame LENGTH, derived: 45-byte 766/767 header (bool + 3 doubles + 3 floats + float + int)
        // + 1 particle id + 20 stack bytes (count 1, item id 1, added 1, removed 0, component id,
        // then the identifier string as VarInt 14 + 14 ASCII bytes).
        Assert.Equal(66, frame.Length);

        var decoded = (ClientboundLevelParticlesPacket)BoundCodec
            .At(protocol, PacketFlow.Clientbound, LevelParticles)
            .Decode(frame, Item.ItemTestRegistries.Context);

        Assert.Equal(ItemParticleTypeId, decoded.Particle.TypeId);

        // The option payload is captured verbatim, so its width IS the number of bytes the era's component table consumed. A neighbouring table consumes a different count.
        Assert.Equal(20, decoded.Particle.Options.Length);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        BoundCodec.At(protocol, PacketFlow.Clientbound, LevelParticles)
            .Encode(ref writer, decoded, Item.ItemTestRegistries.Context);
        Assert.Equal(frame, buffer.WrittenSpan.ToArray());
    }

    /// <summary>The cross-era rejection. The particle is the LAST field of a 766/767 level_particles frame, so a component table that consumes the wrong number of bytes cannot hide: it either faults inside the patch or leaves the frame short or long, and <c>BoundPacketCodec.Decode</c> is frame-exact.</summary>
    /// <param name="protocol">The protocol whose codec is fed the other era's frame.</param>
    /// <param name="foreignComponentWireId">The other era's wire id for minecraft:note_block_sound.</param>
    [Theory]
    [InlineData(766, 48)]
    [InlineData(767, 47)]
    public void ItemParticle_ForeignComponentTableIsRejected(int protocol, int foreignComponentWireId) =>
        Assert.ThrowsAny<Exception>(() => BoundCodec
            .At(protocol, PacketFlow.Clientbound, LevelParticles)
            .Decode(ItemParticleFrame(foreignComponentWireId), Item.ItemTestRegistries.Context));

    /// <summary><c>minecraft:item</c>'s particle-registry id on the 766/767 table.</summary>
    private const int ItemParticleTypeId = 44;

    /// <summary>A 766/767 level_particles frame whose particle is <c>minecraft:item</c> carrying a one-component stack. The hand-built header is a bool, 3 doubles, 3 floats, a float, an int, then the particle. The stack is a VarInt count, VarInt item holder id, then the patch as VarInt added, VarInt removed, and one (VarInt component id, payload) pair.</summary>
    private static byte[] ItemParticleFrame(int componentWireId)
    {
        byte[] sound = System.Text.Encoding.UTF8.GetBytes("minecraft:test");
        var bytes = new List<byte> { 0x01 };                       // overrideLimiter
        bytes.AddRange(new byte[24]);                              // x, y, z
        bytes.AddRange(new byte[12]);                              // xDist, yDist, zDist
        bytes.AddRange(new byte[4]);                               // maxSpeed
        bytes.AddRange(new byte[4]);                               // count
        bytes.Add(ItemParticleTypeId);                             // particle type id
        bytes.Add(0x01);                                           // stack count
        bytes.Add((byte)Item.ItemTestRegistries.Stone);            // item holder id
        bytes.Add(0x01);                                           // components added
        bytes.Add(0x00);                                           // components removed
        bytes.Add((byte)componentWireId);
        bytes.Add((byte)sound.Length);
        bytes.AddRange(sound);
        return [.. bytes];
    }
}
