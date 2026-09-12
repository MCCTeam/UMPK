using System.Buffers;
using System.Text;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>
/// Byte-exact pins for the three pre-1.13.2 advancement eras. Advancements arrived at 1.12, but no recorded capture below protocol 393 carries an <c>update_advancements</c> frame and the 393-404 captures carry only an empty tree, so those eras cannot be pinned from the corpus. These frames are therefore literal wire bytes with a full, non-empty DisplayInfo. Each frame includes an era-specific item id, a background texture, non-zero flags, coordinates, criteria, requirements, and progress. An empty tree decodes identically under every era shape, so each frame contains populated fields.
/// <para>Each frame orders parent, display, criteria, and requirements before progress. Display fields contain title, description, icon, frame, flags, an optional background, and coordinates. The eras differ only in the icon slot: 1.12 uses a short id with damage, 1.13 removes damage, and 1.13.2 adds a presence flag before a VarInt id.</para>
/// </summary>
public sealed class AdvancementNumericIdFramePinTests
{
    private const string Title = "{\"translate\":\"advancements.story.root.title\"}";
    private const string Description = "{\"translate\":\"advancements.story.root.description\"}";
    private const string Background = "minecraft:textures/gui/advancements/backgrounds/stone.png";

    /// <summary>1.12-1.12.2 (335-340): the icon is the legacy slot (short item id, byte count, short damage, NBT-or-0). Diamond is item id 264 damage 0 on this era, which the legacy registry keys as the composite 264 &lt;&lt; 16.</summary>
    [Fact]
    public void Protocol340_LegacyIconSlot_DecodesAndReEncodesByteExact()
    {
        byte[] frame = Build(w =>
        {
            w.I16(264);   // legacy item id: diamond
            w.U8(1);      // count
            w.I16(0);     // damage
            w.U8(0);      // no NBT
        });

        ClientboundUpdateAdvancementsPacket p = Check(AdvancementCodecs.UpdateAdvancementsV1_12, frame, 340);
        AdvancementDisplayInfo display = Assert.IsType<AdvancementDisplayInfo>(p.Added[0].Value.Display);
        Assert.Equal("minecraft:diamond", display.Icon.Item.Id.ToString());
        Assert.Equal(1, display.Icon.Count);
    }

    /// <summary>1.13-1.13.1 (393-401): the flattening dropped the damage short, so the icon is short id, byte count, named-root optional NBT. Diamond is flat id 476 on 1.13.</summary>
    [Fact]
    public void Protocol393_ShortIdIconSlot_DecodesAndReEncodesByteExact()
    {
        byte[] frame = Build(w =>
        {
            w.I16(476);   // flat item id: diamond on 1.13
            w.U8(1);      // count
            w.U8(0);      // no NBT
        });

        ClientboundUpdateAdvancementsPacket p = Check(AdvancementCodecs.UpdateAdvancementsV1_13, frame, 393);
        AdvancementDisplayInfo display = Assert.IsType<AdvancementDisplayInfo>(p.Added[0].Value.Display);
        Assert.Equal("minecraft:diamond", display.Icon.Item.Id.ToString());
    }

    /// <summary>1.13.2-1.19.4 (404-762): the 1.13.2 Slot flip to a present flag plus a VarInt id. Diamond is flat id 481 on 1.13.2.</summary>
    [Fact]
    public void Protocol404_PresentIdIconSlot_DecodesAndReEncodesByteExact()
    {
        byte[] frame = Build(w =>
        {
            w.U8(1);        // present
            w.VarInt(481);  // flat item id: diamond on 1.13.2
            w.U8(1);        // count
            w.U8(0);        // no NBT
        });

        ClientboundUpdateAdvancementsPacket p = Check(AdvancementCodecs.UpdateAdvancementsV1_13_2, frame, 404);
        AdvancementDisplayInfo display = Assert.IsType<AdvancementDisplayInfo>(p.Added[0].Value.Display);
        Assert.Equal("minecraft:diamond", display.Icon.Item.Id.ToString());
    }

    /// <summary>The era boundaries are real, not cosmetic: each of the three frames above is rejected by the two neighbouring eras' codecs. The icon slot forms have different lengths, so a wrong era either runs past the frame or leaves bytes unread.</summary>
    [Fact]
    public void EachIconSlotWireLayout_RejectsTheNeighbouringWireLayouts()
    {
        byte[] legacy = Build(w => { w.I16(264); w.U8(1); w.I16(0); w.U8(0); });
        byte[] shortId = Build(w => { w.I16(476); w.U8(1); w.U8(0); });
        byte[] presentId = Build(w => { w.U8(1); w.VarInt(481); w.U8(1); w.U8(0); });

        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_13, legacy, 393);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_13_2, legacy, 404);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_12, shortId, 340);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_13_2, shortId, 404);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_12, presentId, 340);
        AssertRejects(AdvancementCodecs.UpdateAdvancementsV1_13, presentId, 393);
    }

    // shared frame body

    /// <summary>One advancement with a full DisplayInfo whose icon bytes are supplied by the era, plus a second advancement with no display, a removal, and a progress map with one obtained and one unobtained criterion. Everything carries a distinct non-default value.</summary>
    private static byte[] Build(Action<Writer> icon)
    {
        var w = new Writer();
        w.U8(1);                                        // reset = true
        w.VarInt(2);                                    // added count

        w.Str("minecraft:story/root");
        w.U8(0);                                        // no parent
        w.U8(1);                                        // has display
        w.Str(Title);
        w.Str(Description);
        icon(w);
        w.VarInt(1);                                    // frame = challenge
        w.I32(0b111);                                   // background + show toast + hidden
        w.Str(Background);
        w.F32(0.5f);
        w.F32(-3.25f);
        w.VarInt(2);                                    // criteria
        w.Str("crafted_stone");
        w.Str("mined_dirt");
        w.VarInt(2);                                    // requirement groups
        w.VarInt(1);
        w.Str("crafted_stone");
        w.VarInt(1);
        w.Str("mined_dirt");

        w.Str("minecraft:story/child");
        w.U8(1);                                        // has parent
        w.Str("minecraft:story/root");
        w.U8(0);                                        // no display
        w.VarInt(1);                                    // criteria
        w.Str("impossible");
        w.VarInt(1);                                    // requirement groups
        w.VarInt(1);
        w.Str("impossible");

        w.VarInt(1);                                    // removed count
        w.Str("minecraft:story/removed");

        w.VarInt(1);                                    // progress count
        w.Str("minecraft:story/root");
        w.VarInt(2);
        w.Str("crafted_stone");
        w.U8(1);
        w.I64(1_700_000_000_123L);
        w.Str("mined_dirt");
        w.U8(0);
        return w.ToArray();
    }

    private static ClientboundUpdateAdvancementsPacket Check(
        PacketCodec<ClientboundUpdateAdvancementsPacket> codec, byte[] frame, int protocol)
    {
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var reader = new PacketReader(frame);
        ClientboundUpdateAdvancementsPacket p = codec.Decode(ref reader, context);
        Assert.Equal(0, reader.Remaining);

        Assert.True(p.Reset);
        Assert.Equal(2, p.Added.Count);
        Assert.Equal("minecraft:story/removed", Assert.Single(p.Removed).ToString());

        AdvancementNode root = p.Added[0].Value;
        Assert.Null(root.Parent);
        Assert.Equal(["crafted_stone", "mined_dirt"], [.. root.Criteria]);
        Assert.Equal(2, root.Requirements.Count);
        Assert.False(root.SendsTelemetryEvent);

        AdvancementDisplayInfo display = Assert.IsType<AdvancementDisplayInfo>(root.Display);
        Assert.Equal(AdvancementFrameType.Challenge, display.Frame);
        Assert.Equal(Background, display.Background?.ToString());
        Assert.True(display.ShowToast);
        Assert.True(display.Hidden);
        Assert.Equal(0.5f, display.X);
        Assert.Equal(-3.25f, display.Y);
        Assert.Equal("advancements.story.root.title", Assert.IsType<Umpk.Text.TranslatableContent>(display.Title.Content).Key);

        Assert.Equal("minecraft:story/root", p.Added[1].Value.Parent?.ToString());
        Assert.Null(p.Added[1].Value.Display);

        AdvancementProgressEntry progress = Assert.Single(p.Progress);
        Assert.Equal(1_700_000_000_123L, progress.Criteria[0].ObtainedEpochMillis);
        Assert.Null(progress.Criteria[1].ObtainedEpochMillis);

        var buffer = new ArrayBufferWriter<byte>(frame.Length + 8);
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, p, context);
        Assert.Equal(frame, buffer.WrittenSpan.ToArray());
        return p;
    }

    private static void AssertRejects(
        PacketCodec<ClientboundUpdateAdvancementsPacket> codec, byte[] frame, int protocol)
    {
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        bool rejected;
        try
        {
            var reader = new PacketReader(frame);
            codec.Decode(ref reader, context);
            rejected = reader.Remaining != 0;
        }
        catch (Exception)
        {
            rejected = true;
        }

        Assert.True(rejected, "a neighbouring era's codec consumed the frame exactly, so the boundary is not pinned");
    }

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void U8(int value) => _bytes.Add((byte)value);

        public void VarInt(int value)
        {
            uint v = (uint)value;
            while ((v & ~0x7Fu) != 0)
            {
                _bytes.Add((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }

            _bytes.Add((byte)v);
        }

        public void I16(short value)
        {
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }

        public void I32(int value)
        {
            for (int shift = 24; shift >= 0; shift -= 8)
                _bytes.Add((byte)(value >> shift));

        }

        public void I64(long value)
        {
            for (int shift = 56; shift >= 0; shift -= 8)
                _bytes.Add((byte)(value >> shift));

        }

        public void F32(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            _bytes.AddRange(bytes);
        }

        public void Str(string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            VarInt(utf8.Length);
            _bytes.AddRange(utf8);
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
